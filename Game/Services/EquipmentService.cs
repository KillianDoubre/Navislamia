using System;
using System.Collections.Frozen;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Navislamia.Game.DataAccess.Entities.Enums;
using Navislamia.Game.DataAccess.Entities.Telecaster;
using Navislamia.Game.DataAccess.Repositories.Interfaces;
using Navislamia.Game.Network.Clients;
using Navislamia.Game.Network.Packets;
using Navislamia.Game.Network.Packets.Enums;
using Navislamia.Game.Network.Packets.Game;
using Navislamia.Game.Services.Interfaces;
using Serilog;

namespace Navislamia.Game.Services;

public class EquipmentService : IEquipmentService
{
    private const ushort EquipRequestId = (ushort)GamePackets.TM_CS_PUTON_ITEM;
    private const ushort UnequipRequestId = (ushort)GamePackets.TM_CS_PUTOFF_ITEM;
    private const ushort EquipSetRequestId = (ushort)GamePackets.TM_CS_PUTON_ITEM_SET;

    private readonly ILogger _logger = Log.ForContext<EquipmentService>();
    private readonly ICharacterService _characterService;
    private readonly IStatService _statService;
    private readonly IItemWearCatalog _wearCatalog;
    private readonly IPlayerVisibilityService _visibility;
    private readonly Weight.ICarriedWeightService _weights;
    private readonly FrozenDictionary<int, JobWearFields> _jobs;
    private readonly bool _depthFlags;

    public EquipmentService(ICharacterService characterService, IStatService statService,
        IItemWearCatalog wearCatalog, IPlayerVisibilityService visibility,
        IJobResourceRepository jobs, Weight.ICarriedWeightService weights = null)
    {
        _weights = weights;
        _characterService = characterService;
        _statService = statService;
        _wearCatalog = wearCatalog;
        _visibility = visibility;
        _jobs = jobs.GetWearFields().ToFrozenDictionary(j => j.Job);
        _depthFlags = JobDepths.AreFlags(_jobs.Values.Select(j => j.JobDepth));
    }

    public async Task EquipAsync(GameClient client, GameActionPackets.PutonItemRequest request)
    {
        var info = client.ConnectionInfo;
        if (request.TargetHandle != 0 && request.TargetHandle != info.CharacterHandle)
        {
            client.SendResult(EquipRequestId, (ushort)ResultCode.NotExist, 0);
            return;
        }

        // A spare slot (24..27) takes the item of its main twin unjudged: StructPlayer::TranslateWearPosition judges
        // a spare item when the swap (223) brings it to the main slot, not before.
        var spare = request.Position is >= (sbyte)ItemWearType.SpareWeapon and <= (sbyte)ItemWearType.SpareDecoShield;
        if (!spare && !ItemWearRules.IsWearableSlot(request.Position))
        {
            client.SendResult(EquipRequestId, (ushort)ResultCode.InvalidArgument, 0);
            return;
        }

        var refusal = spare
            ? await JudgeSpareAsync(info, request.ItemHandle, (ItemWearType)request.Position)
            : await JudgeWearAsync(info, request.ItemHandle);
        if (refusal != (ushort)ResultCode.Success)
        {
            client.SendResult(EquipRequestId, refusal, 0);
            return;
        }

        var step = await EquipAtSlotAsync(client, info, request.ItemHandle, (ItemWearType)request.Position);
        if (!step.Succeeded)
        {
            client.SendResult(EquipRequestId, step.Code, 0);
            return;
        }

        var handle = info.CharacterHandle;
        SendStatInfo(client, info, handle, step.Character);
        client.SendResult(EquipRequestId, (ushort)ResultCode.Success, 0);
        PublishWear(client, step.Character);
    }

