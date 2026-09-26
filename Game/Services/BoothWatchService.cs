using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Navislamia.Game.DataAccess.Entities.Telecaster;
using Navislamia.Game.Network.Clients;
using Navislamia.Game.Network.Packets;
using Navislamia.Game.Network.Packets.Enums;
using Navislamia.Game.Network.Packets.Game;
using Serilog;

namespace Navislamia.Game.Services;

/// <summary>
/// The visibility socle of the player booth: it makes the booth a player declared with
/// <c>TM_CS_START_BOOTH</c> (700) observable, and nothing more. The 7.3 client is the whole authority
/// here — NGemity handles none of the family (<c>Chihiro/src/Network/Messages.cpp:573-577</c> are
/// comments) — so every rule below is one of three kinds: measured in the client, decided by
/// Navislamia, or carried verbatim.
///
/// What the service does on a <c>702</c>: read the 11 byte frame, find the session that declared the
/// booth handle among the authorised clients, resolve <strong>each declared item against the owner's own
/// inventory</strong> (<c>ICharacterService.GetItemByHandleAsync</c>) and answer the requester with a
/// <c>TM_SC_WATCH_BOOTH</c> (703). The declared triplet only supplies the handle and the price; the 75
/// byte motif comes from the inventory, because the server cannot invent a code, a uid, an endurance or
/// the sockets (docs/packet-specs/socle-booths-visibilite.md §5.2 point 3). No frame is sent to anyone
/// else on 700, 702 or 704 (§5.2 point 8).
///
/// Three decisions the specification left open, taken here and reported in the packet sheet's
/// "A VERIFIER" section (the reference server is not available to settle them):
/// <list type="bullet">
/// <item><description>a <c>702</c> naming a booth that is not open is refused with
/// <see cref="ResultCode.NotExist"/> (1) and the handle as value, the code the repository already uses
/// when a handle resolves to nothing (crafting socle §9.2, 203 drop §5.3) — §7.2;</description></item>
/// <item><description>a declared handle that no longer resolves is <strong>skipped</strong>: the record
/// is left out and <c>count</c> drops with it, so the window stays coherent instead of failing
/// entirely — §7.6;</description></item>
/// <item><description>the observation is forgotten on <c>704</c> and with the character session, and
/// nowhere else: when the owner closes its booth (<c>701</c>) or leaves, the server has no channel to
/// tell the observer, and the next <c>702</c> is what refreshes the window — §7.9.</description></item>
/// </list>
/// </summary>
public class BoothWatchService : IBoothWatchService
{
    private readonly ILogger _logger = Log.ForContext<BoothWatchService>();
    private readonly ICharacterService _characterService;

    public BoothWatchService(ICharacterService characterService)
    {
        _characterService = characterService;
    }

