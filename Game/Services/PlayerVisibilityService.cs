using System;
using System.Collections.Generic;
using Microsoft.Extensions.Logging;
using Navislamia.Game.Network.Clients;
using Navislamia.Game.Network.Packets;
using Navislamia.Game.Network.Packets.Enums;
using Navislamia.Game.Network.Packets.Game;
using Navislamia.Game.Services.Interfaces;

namespace Navislamia.Game.Services;

/// <summary>
/// The streaming service of the player visibility socle: it keeps the index of the presences in step
/// with the sessions, and it is the only place that emits the three player frames — the <c>ENTER</c>
/// of a peer, its <c>LEAVE</c>, and the walker's <c>MOVE</c>
/// (docs/packet-specs/socle-visibilite-joueurs.md §5.3).
/// </summary>
/// <remarks>
/// <para>
/// Three rules make it coherent (docs/packet-specs/socle-visibilite-joueurs.md §5.4):
/// </para>
/// <list type="number">
/// <item>
/// <b>Reciprocity.</b> A entering B's window means B enters A's: a synchronisation reconciles both
/// sides of every pair, so the two views can never disagree (A sees B ⟺ B sees A, NGemity
/// <c>Functors.cpp:25-35</c>).
/// </item>
/// <item>
/// <b>One lock at a time.</b> The client's visibility lock is taken, the delta computed, the lock
/// released, and only then the next client's lock is taken. Two visibility locks are never nested:
/// that is the interlocking the reference avoids and the trap of this lot.
/// </item>
/// <item>
/// <b>Each frame is queued under its recipient's lock.</b> <c>Connection.Send</c> only enqueues on an
/// unbounded channel, so it costs nothing to hold the lock across it, and it is what keeps a recipient's
/// frames in the order its view changed. Queued after the release, an <c>ENTER</c> built by one thread
/// could reach the client after the <c>LEAVE</c> another thread built later, and that client would keep a
/// ghost until it reconnects.
/// </item>
/// </list>
/// <para>
/// What is deliberately <b>not</b> done here: the <c>bIsStopMessage</c> flag of a region update (§7.5),
/// and any validation of a walk (speed, points),
/// which the repository already bounds. The handle of an incoming <c>MOVE_REQUEST</c> is checked by
/// <c>GameClient</c> before this service is reached (§7.7).
/// </para>
/// <para>
/// The entry of a player in the world is <b>not</b> streamed through <c>WorldObjectStreamer</c>: that
/// loop holds a per-client handle map and allocates a handle per client
/// (<c>WorldObjectStreamer.cs:63</c>), while a player is seen under the one handle every observer
/// shares, <c>character.Id</c> — the handle <c>TS_SC_MOVE</c> and <c>TS_SC_LEAVE</c> carry. Its
/// <c>canEnter</c> predicate and its card hold have no equivalent for a character either.
/// </para>
/// </remarks>
public sealed class PlayerVisibilityService : IPlayerVisibilityService
{
    private readonly ILogger<PlayerVisibilityService> _logger;
    private readonly PlayerVisibilityIndex _index = new();
    private readonly PlayerRegistry _registry = new();

    public PlayerVisibilityService(ILogger<PlayerVisibilityService> logger)
    {
        _logger = logger;
    }

    public PlayerRegistry Registry => _registry;

    public PlayerVisibilityIndex Index => _index;

    public void EnterWorld(GameClient client)
    {
        var info = client.ConnectionInfo;

        if (info.CharacterHandle == 0 || info.Appearance is null)
        {
            _logger.LogWarning("{clientTag} entered the world without a character handle or an appearance: no visibility",
                client.ClientTag);
            return;
        }

        var presence = new PlayerPresence(info.CharacterHandle, info.Appearance, info.Layer, info.X, info.Y,
            info.Z);

        if (!_index.Add(presence))
        {
            _logger.LogWarning("{clientTag} entered the world as handle {handle}, which another presence held",
                client.ClientTag, presence.Handle);
        }

        if (!_registry.Register(presence.Handle, client))
        {
            _logger.LogWarning("{clientTag} took the handle {handle} over from another session", client.ClientTag,
                presence.Handle);
        }

        Sync(client);
    }

