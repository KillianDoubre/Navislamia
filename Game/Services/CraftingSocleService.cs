using System;
using System.Threading.Tasks;
using Navislamia.Game.DataAccess.Entities.Telecaster;
using Navislamia.Game.Network.Clients;
using Navislamia.Game.Network.Packets;
using Navislamia.Game.Network.Packets.Enums;
using Navislamia.Game.Network.Packets.Game;
using Serilog;

namespace Navislamia.Game.Services;

/// <summary>
/// The structural socle of the crafting and item-enchantment family: <c>TM_CS_MIX</c> (256),
/// <c>TM_CS_SOULSTONE_CRAFT</c> (260), <c>TM_CS_REPAIR_SOULSTONE</c> (262),
/// <c>TM_CS_TRANSMIT_ETHEREAL_DURABILITY</c> (263) and
/// <c>TM_CS_TRANSMIT_ETHEREAL_DURABILITY_TO_EQUIPMENT</c> (264).
///
/// It does four things, in this order, and writes no crafting of any kind: read the frame at its
/// established Epic 7.3 size, bound it, resolve every handle it names against the character's own items,
/// then refuse. No <c>MixResource</c>/<c>EnhanceResource</c> table is loaded, no rate is rolled, no
/// failure policy is applied and no socket is touched: those are game decisions the specification
/// deliberately leaves to Killian (docs/packet-specs/socle-artisanat-objets.md §9.2, §9.3, §9.5).
///
/// The refusals answer <c>TM_SC_RESULT</c> (0) with the received id as <c>request_msg_id</c>: an unknown
/// or non-owned handle reports <c>NotExist</c> (1) with the handle as value, which is exactly what NGemity
/// answers on <c>WorldSession.cpp:1503-1526</c>; an accepted frame is refused with <c>InvalidArgument</c>
/// (28), the code NGemity sends when no mix rule resolves (<c>WorldSession.cpp:1463-1466</c>).
/// </summary>
public class CraftingSocleService : ICraftingSocleService
{
    private readonly ILogger _logger = Log.ForContext<CraftingSocleService>();
    private readonly ICharacterService _characterService;

    public CraftingSocleService(ICharacterService characterService)
    {
        _characterService = characterService;
    }

    public async Task HandleAsync(GameClient client, ushort packetId, byte[] packet)
    {
        // Nothing to answer before the character is in the world: no inventory to resolve against and no
        // handle to name. Same precedent as TM_CS_GET_REGION_INFO's handler (GameClient.HandleGetRegionInfo).
        if (client.ConnectionInfo.CharacterHandle == 0)
        {
            _logger.Debug("Crafting packet {id} received from {clientTag} outside the world, dropped", packetId,
                client.ClientTag);
            return;
        }

        uint[] handles;
        switch (packetId)
        {
            case (ushort)GamePackets.TM_CS_MIX:
                if (!GameActionPackets.TryReadMix(packet, out var mix))
                {
                    RefuseMalformed(client, packetId, packet.Length);
                    return;
                }

                handles = CraftingSocleRules.ReferencedHandles(mix);
                break;

            case (ushort)GamePackets.TM_CS_SOULSTONE_CRAFT:
                if (!GameActionPackets.TryReadSoulstoneCraft(packet, out var soulstoneCraft))
                {
                    RefuseMalformed(client, packetId, packet.Length);
                    return;
                }

                handles = CraftingSocleRules.ReferencedHandles(soulstoneCraft);
                break;

            case (ushort)GamePackets.TM_CS_REPAIR_SOULSTONE:
                if (!GameActionPackets.TryReadRepairSoulstone(packet, out var repair))
                {
                    RefuseMalformed(client, packetId, packet.Length);
                    return;
                }

                handles = CraftingSocleRules.ReferencedHandles(repair);
                break;

            case (ushort)GamePackets.TM_CS_TRANSMIT_ETHEREAL_DURABILITY:
                if (!GameActionPackets.TryReadTransmitEtherealDurability(packet, out var transmit))
                {
                    RefuseMalformed(client, packetId, packet.Length);
                    return;
                }

                handles = CraftingSocleRules.ReferencedHandles(transmit);
                break;

            case (ushort)GamePackets.TM_CS_TRANSMIT_ETHEREAL_DURABILITY_TO_EQUIPMENT:
                // The 7.3 frame carries a float rate and no handle at all: nothing can be resolved, and the
                // unit of the rate is not established, so the frame is only bounded.
                if (!GameActionPackets.TryReadTransmitEtherealDurabilityToEquipment(packet, out _))
                {
                    RefuseMalformed(client, packetId, packet.Length);
                    return;
                }

                handles = Array.Empty<uint>();
                break;

            default:
                _logger.Warning("Crafting packet {id} from {clientTag} is outside the socle family, dropped",
                    packetId, client.ClientTag);
                return;
        }

        if (await HasUnknownHandleAsync(client, packetId, handles))
        {
            return;
        }

        // The frame is well formed and every item it names exists. What the craft would do is not written:
        // answering anything but a refusal here would invent a rate, a failure policy or a socket rule.
        _logger.Warning(
            "Crafting packet {id} from {clientTag} is well formed and resolvable but the crafting engine is not implemented: refused with InvalidArgument",
            packetId, client.ClientTag);
        client.SendResult(packetId, (ushort)ResultCode.InvalidArgument);
    }

    /// <summary>
    /// Resolves each named handle against the character's own inventory; an item that does not resolve is
    /// not one of this character's items, which is the <c>NotExist</c> NGemity answers with the handle as
    /// value. Returns true when a refusal has already been sent.
    /// </summary>
    private async Task<bool> HasUnknownHandleAsync(GameClient client, ushort packetId, uint[] handles)
    {
        foreach (var handle in handles)
        {
            ItemEntity item;
            try
            {
                item = await _characterService.GetItemByHandleAsync(client.ConnectionInfo.CharacterName, handle);
            }
            catch (Exception exception)
            {
                _logger.Error(exception, "Could not read item {itemHandle} for {clientTag}", handle, client.ClientTag);
                client.SendResult(packetId, (ushort)ResultCode.DBError, unchecked((int)handle));
                return true;
            }

            if (item is null)
            {
                _logger.Debug("Crafting packet {id} from {clientTag} names unknown item {itemHandle}, refused",
                    packetId, client.ClientTag, handle);
                client.SendResult(packetId, (ushort)ResultCode.NotExist, unchecked((int)handle));
                return true;
            }
        }

        return false;
    }

    private void RefuseMalformed(GameClient client, ushort packetId, int length)
    {
        _logger.Warning("Malformed crafting frame {id} from {clientTag} (Length: {length}), refused with InvalidArgument",
            packetId, client.ClientTag, length);
        client.SendResult(packetId, (ushort)ResultCode.InvalidArgument);
    }
}