    /// <summary>
    /// <c>TM_CS_PUTON_ITEM_SET</c> (281): a whole set of equipment in one request. The frame carries no
    /// position, so each handle is placed in the slot its own item resource declares (sheet §5.3-4).
    /// A zero handle means "nothing in this slot" and is left untouched — this request never unequips
    /// (sheet §7.4) — and a handle that fails does not stop the following ones (sheet §5.3-4).
    /// The answer is a single result, because the client has one handler for id 281 (sheet §5.2-1).
    /// </summary>
    public async Task EquipSetAsync(GameClient client, uint[] handles)
    {
        var info = client.ConnectionInfo;
        var equipped = 0;
        var firstError = (ushort)ResultCode.Success;
        CharacterEntity character = null;

        foreach (var itemHandle in handles)
        {
            if (itemHandle == 0)
            {
                continue;
            }

            EquipStep step;
            try
            {
                var placement = await ResolveSlotAsync(info.CharacterName, itemHandle);
                step = placement.Code != (ushort)ResultCode.Success
                    ? new EquipStep(placement.Code, null)
                    : !IsWearAllowed(placement.Fields.Value, info)
                        ? new EquipStep((ushort)ResultCode.NotActable, null)
                        : await EquipAtSlotAsync(client, info, itemHandle, placement.Slot);
            }
            catch (Exception exception)
            {
                _logger.Error(exception, "Could not equip item {itemHandle} of an equipment set for {clientTag}",
                    itemHandle, client.ClientTag);
                step = new EquipStep((ushort)ResultCode.DBError, null);
            }

            if (step.Succeeded)
            {
                equipped++;
                character = step.Character;
                continue;
            }

            if (firstError == (ushort)ResultCode.Success)
            {
                firstError = step.Code;
            }
        }

        if (equipped == 0)
        {
            // Nothing was equipped: report the first refusal. A frame that carried no handle at all has
            // no refusal to report and is answered like a frame that cannot be acted on, the same
            // InvalidArgument the truncated frame receives (sheet §5.3-3 and §7.5).
            client.SendResult(EquipSetRequestId,
                firstError == (ushort)ResultCode.Success ? (ushort)ResultCode.InvalidArgument : firstError, 0);
            return;
        }

        var handle = info.CharacterHandle;
        SendStatInfo(client, info, handle, character);
        client.SendResult(EquipSetRequestId, (ushort)ResultCode.Success, 0);
        PublishWear(client, character);
    }

    public async Task UnequipAsync(GameClient client, GameActionPackets.PutoffItemRequest request)
    {
        var info = client.ConnectionInfo;
        if (request.TargetHandle != 0 && request.TargetHandle != info.CharacterHandle)
        {
            client.SendResult(UnequipRequestId, (ushort)ResultCode.NotExist, 0);
            return;
        }

        if (!ItemWearRules.IsWearableSlot(request.Position))
        {
            client.SendResult(UnequipRequestId, (ushort)ResultCode.InvalidArgument, 0);
            return;
        }

        try
        {
            // Unit::putoffItem on WEAR_BAG_SLOT: TooHeavy while overloaded, or if the load would pass the
            // maximum without the bag's own capacity.
            if ((ItemWearType)request.Position == ItemWearType.BagSlot && _weights is not null)
            {
                var bag = (await _characterService.GetCarriedItemsAsync(info.CharacterName))
                    .FirstOrDefault(carried => ItemWearRules.IsWornByPlayerAt(carried, ItemWearType.BagSlot));
                if (bag is not null && !_weights.CanTakeOffBag(info, (int)bag.ItemResourceId))
                {
                    client.SendResult(UnequipRequestId, (ushort)ResultCode.TooHeavy, 0);
                    return;
                }
            }

            var item = await _characterService.UnequipItemAsync(info.CharacterName, (ItemWearType)request.Position);
            if (item is null)
            {
                client.SendResult(UnequipRequestId, (ushort)ResultCode.NotExist, 0);
                return;
            }

            var handle = info.CharacterHandle;
            SendItemWear(client, handle, item);
            SendStatInfo(client, info, handle, item.Character);
            client.SendResult(UnequipRequestId, (ushort)ResultCode.Success, 0);
            PublishWear(client, item.Character);
        }
        catch (Exception exception)
        {
            _logger.Error(exception, "Could not unequip slot {position} for {clientTag}", request.Position,
                client.ClientTag);
            client.SendResult(UnequipRequestId, (ushort)ResultCode.DBError, 0);
        }
    }

