using System;
using System.Threading.Tasks;
using Navislamia.Game.DataAccess.Entities.Telecaster;
using Navislamia.Game.DataAccess.Entities.Enums;
using Navislamia.Game.Network.Clients;
using Navislamia.Game.Network.Packets;
using Navislamia.Game.Network.Packets.Enums;
using Navislamia.Game.Network.Packets.Game;
using Navislamia.Game.Services.Pets;
using Serilog;

namespace Navislamia.Game.Services;

/// <summary>
/// Handles <c>TM_CS_USE_ITEM</c> (253): checks the resource and cooldown, consumes one unit,
/// answers the request, then applies supported instant effects. Pet cages use the pet service.
/// </summary>
public class ItemUseService : IItemUseService
{
    private const ushort UseItemRequestId = (ushort)GamePackets.TM_CS_USE_ITEM;

    private readonly ILogger _logger = Log.ForContext<ItemUseService>();
    private readonly ICharacterService _characterService;
    private readonly IItemUseCatalog _catalog;
    private readonly IPetSummonService _petSummon;
    private readonly ISkillCastService _states;
    private readonly IStatService _stats;

    private readonly Progression.ITitleService _titles;
    private readonly Huntaholic.IHuntaholicCatalog _huntaholics;

    public ItemUseService(ICharacterService characterService, IItemUseCatalog catalog,
        IPetSummonService petSummon, ISkillCastService states, IStatService stats,
        Progression.ITitleService titles = null, Huntaholic.IHuntaholicCatalog huntaholics = null)
    {
        _huntaholics = huntaholics;
        _titles = titles;
        _characterService = characterService;
        _catalog = catalog;
        _petSummon = petSummon;
        _states = states;
        _stats = stats;
    }

    /// <summary>The cool-time groups <c>TS_SC_ITEM_COOL_TIME</c> carries at Epic 7.3 (<c>&gt;= EPIC_6_2</c>).</summary>
    private const int MaxCoolTimeGroup = 40;

    public async Task UseAsync(GameClient client, GameActionPackets.UseItemRequest request)
    {
        var info = client.ConnectionInfo;
        await info.ItemUseLock.WaitAsync();
        try
        {
            await UseLockedAsync(client, request);
        }
        finally
        {
            info.ItemUseLock.Release();
        }
    }