    public void Sync(GameClient client)
    {
        if (client is null)
        {
            throw new ArgumentNullException(nameof(client));
        }

        var info = client.ConnectionInfo;

        if (info.CharacterHandle == 0 || !_index.TryGet(info.CharacterHandle, out var mine))
        {
            return;
        }

        // The session's place is the authority, and a synchronisation is also how a region border is
        // taken into account: the client reports a new place, emits no path, and nothing else would
        // move the entry — so the presence is re-indexed before the window is computed. On the walk
        // path OnMove has already done it; moving twice is a no-op.
        _index.Move(mine.Handle, info.X, info.Y);

        var peers = _index.PeersInViewOf(mine.Handle);
        var inView = new HashSet<uint>(peers.Count);
        foreach (var peer in peers)
        {
            inView.Add(peer.Handle);
        }

        var gone = new List<KeyValuePair<long, uint>>();
        var now = ServerClock.Now;

        // (1) My own view, under my own lock only.
        lock (info.PlayerVisibilityLock)
        {
            foreach (var peer in peers)
            {
                if (info.SpawnedPlayers.ContainsKey(peer.Handle))
                {
                    continue;
                }

                if (!_registry.TryResolve(peer.Handle, out var peerClient))
                {
                    // The index and the registry are written together, so this only happens while a
                    // session is torn down: the peer is not shown, and it is not remembered either.
                    _logger.LogDebug("No session for the handle {handle} in the window of {clientTag}", peer.Handle,
                        client.ClientTag);
                    continue;
                }

                info.SpawnedPlayers[peer.Handle] = peer.Handle;
                Send(client, BuildEnterFrame(peer, peerClient.ConnectionInfo));
                if (peerClient.ConnectionInfo.WearFrame is { } peerWear)
                    Send(client, peerWear);
                SendAll(client, CompanionFrames.Enter(peerClient.ConnectionInfo, now));
                SendAll(client, CompanionFrames.States(peerClient.ConnectionInfo));
            }

            foreach (var entry in info.SpawnedPlayers)
            {
                if (!inView.Contains((uint)entry.Key))
                {
                    gone.Add(entry);
                }
            }

            foreach (var entry in gone)
            {
                info.SpawnedPlayers.Remove(entry.Key);
                if (_registry.TryResolve(entry.Value, out var goneClient))
                {
                    SendAll(client, CompanionFrames.Leave(goneClient.ConnectionInfo));
                }

                Send(client, GameSpawnPackets.BuildLeave(entry.Value));
            }
        }

        List<byte[]> myCompanions = null;
        List<byte[]> myStates = null;

        // (2) The other side of every pair in range, one lock at a time, never nested.
        foreach (var peer in peers)
        {
            if (!_registry.TryResolve(peer.Handle, out var peerClient))
            {
                continue;
            }

            var peerInfo = peerClient.ConnectionInfo;

            lock (peerInfo.PlayerVisibilityLock)
            {
                if (peerInfo.SpawnedPlayers.ContainsKey(mine.Handle))
                {
                    continue;
                }

                peerInfo.SpawnedPlayers[mine.Handle] = mine.Handle;
                Send(peerClient, BuildEnterFrame(mine, info));
                if (info.WearFrame is { } mineWear)
                    Send(peerClient, mineWear);
                SendAll(peerClient, myCompanions ??= CompanionFrames.Enter(info, now));
                SendAll(peerClient, myStates ??= CompanionFrames.States(info));
            }
        }

        // (3) The pairs that dropped out of my window: they must forget me, exactly as I forgot them.
        foreach (var entry in gone)
        {
            if (!_registry.TryResolve(entry.Value, out var peerClient))
            {
                continue;
            }

            var peerInfo = peerClient.ConnectionInfo;

            lock (peerInfo.PlayerVisibilityLock)
            {
                if (peerInfo.SpawnedPlayers.Remove(mine.Handle))
                {
                    SendAll(peerClient, CompanionFrames.Leave(info));
                    Send(peerClient, GameSpawnPackets.BuildLeave(mine.Handle));
                }
            }
        }
    }

    public void OnMove(GameClient walker, byte[] waypoints)
    {
        var info = walker.ConnectionInfo;

        if (info.CharacterHandle == 0 || !_index.TryGet(info.CharacterHandle, out var mine))
        {
            return;
        }

        _index.Move(mine.Handle, info.X, info.Y);
        Sync(walker);

        if (waypoints.Length == 0)
        {
            // A request without a waypoint is a stop. Only the walker's own echo describes it.
            return;
        }

        var now = ServerClock.Now;

        foreach (var peer in _index.PeersInViewOf(mine.Handle))
        {
            if (!_registry.TryResolve(peer.Handle, out var peerClient))
            {
                continue;
            }

            var peerInfo = peerClient.ConnectionInfo;

            lock (peerInfo.PlayerVisibilityLock)
            {
                if (peerInfo.SpawnedPlayers.ContainsKey(mine.Handle))
                {
                    Send(peerClient, GameMovePackets.BuildMove(mine.Handle,
                        unchecked(now + peerInfo.ClientClockOffset), mine.Layer, info.MoveSpeed,
                        waypoints));
                }
            }
        }
    }

    public void LeaveWorld(GameClient client, bool notifyWalker = false)
    {
        var info = client.ConnectionInfo;
        var handle = info.CharacterHandle;

        if (handle == 0)
        {
            PurgeOwnView(info);
            return;
        }

        // The observers are read before the removal: afterwards the index no longer knows the place.
        var observers = _index.PeersInViewOf(handle);

        _index.Remove(handle);
        _registry.Unregister(handle, client);

        if (notifyWalker)
        {
            // The warp case: the client keeps every object it was told about, so it is told the peers
            // it could see are gone — the same job LeaveEverything does for NPCs, monsters and props.
            lock (info.PlayerVisibilityLock)
            {
                foreach (var seen in info.SpawnedPlayers.Values)
                {
                    if (_registry.TryResolve(seen, out var seenClient))
                    {
                        SendAll(client, CompanionFrames.Leave(seenClient.ConnectionInfo));
                    }

                    Send(client, GameSpawnPackets.BuildLeave(seen));
                }

                info.SpawnedPlayers.Clear();
            }
        }
        else
        {
            // A real exit: the client is leaving the world, nothing has to be said to it.
            PurgeOwnView(info);
        }

        foreach (var observer in observers)
        {
            if (!_registry.TryResolve(observer.Handle, out var observerClient))
            {
                continue;
            }

            var observerInfo = observerClient.ConnectionInfo;

            lock (observerInfo.PlayerVisibilityLock)
            {
                if (observerInfo.SpawnedPlayers.Remove(handle))
                {
                    SendAll(observerClient, CompanionFrames.Leave(info));
                    Send(observerClient, GameSpawnPackets.BuildLeave(handle));
                }
            }
        }
    }