    /// <summary>
    /// The slot a handle of 281 must be placed in: the <c>wear_type</c> of its item resource. An item
    /// the character does not own is <c>AccessDenied</c>, the code 200 answers for an unknown handle. A
    /// resource the catalog does not know, or one whose wear type is not a slot <c>TM_SC_WEAR_INFO</c>
    /// can report, is <c>InvalidArgument</c>: the index of the handle in the request is never used as a
    /// fallback, since the sheet leaves that reading open (§7.3). The port requirements come back with
    /// the slot, so the caller judges them without a second read of the item.
    /// </summary>
    private async Task<(ushort Code, ItemWearType Slot, ItemWearFields? Fields)> ResolveSlotAsync(
        string characterName, uint itemHandle)
    {
        var item = await _characterService.GetItemByHandleAsync(characterName, itemHandle);
        if (item is null)
        {
            return ((ushort)ResultCode.AccessDenied, ItemWearType.None, null);
        }

        if (!_wearCatalog.TryGetWearFields(item.ItemResourceId, out var fields) ||
            !ItemWearRules.TryResolveSlot(fields.WearType, out var slot))
        {
            return ((ushort)ResultCode.InvalidArgument, ItemWearType.None, null);
        }

        return ((ushort)ResultCode.Success, slot, fields);
    }

    /// <summary>
    /// The refusal the port requirements of an item handle give (<c>NotActable</c>, the code the
    /// official server answers for an unmet requirement), or <c>Success</c> when the item may be judged
    /// and passes. A handle the character does not own, and a resource no catalog knows, are left to
    /// the equip path itself: that is where the code for an unknown handle lives, and an unreadable
    /// resource must not be refused by a rule that could not be read (the same choice as
    /// <c>ICharacterService.UnbindSkillCardAsync</c>). A read that throws is answered <c>DBError</c>,
    /// the code the equip path already sends for a database failure, so the request is never left
    /// unanswered.
    /// </summary>
    private async Task<ushort> JudgeWearAsync(ConnectionInfo info, uint itemHandle)
    {
        try
        {
            var item = await _characterService.GetItemByHandleAsync(info.CharacterName, itemHandle);
            if (item is null || !_wearCatalog.TryGetWearFields(item.ItemResourceId, out var fields))
            {
                return (ushort)ResultCode.Success;
            }

            return IsWearAllowed(fields, info)
                ? (ushort)ResultCode.Success
                : (ushort)ResultCode.NotActable;
        }
        catch (Exception exception)
        {
            _logger.Error(exception, "Could not judge the wear requirements of item {itemHandle} of {characterName}",
                itemHandle, info.CharacterName);
            return (ushort)ResultCode.DBError;
        }
    }

    private bool IsWearAllowed(ItemWearFields fields, ConnectionInfo info) =>
        IsWearAllowed(fields, info.CharacterLevel, info.CharacterRace, info.CharacterJob);

    private bool IsWearAllowed(ItemWearFields fields, int level, int race, int currentJob)
    {
        var jobId = currentJob == 0 ? race switch { 3 => 100, 4 => 200, 5 => 300, _ => 0 } : currentJob;
        return _jobs.TryGetValue(jobId, out var job)
            && ItemWearRules.IsWearAllowed(fields, level, race, job.JobClass,
                JobDepths.ToIndex(job.JobDepth, _depthFlags));
    }