    private async Task UseLockedAsync(GameClient client, GameActionPackets.UseItemRequest request)
    {
        var info = client.ConnectionInfo;
        var value = unchecked((int)request.ItemHandle);

        ItemEntity item;
        try
        {
            item = await _characterService.GetItemByHandleAsync(info.CharacterName, request.ItemHandle);
        }
        catch (Exception exception)
        {
            _logger.Error(exception, "Could not read item {itemHandle} for {clientTag}", request.ItemHandle,
                client.ClientTag);
            client.SendResult(UseItemRequestId, (ushort)ResultCode.DBError, value);
            return;
        }

        // The handler only ever looks the handle up in the character's own items, so an item that
        // resolves is owned; anything else is unknown to this client.
        if (item is null)
        {
            client.SendResult(UseItemRequestId, (ushort)ResultCode.NotExist, value);
            return;
        }

        // A resource the catalog does not know cannot be judged: refusing it would be an unproven
        // refusal, so the use is left ungated (fiche §6).
        if (_catalog.TryGetLevels((int)item.ItemResourceId, out var levels))
        {
            var gate = ItemUseRules.CheckUseLevel(info.CharacterLevel, levels);
            if (gate != ResultCode.Success)
            {
                client.SendResult(UseItemRequestId, (ushort)gate, value);
                return;
            }
        }

        var inHuntaholic = _huntaholics is not null && _huntaholics.GetHuntaholicId(info.X, info.Y) != 0;
        var huntaholicGate = ItemUseRules.CheckHuntaholic(_catalog.UseFlags((int)item.ItemResourceId), inHuntaholic);
        if (huntaholicGate != ResultCode.Success)
        {
            client.SendResult(UseItemRequestId, (ushort)huntaholicGate, value);
            return;
        }

        // The cool-down is per group and only for groups 1..40, the 40 slots of TS_SC_ITEM_COOL_TIME (217):
        // NGemity Player::IsUseableItem / UseItem (Player.cpp:2091-2092, 2165-2166) ignore cool_time when
        // cool_time_group is 0. A per-item delay the client cannot display would refuse a use it shows ready.
        var hasFields = _catalog.TryGetUseFields((int)item.ItemResourceId, out var fields);
        var grouped = hasFields && fields.CoolTimeGroup is >= 1 and <= MaxCoolTimeGroup;
        var cooldownKey = grouped ? -fields.CoolTimeGroup : 0;
        var now = ServerClock.Now;
        if (grouped && info.ItemCooldowns.TryGetValue(cooldownKey, out var readyAt)
            && unchecked((int)(now - readyAt)) < 0)
        {
            client.SendResult(UseItemRequestId, (ushort)ResultCode.CoolTime, value);
            return;
        }

        // A pet rename item (effect RenamePet) needs a pet out; refused before anything is consumed.
        if (_catalog.RenamesPet((int)item.ItemResourceId) && !_petSummon.HasPetOut(client))
        {
            _logger.Debug("{clientTag} used pet rename item {resourceId} with no pet out: refused",
                client.ClientTag, item.ItemResourceId);
            client.SendResult(UseItemRequestId, (ushort)ResultCode.NotActable, value);
            return;
        }

        // An item whose skill needs a particular target (the Resurrection Scroll: a dead player in sight) is judged
        // before anything is spent.
        if (hasFields)
        {
            foreach (var skillId in SkillSlots(fields))
            {
                var check = _states.CheckItemSkillTarget(client, skillId, request.TargetHandle);
                if (check != ResultCode.Success)
                {
                    client.SendResult(UseItemRequestId, (ushort)check, value);
                    return;
                }
            }
        }

        // NGemity erases the unit inside Player::UseItem, so the stack update (TS_SC_UPDATE_ITEM_COUNT,
        // or TS_SC_DESTROY_ITEM for the last unit) leaves before the result.
        if (_catalog.IsConsumedOnUse((int)item.ItemResourceId))
        {
            long? remaining;
            try
            {
                remaining = await _characterService.ConsumeItemAsync(info.CharacterName, request.ItemHandle, 1);
            }
            catch (Exception exception)
            {
                _logger.Error(exception, "Could not consume item {itemHandle} for {clientTag}",
                    request.ItemHandle, client.ClientTag);
                client.SendResult(UseItemRequestId, (ushort)ResultCode.DBError, value);
                return;
            }

            // The item can vanish between the read and the consumption (a concurrent erase).
            if (remaining is null)
            {
                client.SendResult(UseItemRequestId, (ushort)ResultCode.NotExist, value);
                return;
            }

            client.Connection.Send(remaining == 0
                ? GameCharacterPackets.BuildDestroyItem(request.ItemHandle)
                : GameCharacterPackets.BuildUpdateItemCount(request.ItemHandle, remaining.Value));
        }

        // A successful use answers twice, in the order of NGemity's WorldSession::onUseItem
        // (Chihiro/src/Network/GameNetwork/WorldSession.cpp:1336 then :1375): the generic
        // acknowledgement, then the use result echoing the item and target handles of the request.
        client.SendResult(UseItemRequestId, (ushort)ResultCode.Success, value);
        client.Connection.Send(GameCharacterPackets.BuildUseItemResult(request.ItemHandle, request.TargetHandle));
        if (hasFields)
        {
            if (grouped && fields.CoolTime > 0)
            {
                info.ItemCooldowns[cooldownKey] = unchecked(now + ServerClock.FromSeconds(fields.CoolTime));
                client.Connection.Send(GameCharacterPackets.BuildItemCoolTime(info.ItemCooldowns, now));
            }
            ApplyEffects(client, fields, request.TargetHandle);
        }
        _logger.Debug("{clientTag} used item {resourceId} (handle {itemHandle}, target {targetHandle})",
            client.ClientTag, item.ItemResourceId, request.ItemHandle, request.TargetHandle);
        // UpdateTitleConditionByItemUse (StructPlayer::UseItem).
        _ = _titles?.RecordAsync(client, Progression.TitleEvents.ItemUsed((int)item.ItemResourceId));

        // A cage is a reusable item (type Use): the use is acknowledged like any other, then the pet comes
        // out, goes away or is swapped. The pet frames follow the acknowledgement.
        if (await _petSummon.TryUseCageAsync(client, item.ItemResourceId, request.ItemHandle))
        {
            return;
        }

        if (_catalog.RenamesPet((int)item.ItemResourceId))
        {
            _petSummon.OfferRename(client);
        }
    }

    private void ApplyEffects(GameClient client,
        Navislamia.Game.DataAccess.Repositories.Interfaces.ItemUseFields fields, uint targetHandle = 0)
    {
        ApplySlots(client, fields.BaseTypes, fields.BaseVar1, fields.BaseVar2, fields, targetHandle);
        ApplySlots(client, fields.OptTypes, fields.OptVar1, fields.OptVar2, fields, targetHandle);
    }

