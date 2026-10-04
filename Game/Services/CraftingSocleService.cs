using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;
using Navislamia.Configuration.Options;
using Navislamia.Game.DataAccess.Entities.Arcadia;
using Navislamia.Game.DataAccess.Entities.Telecaster;
using Navislamia.Game.DataAccess.Repositories.Interfaces;
using Navislamia.Game.Network.Clients;
using Navislamia.Game.Network.Packets;
using Navislamia.Game.Network.Packets.Enums;
using Navislamia.Game.Network.Packets.Game;
using Serilog;

namespace Navislamia.Game.Services;

/// <summary>
/// Reads crafting frames and resolves their owned inventory handles. Mix recipes 101, 102, 103, 311
/// and 501 execute through CraftingEngine and the atomic inventory commit; unsupported recipes and
/// the remaining repair/ethereal frames retain the structural socle's refusal.
/// See docs/packet-specs/socle-artisanat-ressources.md and socle-artisanat-cartes-competences.md.
/// </summary>
public class CraftingSocleService : ICraftingSocleService
{
    private readonly ILogger _logger = Log.ForContext<CraftingSocleService>();
    private readonly ICharacterService _characterService;
    private readonly IMixResourceCatalog _mixCatalog;
    private readonly IItemMatchCatalog _itemCatalog;
    private readonly IEnhanceResourceCatalog _enhanceCatalog;
    private readonly IEtherealSacrificeCatalog _etherealSacrifices;
    private readonly int _localFlag;
    private readonly IMonsterDropCatalog _drops;
    private readonly Progression.ITitleService _titles;
    private readonly Creatures.ICreatureCatalog _creatures;
    private readonly Buffs.IBuffCatalog _skills;
    private readonly Auction.IAuctionCatalog _names;
    private readonly IItemUseCatalog _useFlags;
    private readonly IStatService _stats;
    private readonly Creatures.ICreatureEvents _creatureEvents;

    public CraftingSocleService(ICharacterService characterService, IMixResourceCatalog mixCatalog,
        IItemMatchCatalog itemCatalog, IEnhanceResourceCatalog enhanceCatalog = null,
        IOptions<CraftingOptions> craftingOptions = null, IEtherealSacrificeCatalog etherealSacrifices = null,
        IMonsterDropCatalog drops = null, Progression.ITitleService titles = null,
        Creatures.ICreatureCatalog creatures = null, Buffs.IBuffCatalog skills = null, Auction.IAuctionCatalog names = null,
        IItemUseCatalog useFlags = null, IStatService stats = null, Creatures.ICreatureEvents creatureEvents = null)
    {
        _names = names;
        _useFlags = useFlags;
        _stats = stats;
        _creatureEvents = creatureEvents;
        _creatures = creatures;
        _skills = skills;
        _drops = drops;
        _titles = titles;
        _characterService = characterService;
        _mixCatalog = mixCatalog;
        _itemCatalog = itemCatalog;
        _enhanceCatalog = enhanceCatalog;
        _etherealSacrifices = etherealSacrifices;
        _localFlag = craftingOptions?.Value.LocalFlag ?? new CraftingOptions().LocalFlag;
    }

    /// <summary>
    /// The craft's dice: an integer in <c>[min, max]</c>, both inclusive, like NGemity's <c>irand</c> and
    /// <c>urand</c>. Replaceable so a test can decide a success or a failure.
    /// </summary>
    public Func<int, int, int> Roll { get; set; } = (min, max) => Random.Shared.Next(min, max + 1);

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
        var etherealHandle = 0u;
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

                // Kept aside as well as resolved: the value of the answer names the object the player
                // offered, and the guard below judges it. A zero handle is the family's empty-slot
                // sentinel and never reaches either (CraftingSocleRules).
                etherealHandle = transmit.Handle;
                handles = CraftingSocleRules.ReferencedHandles(transmit);
                break;