    /// <summary>
    /// The items the character wears that <c>DB_Login::readEquipItemList</c> would refuse to wear again: a main slot
    /// (0..23) already taken by an earlier item, or an item that no longer passes its requirements (level, race,
    /// class, job depth). The spare set (24..27) is not judged — <c>StructPlayer::TranslateWearPosition</c> leaves it
    /// to the swap — and an item a summon wears is the summon's.
    /// </summary>
    public IReadOnlyList<ItemEntity> FindUnwearableItems(CharacterEntity character)
    {
        if (character.Items is null)
        {
            return Array.Empty<ItemEntity>();
        }

        var level = character.Lv > 0 ? character.Lv : 1;
        var taken = new HashSet<ItemWearType>();
        List<ItemEntity> refused = null;
        foreach (var item in character.Items.Where(item => item.EquippedBySummonId is null).OrderBy(item => item.Id))
        {
            if (!ItemWearRules.IsWearableSlot(item.WearInfo))
            {
                continue;
            }

            var wearable = taken.Add(item.WearInfo)
                           && _wearCatalog.TryGetWearFields(item.ItemResourceId, out var fields)
                           && IsWearAllowed(fields, level, character.Race, (int)character.CurrentJob);
            if (!wearable)
            {
                (refused ??= new List<ItemEntity>()).Add(item);
            }
        }

        return (IReadOnlyList<ItemEntity>)refused ?? Array.Empty<ItemEntity>();
    }

    /// <summary>
    /// World entry: the refused items of <see cref="FindUnwearableItems"/> go back to the bag, in the loaded character
    /// (so the stats, the wear frame and the inventory sent next see them unworn) and in the database.
    /// </summary>
    public async Task RevalidateWornItemsAsync(CharacterEntity character)
    {
        var refused = FindUnwearableItems(character);
        if (refused.Count == 0)
        {
            return;
        }

        foreach (var item in refused)
        {
            _logger.Information("{name} no longer meets the requirements of item {item} ({resource}) at {slot}: unworn",
                character.CharacterName, item.Id, item.ItemResourceId, item.WearInfo);
            item.WearInfo = ItemWearType.None;
        }

        await _characterService.UnwearItemsAsync(character.CharacterName, refused.Select(item => item.Id).ToList());
    }

    /// <summary>
    /// Equips one item at a slot the server already judged, sending the wear information of the item
    /// that moved and of the one it displaced. Nothing is sent when the equip fails: the caller owns
    /// the answer to its own request id.
    /// </summary>
    private async Task<EquipStep> EquipAtSlotAsync(GameClient client, ConnectionInfo info, uint itemHandle,
        ItemWearType slot)
    {
        try
        {
            var result = await _characterService.EquipItemAsync(info.CharacterName, itemHandle, slot);

            switch (result.Outcome)
            {
                case EquipItemOutcome.NotFound:
                    return new EquipStep((ushort)ResultCode.AccessDenied, null);
                case EquipItemOutcome.AlreadyWorn:
                    return new EquipStep((ushort)ResultCode.NotActable, null);
            }

            var handle = info.CharacterHandle;
            if (result.Displaced is not null)
            {
                SendItemWear(client, handle, result.Displaced);
            }

            SendItemWear(client, handle, result.Equipped);
            return new EquipStep((ushort)ResultCode.Success, result.Character);
        }
        catch (Exception exception)
        {
            _logger.Error(exception, "Could not equip item {itemHandle} at slot {position} for {clientTag}",
                itemHandle, slot, client.ClientTag);
            return new EquipStep((ushort)ResultCode.DBError, null);
        }
    }

    /// <summary>The main slot a spare slot doubles (<c>WEAR_SPARE_*</c> minus 24).</summary>
    public static ItemWearType MainOf(ItemWearType spare) => spare switch
    {
        ItemWearType.SpareWeapon => ItemWearType.Weapon,
        ItemWearType.SpareShield => ItemWearType.Shield,
        ItemWearType.SpareDecoWeapon => ItemWearType.DecoWeapon,
        ItemWearType.SpareDecoShield => ItemWearType.DecoShield,
        _ => ItemWearType.None
    };

