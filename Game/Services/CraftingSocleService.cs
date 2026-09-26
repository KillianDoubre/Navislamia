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
/// The structural socle of the crafting and item-enchantment family: <c>TM_CS_MIX</c> (256),
/// <c>TM_CS_SOULSTONE_CRAFT</c> (260), <c>TM_CS_REPAIR_SOULSTONE</c> (262),
/// <c>TM_CS_TRANSMIT_ETHEREAL_DURABILITY</c> (263) and
/// <c>TM_CS_TRANSMIT_ETHEREAL_DURABILITY_TO_EQUIPMENT</c> (264).
///
/// It does four things, in this order, and writes no crafting of any kind: read the frame at its
/// established Epic 7.3 size, bound it, resolve every handle it names against the character's own items,
/// then refuse. No rate is rolled, no failure policy is applied and no socket is touched: those are game
/// decisions the specification deliberately leaves to Killian (docs/packet-specs/socle-artisanat-objets.md
/// §9.2, §9.3, §9.5).
///
/// <c>TM_CS_MIX</c> (256) goes one step further since the resource lobe landed: its target and its
/// material stacks are read from the two item rows, the <c>MixResource</c> table is asked which rule
/// accepts the combination (<see cref="MixResourceMatcher"/>), and the outcome is written to the log —
/// "resolved to rule N, effects not implemented" against "no rule accepts this frame". The answer is the
/// same refusal either way: what a matched type does (a rate, a failure policy, the items it removes, the
/// <c>TM_SC_MIX_RESULT</c> 257 it sends) is the next lobe
/// (docs/packet-specs/socle-artisanat-ressources.md §6.4 and §8, L2).
///
/// The refusals answer <c>TM_SC_RESULT</c> (0) with the received id as <c>request_msg_id</c>. A handle
/// that resolves to none of the character's items reports <c>NotExist</c> (1) with the handle as value,
/// the convention the 203 drop path already uses (docs/packet-specs/203-drop-item.md §5.3); NGemity
/// splits that case in two (<c>NOT_EXIST</c> for the item being crafted, <c>ACCESS_DENIED</c> for a soul
/// stone, <c>WorldSession.cpp:1503-1507</c> and <c>:1521-1526</c>), a distinction the socle does not
/// reproduce because which handle plays which part is only established for 256 and 260. A readable
/// frame is refused with <c>InvalidArgument</c> (28), the code NGemity sends when no mix rule resolves
/// (<c>WorldSession.cpp:1463-1466</c>); the value stays 0 as in that reference answer.
/// </summary>
public class CraftingSocleService : ICraftingSocleService
{
    private readonly ILogger _logger = Log.ForContext<CraftingSocleService>();
    private readonly ICharacterService _characterService;
    private readonly IMixResourceCatalog _mixCatalog;
    private readonly IItemMatchCatalog _itemCatalog;

    public CraftingSocleService(ICharacterService characterService, IMixResourceCatalog mixCatalog,
        IItemMatchCatalog itemCatalog)
    {
        _characterService = characterService;
        _mixCatalog = mixCatalog;
        _itemCatalog = itemCatalog;
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

                // 256 is the one frame of the family whose resolution is decided (lobe L1b): it is answered
                // by the mix rule table rather than by the share handle check below.
                await ResolveMixAsync(client, packetId, mix);
                return;

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
    /// The resolution of a <c>TM_CS_MIX</c> (256) frame. The target is read only when the frame names one
    /// (handle 0 is a sentinel, not an item — <see cref="CraftingSocleRules"/>), the materials in the order
    /// of the frame, then the rule table decides. Whatever the verdict, the answer is the refusal the socle
    /// already sent (<c>InvalidArgument</c>): the log is the only thing that tells a resolved rule from an
    /// unmatched frame.
    /// </summary>
    private async Task ResolveMixAsync(GameClient client, ushort packetId, GameActionPackets.MixRequest mix)
    {
        MixMaterial? target = null;

        if (mix.MainItemHandle != 0)
        {
            target = await TryReadMaterialAsync(client, packetId, mix.MainItemHandle, mix.MainItemCount);
            if (target is null)
            {
                return;
            }
        }

        var subItems = mix.SubItems ?? Array.Empty<GameActionPackets.MixItemInfo>();
        var materials = new List<MixMaterial>(subItems.Length);

        foreach (var subItem in subItems)
        {
            var material = await TryReadMaterialAsync(client, packetId, subItem.Handle, subItem.Count);
            if (material is null)
            {
                return;
            }

            materials.Add(material.Value);
        }

        var rules = _mixCatalog.Rules;

        if (!MixResourceMatcher.TryResolve(rules, target, materials, out var resolution))
        {
            _logger.Warning(
                "Crafting packet {id} from {clientTag} is not accepted by any of the {rules} mix rules ({target} target, {materials} materials): refused with InvalidArgument",
                packetId, client.ClientTag, rules.Count, target is null ? "no" : "a named", materials.Count);
            client.SendResult(packetId, (ushort)ResultCode.InvalidArgument);
            return;
        }

        _logger.Warning(
            "Crafting packet {id} from {clientTag} resolves to mix rule {ruleId} of type {mixType} and would consume {stacks} material stacks, but the effects of that type are not implemented: refused with InvalidArgument",
            packetId, client.ClientTag, resolution.Rule.Id, resolution.Rule.MixType, resolution.ConsumedCounts.Count);
        client.SendResult(packetId, (ushort)ResultCode.InvalidArgument);
    }

    /// <summary>
    /// Reads one material of a <c>TM_CS_MIX</c> frame: the item instance from the character's own
    /// inventory, then the four template columns the conditions compare
    /// (<see cref="IItemMatchCatalog"/>). The refusals are the ones the socle already sent — <c>DBError</c>
    /// when the read fails, <c>NotExist</c> when the handle is not one of the character's items — plus
    /// <c>InvalidArgument</c> when the item exists but its resource is absent from the table: judging
    /// conditions on a zeroed row would accept a recipe the client never meant.
    /// Returns null when a response has already been sent.
    /// </summary>
    private async Task<MixMaterial?> TryReadMaterialAsync(GameClient client, ushort packetId, uint handle,
        ushort count)
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
            return null;
        }

        if (item is null)
        {
            _logger.Debug("Crafting packet {id} from {clientTag} names unknown item {itemHandle}, refused",
                packetId, client.ClientTag, handle);
            client.SendResult(packetId, (ushort)ResultCode.NotExist, unchecked((int)handle));
            return null;
        }

        if (!_itemCatalog.TryGetFields(item.ItemResourceId, out var fields))
        {
            _logger.Warning(
                "Crafting packet {id} from {clientTag} names item {itemHandle} of resource {resourceId}, which is not in ItemResource: refused with InvalidArgument",
                packetId, client.ClientTag, handle, item.ItemResourceId);
            client.SendResult(packetId, (ushort)ResultCode.InvalidArgument);
            return null;
        }

        return new MixMaterial(
            (int)item.ItemResourceId,
            (int)fields.Group,
            (int)fields.Class,
            fields.Rank,
            (int)fields.WearType,
            item.Level,
            item.Enhance,
            (int)item.Flag,
            count);
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