            case (ushort)GamePackets.TM_CS_TRANSMIT_ETHEREAL_DURABILITY_TO_EQUIPMENT:
                // The 7.3 frame carries a float rate and no handle at all: nothing can be resolved. The rate
                // is a share of the restitution asked for, and the client can only write (0,1]
                // (docs/packet-specs/264-transmit-ethereal-durability-to-equipment.md §2.3), so the frame is
                // bounded by that domain before the socle's usual refusal. A rate outside it is not a frame
                // Epic 7.3 can produce: it is refused, never clamped or interpreted.
                if (!GameActionPackets.TryReadTransmitEtherealDurabilityToEquipment(packet, out var restoration))
                {
                    RefuseMalformed(client, packetId, packet.Length);
                    return;
                }

                if (!CraftingSocleRules.IsRestorableRate(restoration.Rate))
                {
                    _logger.Warning(
                        "Crafting packet {id} from {clientTag} carries a rate outside the domain the Epic 7.3 client can produce (Rate: {rate}), refused with InvalidArgument",
                        packetId, client.ClientTag, restoration.Rate);
                    client.SendResult(packetId, (ushort)ResultCode.InvalidArgument);
                    return;
                }

                handles = Array.Empty<uint>();
                break;

            default:
                _logger.Warning("Crafting packet {id} from {clientTag} is outside the socle family, dropped",
                    packetId, client.ClientTag);
                return;
        }

        var resolved = await ResolveHandlesAsync(client, packetId, handles);
        if (resolved is null)
        {
            return;
        }

        // 263 is the one frame of the family whose handle is judged rather than merely resolved: it names
        // the object the player offers as the sacrifice, and the client emits the frame even for an object
        // it could not qualify ("Place any equipment in Materials Slots"; spec §2.2). A frame that names no
        // object at all (handle 0, dropped by the sentinel rule) resolves nothing and keeps the generic
        // refusal below.
        if (packetId == (ushort)GamePackets.TM_CS_TRANSMIT_ETHEREAL_DURABILITY && resolved.Length == 1)
        {
            RefuseEtherealSacrifice(client, packetId, etherealHandle, resolved[0]);
            return;
        }

        // The frame is well formed and every item it names exists. What the craft would do is not written:
        // answering anything but a refusal here would invent a rate, a failure policy or a socket rule.
        _logger.Warning(
            "Crafting packet {id} from {clientTag} is well formed and resolvable but the crafting engine is not implemented: refused with InvalidArgument",
            packetId, client.ClientTag);
        client.SendResult(packetId, (ushort)ResultCode.InvalidArgument);
    }

    /// <summary>Resolve a mix recipe, decide its effects, commit them, then publish inventory and result.</summary>
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
            // Debug, not Warning: a frame no rule accepts is a routine event of a player experimenting in
            // the combination window, and the answer is the refusal the socle already sends. Only the rule
            // that was resolved — the one fact the next lobe will need — is worth a Warning.
            // See docs/packet-specs/socle-artisanat-ressources.md §8 (L1b).
            _logger.Debug(
                "Crafting packet {id} from {clientTag} is not accepted by any of the {rules} mix rules ({target} target, {materials} materials): refused with InvalidArgument",
                packetId, client.ClientTag, rules.Count, target is null ? "no" : "a named", materials.Count);
            client.SendResult(packetId, (ushort)ResultCode.InvalidArgument);
            return;
        }

        EnhanceResourceEntity enhance = null;
        if (resolution.Rule.MixType is CraftingEngine.MixEnhance or CraftingEngine.MixEnhanceWithoutFail
            or CraftingEngine.MixEnhanceSkillCard or CraftingEngine.MixEnhanceCreatureCard
            or CraftingEngine.MixEnhanceCreatureCardWithJoker)
        {
            _enhanceCatalog?.TryGetForServer(resolution.Rule.MixValue01, _localFlag, out enhance);
        }

        var info = client.ConnectionInfo;
        var context = new MixContext(info.EtherealStone,
            code => _itemCatalog.TryGetFields(code, out var fields) ? fields.Mix ?? ItemMixFields.Empty : ItemMixFields.Empty);
        var plan = CraftingEngine.Plan(resolution, target, enhance, Roll, PickFromGroup, context);
        if (plan.Refusal != ResultCode.Success)
        {
            _logger.Warning(
                "Crafting packet {id} from {clientTag} resolves to mix rule {ruleId} of type {mixType}, which cannot be carried out ({refusal}): refused",
                packetId, client.ClientTag, resolution.Rule.Id, resolution.Rule.MixType, plan.Refusal);
            client.SendResult(packetId, (ushort)plan.Refusal);
            return;
        }

        CraftCommitResult commit;
        try
        {
            commit = await _characterService.ApplyMixAsync(client.ConnectionInfo.CharacterName, plan);
        }
        catch (Exception exception)
        {
            _logger.Error(exception, "Could not apply mix rule {ruleId} for {clientTag}", resolution.Rule.Id,
                client.ClientTag);
            client.SendResult(packetId, (ushort)ResultCode.DBError);
            return;
        }

        if (commit.Outcome != CraftCommitOutcome.Success)
        {
            // A stack went away or the target changed between the frame and the save (the client resends,
            // another craft ran): nothing was applied.
            client.SendResult(packetId, (ushort)ResultCode.NotExist);
            return;
        }

        foreach (var (handle, remaining) in commit.Consumed)
        {
            client.Connection.Send(remaining == 0
                ? GameCharacterPackets.BuildDestroyItem(handle)
                : GameCharacterPackets.BuildUpdateItemCount(handle, remaining));
        }

        if (plan.Change is { } change)
        {
            if (commit.Target is null && !change.SplitOne)
            {
                client.Connection.Send(GameCharacterPackets.BuildDestroyItem(change.Handle));
            }
            else if (commit.Target is not null)
            {
                foreach (var frame in GameCharacterPackets.BuildInventory(new[] { commit.Target }))
                {
                    client.Connection.Send(frame);
                }
            }
        }

        // The items that changed and the items made: their records again (SendItemMessage).
        var changed = commit.Mutated.Concat(commit.Created).ToArray();
        if (changed.Length > 0)
        {
            foreach (var frame in GameCharacterPackets.BuildInventory(changed))
            {
                client.Connection.Send(frame);
            }
        }

        // A worn item repaired, worn down or changed by the mix changes what the character or its summon wears
        // (MixManager ends with CalculateStat): an exhausted item gets its stats back, the wear its target.
        var worn = commit.Mutated.Where(item => item.WearInfo != DataAccess.Entities.Enums.ItemWearType.None).ToArray();
        if (commit.Target is { WearInfo: not DataAccess.Entities.Enums.ItemWearType.None } wornTarget) worn = worn.Append(wornTarget).ToArray();
        if (worn.Length > 0)
        {
            _creatureEvents?.EquipmentDurabilityChanged(client, worn);
            try
            {
                EquipmentStatRefresh.Send(client, _stats, await _characterService.GetCarriedItemsAsync(info.CharacterName));
            }
            catch (Exception exception)
            {
                _logger.Error(exception, "Could not refresh the stats of {clientTag} after a mix", client.ClientTag);
            }
        }

        if (commit.EtherealStone is { } stone)
        {
            info.EtherealStone = stone;
            client.Connection.Send(GameStatPackets.BuildProperty(info.CharacterHandle, "ethereal_stone", (int)stone));
        }

        // The items a mix allocates are announced (MixManager); the copies a failure or a split leaves are not
        // (procEnhanceFail, EnhanceSkillCard). They come first in Created, in the plan's order.
        var copies = plan.Created.Count(creation => creation.CopyOf != 0);
        foreach (var item in commit.Created.Skip(copies))
        {
            if (resolution.Rule.MixType == CraftingEngine.MixCreateItem)
            {
                // CreateItem always names the count, joinable or not.
                if (_names?.TryGetItem((int)item.ItemResourceId, out var row) == true && row.NameId > 0)
                    client.Connection.Send(GameChatPackets.BuildChat("@SYSTEM", (byte)ChatType.Item,
                        ItemObtainedNotice.Message(row.NameId, item.Amount, joinable: true)));
            }
            else
            {
                ItemObtainedNotice.Send(client, _names, _useFlags, item, item.Amount);
            }
        }

        foreach (var line in plan.ChatLines)
        {
            client.Connection.Send(GameChatPackets.BuildChat("@SYSTEM", (byte)ChatType.Item, line));
        }

        if (plan.CardEnhance is { } card)
        {
            OnCreatureCardEnhanced(client, card);
        }

        // CreateItem and the recycling report the last item made; the skill card its new unit; the others their plan.
        uint[] resultHandles = plan.ReportCreated || resolution.Rule.MixType == CraftingEngine.MixCreateItem
            ? commit.Created.Count > 0 ? new[] { (uint)commit.Created[^1].Id } : Array.Empty<uint>()
            : plan.Change is { SplitOne: true } && plan.ResultHandles.Count > 0
                ? new[] { (uint)commit.Target.Id } : plan.ResultHandles.ToArray();
        if (!plan.NoResult)
        {
            client.Connection.Send(GameCraftingPackets.BuildMixResult(resultHandles));
        }

        _logger.Information("Mix rule {ruleId} (type {mixType}) for {clientTag}: {outcome}", resolution.Rule.Id,
            resolution.Rule.MixType, client.ClientTag, resultHandles.Length > 0 || plan.NoResult ? "success" : "failure");

        // UpdateTitleConditionByItemCreateByMixing: only MIX_CREATE_ITEM fills vCreatedItem (onMix).
        if (resolution.Rule.MixType == CraftingEngine.MixCreateItem && commit.Created.Count > 0 && _titles is not null)
        {
            _ = _titles.RecordAsync(client, Progression.TitleEvents.ItemsMixed(
                commit.Created.Select(item => ((int)item.ItemResourceId, item.Amount)).ToArray()));
        }
    }

    /// <summary>
    /// <c>EnhanceCreatureCard</c> after the save: the session's card takes its new enhancement (a success reborns the
    /// summon full), and a success counts for the titles (<c>UpdateTitleConditionBySummonEnhance</c>).
    /// </summary>
    private void OnCreatureCardEnhanced(GameClient client, CraftCardEnhance change)
    {
        var info = client.ConnectionInfo;
        Creatures.CreatureCard card;
        lock (info.SummonLock)
        {
            card = info.CreatureCards.GetValueOrDefault(change.CardHandle);
            if (card is null)
            {
                return;
            }

            if (change.Enhance < 0)
            {
                info.CreatureCards.Remove(change.CardHandle);
                return;
            }

            card.Enhance = change.Enhance;
            if (change.Succeeded)
            {
                card.HpKnown = false;
            }
        }

        if (change.Succeeded && _titles is not null && card.SummonCode != 0)
        {
            var rate = _creatures is not null && _creatures.TryGetSummon(card.SummonCode, out var summon) ? summon.Rate : 0;
            _ = _titles.RecordAsync(client, Progression.TitleEvents.SummonEnhance(card.SummonCode, rate, change.Enhance));
        }
    }

    /// <summary><c>GameContent::SelectItemIDFromDropGroup</c>: one member by weight, with its own drawn count.</summary>
    private (int ItemId, long Count)? PickFromGroup(int groupId)
    {
        if (_drops?.Groups is not { } groups || !groups.TryGetValue(groupId, out var members) || members.Length == 0)
        {
            return null;
        }

        var total = members.Sum(member => member.Weight);
        if (total <= 0)
        {
            return null;
        }

        var key = Roll(1, 100_000_000) / 100_000_000.0 * total;
        var cumulated = 0.0;
        foreach (var member in members)
        {
            cumulated += member.Weight;
            if (key <= cumulated)
            {
                return member.ItemId == 0 ? null
                    : (member.ItemId, Roll(Math.Min(member.MinCount, member.MaxCount), Math.Max(member.MinCount, member.MaxCount)));
            }
        }

        return null;
    }

    /// <summary>
    /// Reads one material of a <c>TM_CS_MIX</c> frame: the item instance from the character's own
    /// inventory, then the template columns and skill ID the conditions compare
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
            count,
            handle,
            fields.SkillId,
            item.Amount,
            fields.Mix,
            InstanceOf(client.ConnectionInfo, item));
    }

    /// <summary>The instance fields a condition or an effect reads, and the creature card's summon when it has one.</summary>
    private MixInstance InstanceOf(ConnectionInfo info, ItemEntity item)
    {
        int summonCode = 0, summonRate = 0, summonLevel = 1, joker = 0;
        var formed = false;
        lock (info.SummonLock)
        {
            if (info.CreatureCards.TryGetValue(item.Id, out var card) && card.HasSummon)
            {
                summonCode = card.SummonCode;
                summonLevel = card.Level;
                formed = Array.IndexOf(info.SummonSlots, item.Id) >= 0;
                if (_creatures is not null && _creatures.TryGetSummon(card.SummonCode, out var summon))
                {
                    summonRate = summon.Rate;
                }

                // The joker's Friendship of Crown: var0 × level × 1000 of the creature card chance.
                if (card.Skills.TryGetValue(CraftingEngine.FriendshipOfCrownSkill, out var level) && _skills is not null
                    && _skills.TryGet(CraftingEngine.FriendshipOfCrownSkill, out var skill) && skill.Vars is { Length: > 0 } vars)
                {
                    joker = (int)(vars[0] * level * 1000);
                }
            }
        }

        return new MixInstance((int)item.EtherealDurability, (int)item.ElementalEffectType, item.SocketItemIds,
            item.RemainingTime, summonCode, summonRate, summonLevel, formed, item.Endurance, joker);
    }

    /// <summary>
    /// The conduct of <c>TM_CS_TRANSMIT_ETHEREAL_DURABILITY</c> (263) once its handle resolves. An object
    /// that is not an equipment of the character, or whose ethereal durability is spent, is refused with
    /// the code the repository already answers a refused object with; a sound object is still refused with
    /// the socle's <c>InvalidArgument</c>, because the charge it asks for is not established
    /// (docs/packet-specs/263-transmit-ethereal-durability.md §5.4, §7.1, §7.6).
    /// </summary>
    private void RefuseEtherealSacrifice(GameClient client, ushort packetId, uint handle, ItemEntity item)
    {
        var resource = _etherealSacrifices is not null && _etherealSacrifices.TryGet(item.ItemResourceId, out var fields)
            ? fields
            : (ItemEtherealFields?)null;

        var gate = EtherealDurabilityRules.Judge(resource, item.EtherealDurability);
        if (gate == EtherealSacrificeGate.Accepted)
        {
            _logger.Warning(
                "TM_CS_TRANSMIT_ETHEREAL_DURABILITY from {clientTag} names sacrificeable item {itemHandle} ({resourceId}, ethereal durability {etherealDurability}), but the amount, the ceiling and the object consumed are not established: refused with InvalidArgument",
                client.ClientTag, handle, item.ItemResourceId, item.EtherealDurability);
            client.SendResult(packetId, (ushort)ResultCode.InvalidArgument);
            return;
        }

        _logger.Warning(
            "TM_CS_TRANSMIT_ETHEREAL_DURABILITY from {clientTag} names item {itemHandle} ({resourceId}, ethereal durability {etherealDurability}) which {clause}: refused with NotActable",
            client.ClientTag, handle, item.ItemResourceId, item.EtherealDurability,
            EtherealDurabilityRules.Describe(gate));
        client.SendResult(packetId, (ushort)EtherealDurabilityRules.RefusalCode(gate), unchecked((int)handle));
    }

    /// <summary>
    /// Resolves each named handle against the character's own inventory; an item that does not resolve is
    /// not one of this character's items, which is the <c>NotExist</c> NGemity answers with the handle as
    /// value. Returns the rows in the order of <paramref name="handles"/> — so a caller that judges the
    /// object does not read the item table a second time — or <c>null</c> when a refusal has already been
    /// sent.
    /// </summary>
    private async Task<ItemEntity[]> ResolveHandlesAsync(GameClient client, ushort packetId, uint[] handles)
    {
        var resolved = new ItemEntity[handles.Length];
        for (var index = 0; index < handles.Length; index++)
        {
            var handle = handles[index];

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

            resolved[index] = item;
        }

        return resolved;
    }


    private void RefuseMalformed(GameClient client, ushort packetId, int length)
    {
        _logger.Warning("Malformed crafting frame {id} from {clientTag} (Length: {length}), refused with InvalidArgument",
            packetId, client.ClientTag, length);
        client.SendResult(packetId, (ushort)ResultCode.InvalidArgument);
    }
}