    /// <summary>A spare slot takes an item whose own slot is the spare's main twin.</summary>
    private async Task<ushort> JudgeSpareAsync(ConnectionInfo info, uint itemHandle, ItemWearType spare)
    {
        var placement = await ResolveSlotAsync(info.CharacterName, itemHandle);
        if (placement.Code != (ushort)ResultCode.Success)
        {
            return placement.Code;
        }

        return placement.Slot == MainOf(spare) ? (ushort)ResultCode.Success : (ushort)ResultCode.NotActable;
    }

    /// <summary>
    /// <c>TM_CS_SWAP_EQUIP</c> (223), <c>onSwapEquip</c>: the main weapon set and the spare one change places. A spare
    /// item that comes to a main slot meets the wear requirements, and one refusal refuses the swap as a whole
    /// (the official puts everything off first and can leave a refused item unworn). No result on success: the
    /// 287 of every moved item, the stats and the 202 to whoever sees the character.
    /// </summary>
    public async Task SwapAsync(GameClient client)
    {
        const ushort swapRequestId = (ushort)GamePackets.TM_CS_SWAP_EQUIP;
        var info = client.ConnectionInfo;
        if (info.CharacterHandle == 0)
        {
            return;
        }

        if (info.CharacterHp <= 0)
        {
            client.SendResult(swapRequestId, (ushort)ResultCode.NotActable, 0);
            return;
        }

        try
        {
            var swap = await _characterService.SwapEquipAsync(info.CharacterName, item =>
                _wearCatalog.TryGetWearFields(item.ItemResourceId, out var fields) && IsWearAllowed(fields, info));
            if (swap is not { } result)
            {
                client.SendResult(swapRequestId, (ushort)ResultCode.NotActable, 0);
                return;
            }

            if (result.Moved.Count == 0)
            {
                return;
            }

            foreach (var item in result.Moved)
            {
                SendItemWear(client, info.CharacterHandle, item);
            }

            SendStatInfo(client, info, info.CharacterHandle, result.Character);
            PublishWear(client, result.Character);
        }
        catch (Exception exception)
        {
            _logger.Error(exception, "Could not swap the equipment of {clientTag}", client.ClientTag);
            client.SendResult(swapRequestId, (ushort)ResultCode.DBError, 0);
        }
    }

    private readonly record struct EquipStep(ushort Code, CharacterEntity Character)
    {
        public bool Succeeded => Code == (ushort)ResultCode.Success;
    }

    private void SendStatInfo(GameClient client, ConnectionInfo info, uint handle, CharacterEntity character)
    {
        _statService.Seed(info, character);
        // The session's view once seeded: buffs, title, weapon range and a GM /speed included.
        var result = _statService.Compute(info);
        client.Connection.Send(GameStatPackets.BuildStatInfo(handle, result.Total, StatInfoType.Total));
        client.Connection.Send(GameStatPackets.BuildStatInfo(handle, result.ByItem, StatInfoType.ByItem));
        client.SendVitalProperty(GameStatPackets.BuildProperty(handle, "max_hp", (int)result.Total.MaxHp));
        client.SendVitalProperty(GameStatPackets.BuildProperty(handle, "max_mp", (int)result.Total.MaxMp));
    }

    private void PublishWear(GameClient client, CharacterEntity character)
    {
        var info = client.ConnectionInfo;
        var frame = GameCharacterPackets.BuildWearInfo(info.CharacterHandle, character);
        info.WearFrame = frame;
        _visibility.SendToObservers(client, frame, includeSelf: true);
    }

    private static void SendItemWear(GameClient client, uint targetHandle, ItemEntity item)
    {
        client.Connection.Send(GameCharacterPackets.BuildItemWearInfo((uint)item.Id, (short)item.WearInfo, targetHandle,
            (int)item.Enhance, (byte)item.ElementalEffectType));
    }
}