    public async Task HandleWatchAsync(GameClient client, byte[] packet, IEnumerable<GameClient> sessions)
    {
        // Nothing to answer before the character is in the world: no booth can be named by a session that
        // holds no character. Same precedent as the crafting socle (CraftingSocleService.HandleAsync) and
        // TM_CS_GET_REGION_INFO's handler.
        if (client.ConnectionInfo.CharacterHandle == 0)
        {
            _logger.Debug("TM_CS_WATCH_BOOTH received from {clientTag} outside the world, dropped", client.ClientTag);
            return;
        }

        if (!BoothPackets.TryReadWatchBooth(packet, out var target))
        {
            _logger.Warning("Malformed TM_CS_WATCH_BOOTH from {clientTag} (Length: {length}), refused with InvalidArgument",
                client.ClientTag, packet.Length);
            client.SendResult((ushort)GamePackets.TM_CS_WATCH_BOOTH, (ushort)ResultCode.InvalidArgument);
            return;
        }

        // The booth handle is matched against the CharacterHandle of a session holding an open booth: the
        // repository has no handle → connection index, so the authorised clients are scanned (§5.2 point 7).
        // The scan is bounded by the number of connected players and only runs on a player action.
        if (!TryFindBoothOwner(Sessions(sessions), target, out var owner))
        {
            _logger.Debug("TM_CS_WATCH_BOOTH from {clientTag} names booth {target}, which no open booth serves, refused",
                client.ClientTag, target);
            client.SendResult((ushort)GamePackets.TM_CS_WATCH_BOOTH, (ushort)ResultCode.NotExist,
                unchecked((int)target));
            return;
        }

        var booth = owner.Booth;
        if (booth is null)
        {
            // The owner closed its booth between the scan and this line: same answer as an unknown handle.
            _logger.Debug("TM_CS_WATCH_BOOTH from {clientTag} names booth {target}, closed meanwhile, refused",
                client.ClientTag, target);
            client.SendResult((ushort)GamePackets.TM_CS_WATCH_BOOTH, (ushort)ResultCode.NotExist,
                unchecked((int)target));
            return;
        }

        var items = new List<BoothWatchItem>(booth.Items.Length);
        foreach (var declared in booth.Items)
        {
            ItemEntity item;
            try
            {
                item = await _characterService.GetItemByHandleAsync(owner.CharacterName, declared.ItemHandle);
            }
            catch (Exception exception)
            {
                _logger.Error(exception, "Could not read item {itemHandle} of {owner} for {clientTag}",
                    declared.ItemHandle, owner.CharacterName, client.ClientTag);
                client.SendResult((ushort)GamePackets.TM_CS_WATCH_BOOTH, (ushort)ResultCode.DBError,
                    unchecked((int)declared.ItemHandle));
                return;
            }

            if (item is null)
            {
                // §7.6: the item moved, was consumed or was sold since the 700. Skipping keeps count and
                // length coherent, which the client never checks itself (§3.6).
                _logger.Debug("Booth {target} of {owner} declares item {itemHandle}, which is no longer in the bag: skipped",
                    target, owner.CharacterName, declared.ItemHandle);
                continue;
            }

            items.Add(new BoothWatchItem(ItemFixedInfo.FromItem(item), declared.Gold));
        }

        client.ConnectionInfo.BeginWatchingBooth(target);
        client.Connection.Send(BoothPackets.BuildWatchBooth(target, booth.Type, items));

        _logger.Debug("TM_CS_WATCH_BOOTH from {clientTag}: booth {target} of {owner} answered with {count} item(s)",
            client.ClientTag, target, owner.CharacterName, items.Count);
    }

    public void HandleStopWatch(GameClient client, byte[] packet)
    {
        if (!BoothPackets.TryReadStopWatchBooth(packet, out var target))
        {
            _logger.Warning(
                "Malformed TM_CS_STOP_WATCH_BOOTH from {clientTag} (Length: {length}), refused with InvalidArgument",
                client.ClientTag, packet.Length);
            client.SendResult((ushort)GamePackets.TM_CS_STOP_WATCH_BOOTH, (ushort)ResultCode.InvalidArgument);
            return;
        }

        var wasWatching = client.ConnectionInfo.StopWatchingBooth();
        _logger.Debug("TM_CS_STOP_WATCH_BOOTH from {clientTag}: booth {target} was {state}", client.ClientTag, target,
            wasWatching ? "being watched" : "not watched");

        client.SendResult((ushort)GamePackets.TM_CS_STOP_WATCH_BOOTH, (ushort)ResultCode.Success);
    }

    /// <summary>
    /// The missing primitive of §5.2 point 7: the handle → session lookup. A handle is the booth of the
    /// session whose <see cref="ConnectionInfo.CharacterHandle"/> equals it and which holds an open booth
    /// — the identity the specification sanctions for the owner (§5.2 point 4), and the only one the
    /// repository can build without a new registry. Pure and lock free: each session's booth state is read
    /// through its own lock.
    /// </summary>
    public static bool TryFindBoothOwner(IEnumerable<ConnectionInfo> sessions, uint boothHandle,
        out ConnectionInfo owner)
    {
        owner = null;

        if (boothHandle == 0)
        {
            return false;
        }

        foreach (var session in sessions)
        {
            if (session.CharacterHandle == boothHandle && session.IsBoothOpen)
            {
                owner = session;
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// The sessions behind the connected clients. <c>AuthorizedGameClients</c> is indexed by account name,
    /// so the scan this service performs is over its values (§5.2 point 7).
    /// </summary>
    private static IEnumerable<ConnectionInfo> Sessions(IEnumerable<GameClient> clients)
    {
        foreach (var connected in clients)
        {
            yield return connected.ConnectionInfo;
        }
    }
}
