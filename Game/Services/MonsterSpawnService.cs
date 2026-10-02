using System;
using System.Collections.Generic;
using System.Linq;
using Navislamia.Game.Network;
using Navislamia.Game.Network.Clients;
using Navislamia.Game.Network.Packets.Game;
using Serilog;

namespace Navislamia.Game.Services;

public class MonsterSpawnService : IMonsterSpawnService
{
    private readonly ILogger _logger = Log.ForContext<MonsterSpawnService>();
    private readonly MonsterWorldState _worldState;

    public MonsterSpawnService(MonsterWorldState worldState)
    {
        _worldState = worldState;
    }

    public void Sync(GameClient client)
    {
        try
        {
            var info = client.ConnectionInfo;
            var inRange = _worldState.WithinRange(info.X, info.Y, WorldVisibility.ViewRange)
                .Where(monster => monster.Layer == info.Layer).ToArray();

            WorldObjectStreamer.Stream(client, info.MonsterVisibilityLock, inRange,
                monster => monster.InstanceId,
                (monster, handle) =>
                {
                    // Only read for a monster that is actually entering, so the state lock is not
                    // touched once per visible monster per sync.
                    var (x, y) = _worldState.GetPosition(monster.InstanceId);
                    return GameSpawnPackets.BuildEnterMonster(handle, x, y, monster.Z, info.Layer,
                        _worldState.GetHp(monster.InstanceId), monster.Level, monster.Race,
                        monster.MonsterId, monster.FaceDirection);
                },
                info.SpawnedMonsters,
                // Asked only for a monster about to enter (WorldObjectStreamer): this used to copy the set of
                // every dead monster in the world on every sync, i.e. on every step of every player.
                canEnter: monster => _worldState.IsAlive(monster.InstanceId),
                // A monster that enters a view brings the states it already carries
                // (docs/packet-specs/socle-etats-monstre-entree.md) : the official server puts them in
                // SendEnterMsg, right after the ENTER frame. Sent here, with the handle the ENTER was built
                // for, and after that handle is recorded — a pose landing at the same instant finds this
                // observer by it and must not lose the state it just applied.
                onEntered: (monster, handle) => SendStates(client, monster.InstanceId, handle));
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "{clientTag} monster sync failed", client.ClientTag);
        }
    }

    /// <summary>
    /// The <c>TM_SC_STATE</c> (505) frames a monster's enter frame is followed by, one per active state,
    /// to the single client whose view just received it.
    /// </summary>
    private void SendStates(GameClient client, long instanceId, uint handle)
    {
        var states = _worldState.GetStates(instanceId);
        if (states.Count == 0)
        {
            return;
        }

        var now = ServerClock.Now;
        foreach (var state in states)
        {
            // A state whose deadline has passed is not announced: the expiry tick runs every 500 ms, so
            // between two ticks a state can be over and still be in the list — the client would get a
            // removal frame for a state it never saw (reserve §7.2 of the fiche).
            if (unchecked((int)(now - state.EndTick)) >= 0)
            {
                continue;
            }

            client.Connection.Send(GameSkillPackets.BuildState(handle, state.StateHandle, (uint)state.StateId,
                (ushort)state.StateLevel, state.EndTick, state.StartTick));
        }
    }
}
