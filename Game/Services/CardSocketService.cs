using System;
using System.Threading.Tasks;
using Navislamia.Game.Network.Clients;
using Navislamia.Game.Network.Packets;
using Navislamia.Game.Network.Packets.Enums;
using Navislamia.Game.Network.Packets.Game;
using Serilog;

namespace Navislamia.Game.Services;

/// <summary>
/// Handles <c>TM_CS_PUTON_CARD</c> (214), socketing a soul stone into an equipment socket. The scope
/// is the one the fiche fixes: read the frame, resolve the target under the retained reading, judge it
/// through <see cref="CardSocketRules"/>, socket the stone, answer.
/// <para>
/// The stone's bonus is not applied to the character's stats: the reference recomputes stats after
/// socketing but Navislamia's stat calculator does not read sockets yet. The cost some references
/// charge for socketing is deliberately not charged either (fiche §7.3).
/// </para>
/// </summary>
public class CardSocketService : ICardSocketService
{
    private const ushort SocketCardRequestId = (ushort)GamePackets.TM_CS_PUTON_CARD;

    private readonly ILogger _logger = Log.ForContext<CardSocketService>();
    private readonly ICharacterService _characterService;
    private readonly ICardSocketCatalog _catalog;

    public CardSocketService(ICharacterService characterService, ICardSocketCatalog catalog)
    {
        _characterService = characterService;
        _catalog = catalog;
    }

    public async Task SocketAsync(GameClient client, GameActionPackets.PutonCardRequest request)
    {
        var info = client.ConnectionInfo;
        var cardValue = unchecked((int)request.ItemHandle);

        if (request.Position < 0 || request.Position >= GameCharacterPackets.WearSlots)
        {
            client.SendResult(SocketCardRequestId, (ushort)ResultCode.InvalidArgument, cardValue);
            return;
        }

        // The named decision point of the retained reading (fiche §7.1): position is a wear slot.
        var target = CardSocketRules.ResolveTarget(request.Position, request.ItemHandle);

        CardSocketResult result;
        try
        {
            result = await _characterService.SocketCardAsync(info.CharacterName, target.Slot,
                target.CardHandle, _catalog);
        }
        catch (Exception exception)
        {
            _logger.Error(exception, "Could not socket card {cardHandle} into slot {position} for {clientTag}",
                request.ItemHandle, request.Position, client.ClientTag);
            client.SendResult(SocketCardRequestId, (ushort)ResultCode.DBError, cardValue);
            return;
        }

        if (result.Code != ResultCode.Success)
        {
            // The value mirrors the reference, which answers a refused socketing with the stone handle.
            client.SendResult(SocketCardRequestId, (ushort)result.Code, cardValue);
            return;
        }

        // The socketed stone leaves the inventory the way a consumed item does: a stack update, or the
        // destroy packet once the last unit is gone.
        client.Connection.Send(result.Remaining == 0
            ? GameCharacterPackets.BuildDestroyItem(target.CardHandle)
            : GameCharacterPackets.BuildUpdateItemCount(target.CardHandle, result.Remaining));

        // The sockets reach the client in the item sheet only: no wear-info packet carries them
        // (GameCharacterPackets.WriteInventoryItem), so the socketed item is sent as a one-item
        // inventory sheet, which is what the reference answers with too.
        foreach (var packet in GameCharacterPackets.BuildInventory(new[] { result.Target }))
        {
            client.Connection.Send(packet);
        }

        client.SendResult(SocketCardRequestId, (ushort)ResultCode.Success,
            unchecked((int)info.CharacterHandle));
    }
}