    /// <summary>The skill ids an item's <c>Skill</c> slots cast.</summary>
    private static System.Collections.Generic.IEnumerable<int> SkillSlots(Navislamia.Game.DataAccess.Repositories.Interfaces.ItemUseFields fields)
    {
        foreach (var (types, values) in new[] { (fields.BaseTypes, fields.BaseVar1), (fields.OptTypes, fields.OptVar1) })
        {
            if (types is null || values is null)
            {
                continue;
            }

            for (var i = 0; i < Math.Min(types.Length, values.Length); i++)
            {
                if ((ItemEffectInstant)types[i] == ItemEffectInstant.Skill && values[i] > 0)
                {
                    yield return (int)values[i];
                }
            }
        }
    }

    private void ApplySlots(GameClient client, short[] types, decimal[] values, decimal[] levels,
        Navislamia.Game.DataAccess.Repositories.Interfaces.ItemUseFields fields, uint targetHandle = 0)
    {
        if (types is null || values is null) return;
        var info = client.ConnectionInfo;
        for (var i = 0; i < Math.Min(types.Length, values.Length); i++)
        {
            var amount = (int)values[i];
            switch ((ItemEffectInstant)types[i])
            {
                case ItemEffectInstant.AddImmoralPoint:
                    MoralityRules.Set(client, Math.Max(0m, info.ImmoralPoint + values[i]));
                    break;
                case ItemEffectInstant.SetImmoralPoint:
                    MoralityRules.Set(client, Math.Max(0m, values[i]));
                    break;
                case ItemEffectInstant.IncHp:
                case ItemEffectInstant.IncHpPercent:
                    if (info.CharacterHp <= 0) break;
                    var maxHp = Math.Max(1, (int)_stats.Compute(info).Total.MaxHp);
                    var hpAdd = (ItemEffectInstant)types[i] == ItemEffectInstant.IncHpPercent
                        ? (int)(maxHp * values[i]) : amount;
                    info.CharacterHp = Math.Min(maxHp, Math.Max(0, info.CharacterHp + hpAdd));
                    client.SendVitalProperty(GameStatPackets.BuildProperty(info.CharacterHandle, "hp", info.CharacterHp));
                    break;
                case ItemEffectInstant.IncMp:
                case ItemEffectInstant.IncMpPercent:
                    var maxMp = Math.Max(0, (int)_stats.Compute(info).Total.MaxMp);
                    var mpAdd = (ItemEffectInstant)types[i] == ItemEffectInstant.IncMpPercent
                        ? (int)(maxMp * values[i]) : amount;
                    info.CharacterMp = Math.Min(maxMp, Math.Max(0, info.CharacterMp + mpAdd));
                    client.SendVitalProperty(GameStatPackets.BuildProperty(info.CharacterHandle, "mp", info.CharacterMp));
                    break;
                case ItemEffectInstant.AddState when fields.StateId is > 0 && fields.StateTime > 0:
                    _states.ApplyState(client, (int)fields.StateId.Value, fields.StateLevel,
                        ServerClock.FromSeconds(fields.StateTime));
                    break;
                case ItemEffectInstant.Skill when amount > 0:
                    if (!ApplyRecoverySkill(client, fields, amount))
                        _states.ApplyItemSkill(client, amount,
                            levels is not null && i < levels.Length ? (int)levels[i] : 1, targetHandle);
                    break;
            }
        }
    }

    private bool ApplyRecoverySkill(GameClient client,
        Navislamia.Game.DataAccess.Repositories.Interfaces.ItemUseFields fields, int skillId)
    {
        if (fields.RecoverySkills is null) return false;
        foreach (var skill in fields.RecoverySkills)
        {
            if (skill.SkillId != skillId) continue;
            var info = client.ConnectionInfo;
            if (info.CharacterHp <= 0) return true;
            if (skill.EffectType == (int)SkillEffectType.AddHpByItem)
            {
                var max = Math.Max(1, (int)_stats.Compute(info).Total.MaxHp);
                info.CharacterHp = Math.Min(max, info.CharacterHp + skill.Amount);
                client.SendVitalProperty(GameStatPackets.BuildProperty(info.CharacterHandle, "hp", info.CharacterHp));
            }
            else if (skill.EffectType == (int)SkillEffectType.AddMpByItem)
            {
                var max = Math.Max(0, (int)_stats.Compute(info).Total.MaxMp);
                info.CharacterMp = Math.Min(max, info.CharacterMp + skill.Amount);
                client.SendVitalProperty(GameStatPackets.BuildProperty(info.CharacterHandle, "mp", info.CharacterMp));
            }
            return true;
        }
        return false;
    }
}