    public IReadOnlyList<GameClient> Observers(GameClient subject)
    {
        var handle = subject.ConnectionInfo.CharacterHandle;
        var observers = new List<GameClient>();
        if (handle == 0 || !_index.TryGet(handle, out _))
        {
            return observers;
        }

        foreach (var peer in _index.PeersInViewOf(handle))
        {
            if (!_registry.TryResolve(peer.Handle, out var peerClient))
            {
                continue;
            }

            lock (peerClient.ConnectionInfo.PlayerVisibilityLock)
            {
                if (peerClient.ConnectionInfo.SpawnedPlayers.ContainsKey(handle))
                {
                    observers.Add(peerClient);
                }
            }
        }

        return observers;
    }

    public void SendToObservers(GameClient subject, byte[] frame, bool includeSelf = false)
    {
        var handle = subject.ConnectionInfo.CharacterHandle;
        if (includeSelf)
        {
            Send(subject, frame);
        }

        if (handle == 0 || !_index.TryGet(handle, out _))
        {
            return;
        }

        foreach (var peer in _index.PeersInViewOf(handle))
        {
            if (!_registry.TryResolve(peer.Handle, out var peerClient))
            {
                continue;
            }

            var peerInfo = peerClient.ConnectionInfo;
            lock (peerInfo.PlayerVisibilityLock)
            {
                if (peerInfo.SpawnedPlayers.ContainsKey(handle))
                {
                    Send(peerClient, frame);
                }
            }
        }
    }

    /// <summary>
    /// The <c>TS_SC_ENTER</c> of a player, as the 7.3 client reads it: the fixed physical traits come
    /// from the presence snapshot the session captured at login, everything that changes during the
    /// session is read live, and the handle is the shared one (<c>character.Id</c>). 118 bytes, see
    /// docs/packet-specs/socle-visibilite-joueurs.md §3.2 and §3.3.
    /// </summary>
    public static byte[] BuildEnterFrame(PlayerPresence presence, ConnectionInfo info)
    {
        var appearance = presence.Appearance;

        var enter = new TS_SC_ENTER_PLAYER
        {
            Type = 0,
            Handle = presence.Handle,
            X = presence.X,
            Y = presence.Y,
            Z = presence.Z,
            Layer = presence.Layer,
            ObjType = 0,
            Status = ActorStatus.ForPlayer(info),
            FaceDirection = 0,
            Hp = info.CharacterHp,
            MaxHp = info.CharacterMaxHp,
            Mp = info.CharacterMp,
            // The session carries no maximum mana: the local entry of GameActions uses the current mana
            // for it too (Actions/GameActions.cs:191-192), and this frame does the same rather than
            // invent a value.
            MaxMp = info.CharacterMp,
            Level = info.CharacterLevel,
            Race = appearance.Race,
            SkinColor = appearance.SkinColor,
            IsFirstEnter = 1,
            Energy = 0,
            Sex = appearance.Sex,
            FaceId = appearance.FaceId,
            FaceTextureId = appearance.FaceTextureId,
            HairId = appearance.HairId,
            HairColorIndex = appearance.HairColorIndex,
            HairColorRGB = appearance.HairColorRgb,
            HideEquipFlag = appearance.HideEquipFlag,
            Name = info.CharacterName,
            JobId = (ushort)info.CharacterJob,
            RideHandle = 0,
            GuildId = (uint)(info.GuildId ?? 0),
        };

        return new Packet<TS_SC_ENTER_PLAYER>((ushort)GamePackets.TM_SC_ENTER, enter).Data;
    }

    private static void PurgeOwnView(ConnectionInfo info)
    {
        lock (info.PlayerVisibilityLock)
        {
            info.SpawnedPlayers.Clear();
        }
    }

    /// <summary>
    /// Queues one frame, under its recipient's visibility lock (see the class remarks); one broken socket
    /// never costs the other observers theirs.
    /// </summary>
    private void SendAll(GameClient recipient, List<byte[]> frames)
    {
        foreach (var frame in frames)
        {
            Send(recipient, frame);
        }
    }

    private void Send(GameClient recipient, byte[] data)
    {
        try
        {
            recipient.Connection.Send(data);
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Could not send the player visibility frame to {clientTag}",
                recipient.ClientTag);
        }
    }
}
