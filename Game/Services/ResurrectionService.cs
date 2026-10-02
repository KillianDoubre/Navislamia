using System;
using System.Threading;
using System.Threading.Tasks;
using Navislamia.Game.Network.Clients;
using Navislamia.Game.Network.Packets;
using Navislamia.Game.Network.Packets.Enums;
using Navislamia.Game.Network.Packets.Game;
using Navislamia.Game.Services.Interfaces;
using Navislamia.Game.Services.Buffs;
using Navislamia.Game.Services.Stats;
using Serilog;

namespace Navislamia.Game.Services;

/// <summary>
/// Handles <c>TM_CS_RESURRECTION</c> (513): a player character at 0 HP is brought back at its return
/// point. The death itself is carried by the hit that lands (<c>target_hp = 0</c>) and by the
/// <c>hp</c> property — this version has no server-side death packet (§3.2 of the specification).
/// </summary>
/// <remarks>
/// The return point is the position persisted with the character at world entry, kept in
/// <see cref="ConnectionInfo"/> by <c>GameActions.OnLogin</c>: the specification's option (a) of §16.1,
/// which needs neither a new column nor a migration. A character's persisted position is only written
/// when progress is saved, so the in-memory value is the stored one for the whole session.
/// </remarks>
public class ResurrectionService : IResurrectionService
{
    private readonly ILogger _logger = Log.ForContext<ResurrectionService>();
    private readonly IWarpService _warpService;
    private readonly IStatService _statService;
    private readonly IStateCatalog _stateCatalog;
    private readonly ISkillCastService _skillCastService;
    private readonly ICharacterService _characterService;
    private readonly IResurrectionItemCatalog _resurrectionItems;
    private readonly ILevelingService _leveling;
    private readonly Compete.ICompeteService _compete;

    public ResurrectionService(IWarpService warpService, IStatService statService, IStateCatalog stateCatalog,
        ISkillCastService skillCastService, ICharacterService characterService,
        IResurrectionItemCatalog resurrectionItems, ILevelingService leveling = null,
        Compete.ICompeteService compete = null)
    {
        _compete = compete;
        _leveling = leveling;
        _warpService = warpService;
        _statService = statService;
        _stateCatalog = stateCatalog;
        _skillCastService = skillCastService;
        _characterService = characterService;
        _resurrectionItems = resurrectionItems;
    }

    public void Resurrect(GameClient client, GameActionPackets.ResurrectionRequest request)
    {
        const ushort requestId = (ushort)GamePackets.TM_CS_RESURRECTION;
        var info = client.ConnectionInfo;

        // The handle is logged, not checked (ResurrectionRules.CheckRequest): what the 7.3 client puts there
        // is not established, and this line is how it gets measured.
        _logger.Debug("{clientTag} TM_CS_RESURRECTION type={type} handle={handle:X8} (character {characterHandle:X8})",
            client.ClientTag, request.Type, request.Handle, info.CharacterHandle);

        var result = ResurrectionRules.CheckRequest(request.Type, info.CharacterHandle, info.CharacterHp);
        if (result != ResultCode.Success)
        {
            client.SendResult(requestId, (ushort)result);
            return;
        }

        if (request.Type == ResurrectionType.Compete)
        {
            // Only the loser of a duel who died for it: ResurrectByCompete, in place, a tenth of the HP, no penalty.
            if (_compete is null || !_compete.ConsumeLoss(client))
            {
                client.SendResult(requestId, (ushort)ResultCode.NotActable);
                return;
            }

            var hp = ResurrectionRules.CompeteHp(_statService.Compute(info).Total.MaxHp);
            info.CharacterHp = hp;
            client.SendVitalProperty(GameStatPackets.BuildProperty(info.CharacterHandle, "hp", hp));
            client.SendResult(requestId, (ushort)ResultCode.Success);
            return;
        }

        if (request.Type == ResurrectionType.UseState)
        {
            ResurrectByState(client, requestId);
            return;
        }

        if (request.Type == ResurrectionType.UsePotion)
        {
            // The one path that waits on the database: a coalesced second request must not pass the dead
            // check while the first is still consuming its item.
            if (Interlocked.CompareExchange(ref info.ResurrectionInProgress, 1, 0) != 0)
            {
                client.SendResult(requestId, (ushort)ResultCode.NotActable);
                return;
            }

            _ = ResurrectByItemAsync(client, requestId);
            return;
        }

        try
        {
            var stats = _statService.Compute(info).Total;
            var (hp, mp) = ResurrectionRules.RestoredVitals(stats.MaxHp, stats.MaxMp);

            // Restore the vitals before moving: hp > 0 is the whole of the alive state, and the monster
            // AI drops a target at 0 HP, so nothing can hit the character on the way.
            info.CharacterHp = hp;
            info.CharacterMp = mp;

            // The way back to town gives no experience back: the death's loss is final.
            info.DeathExpLoss = 0;

            // The return point carries its own layer: the character may have died on another layer of
            // the same map. Warp stops the attack, drops the aggro, leaves every visible object and
            // re-streams the surroundings around the new position.
            info.Layer = info.RespawnLayer;
            _warpService.Warp(client, info.RespawnX, info.RespawnY);

            client.SendVitalProperty(GameStatPackets.BuildProperty(info.CharacterHandle, "hp", hp));
            client.SendVitalProperty(GameStatPackets.BuildProperty(info.CharacterHandle, "mp", mp));

            // Which of these three frames closes the client's death window is not established
            // (§15.2): the recommendation is to send the result, the warp and the vitals together.
            client.SendResult(requestId, (ushort)ResultCode.Success);

            _logger.Debug("{clientTag} resurrected at ({x}, {y}) with {hp} hp", client.ClientTag,
                info.RespawnX, info.RespawnY, hp);
        }
        catch (Exception exception)
        {
            _logger.Error(exception, "Could not resurrect {clientTag}", client.ClientTag);
        }
    }

    /// <summary>
    /// <c>RT_UsePotion</c> (<c>StructPlayer::ResurrectByPotion</c>, 2012-11 <c>0x1400e5650</c>): the character comes
    /// back <b>where it fell</b> by using one resurrection potion from its bag, looked for in the reference's order
    /// (2010454, 2902042, 910005, 910004, effect 114). HP = <c>var1 × max HP</c>, the MP stay as they are, and
    /// <c>var2</c> of the death's experience comes back. One unit is consumed; the stack update (255, or 254 for
    /// the last one) precedes the vitals and the result. Without a potion: <c>NotActable</c>, nothing changes.
    /// The Resurrection Scroll (603002) is not one of them: it is skill 6001, used on <b>another</b> dead player.
    /// </summary>
    private async Task ResurrectByItemAsync(GameClient client, ushort requestId)
    {
        var info = client.ConnectionInfo;
        try
        {
            ResurrectionPotion used = default;
            (Navislamia.Game.DataAccess.Entities.Telecaster.ItemEntity Item, long Remaining)? consumed = null;
            foreach (var potion in _resurrectionItems.Potions)
            {
                consumed = await _characterService.ConsumeFirstAsync(info.CharacterName,
                    item => item.ItemResourceId == potion.ItemResourceId);
                if (consumed is not null)
                {
                    used = potion;
                    break;
                }
            }

            if (consumed is not { } result)
            {
                client.SendResult(requestId, (ushort)ResultCode.NotActable);
                return;
            }

            var handle = (uint)result.Item.Id;
            client.Connection.Send(result.Remaining == 0
                ? GameCharacterPackets.BuildDestroyItem(handle)
                : GameCharacterPackets.BuildUpdateItemCount(handle, result.Remaining));

            var stats = _statService.Compute(info).Total;
            var hp = ResurrectionRules.PotionHp(used.HpRatio, stats.MaxHp);
            info.CharacterHp = hp;

            client.SendVitalProperty(GameStatPackets.BuildProperty(info.CharacterHandle, "hp", hp));
            var restored = _leveling?.RestoreDeathExperience(client, used.ExpRatio) ?? 0;
            client.SendResult(requestId, (ushort)ResultCode.Success);

            _logger.Debug("{clientTag} resurrected in place by potion {itemId} with {hp} hp and {exp} exp back",
                client.ClientTag, used.ItemResourceId, hp, restored);
        }
        catch (Exception exception)
        {
            _logger.Error(exception, "Could not resurrect {clientTag} by item", client.ClientTag);
            client.SendResult(requestId, (ushort)ResultCode.DBError);
        }
        finally
        {
            Volatile.Write(ref info.ResurrectionInProgress, 0);
        }
    }

    /// <summary>
    /// <c>RT_UseState</c>: the character comes back <b>where it fell</b>, on the strength of a resurrection
    /// state it carried when it died (a buff such as skill 3472's state 13472). The port of NGemity's
    /// <c>WorldSession::onRevive</c> + <c>Unit::ResurrectByState</c>: pick the highest-level resurrection
    /// state, give back its share of HP and MP, take the state off, answer 513. Without such a state the
    /// answer is <c>NotActable</c>, as in the reference. See docs/packet-specs/socle-effets-resurrection.md.
    /// </summary>
    private void ResurrectByState(GameClient client, ushort requestId)
    {
        var info = client.ConnectionInfo;
        try
        {
            ActiveBuff[] active;
            lock (info.BuffLock)
            {
                active = info.ActiveBuffs.ToArray();
            }

            if (!ResurrectionRules.TrySelectState(active, _stateCatalog.TryGetResurrection, out var state,
                    out var values))
            {
                client.SendResult(requestId, (ushort)ResultCode.NotActable);
                return;
            }

            var stats = _statService.Compute(info).Total;
            var (hp, mp) = ResurrectionRules.VitalsByState(values, state.StateLevel, stats.MaxHp, stats.MaxMp,
                info.CharacterMp);

            // Alive first, as on the town path: hp > 0 is the whole of the alive state.
            info.CharacterHp = hp;
            info.CharacterMp = mp;

            // The state is consumed by the resurrection (RemoveState in the reference): its removal and the
            // stat refresh reach the client before the vitals.
            _skillCastService.RemoveState(client, state.StateId);

            client.SendVitalProperty(GameStatPackets.BuildProperty(info.CharacterHandle, "hp", hp));
            client.SendVitalProperty(GameStatPackets.BuildProperty(info.CharacterHandle, "mp", mp));
            _leveling?.RestoreDeathExperience(client, values.ExpRatio(state.StateLevel));
            client.SendResult(requestId, (ushort)ResultCode.Success);

            _logger.Debug("{clientTag} resurrected in place by state {stateId} level {level} with {hp} hp",
                client.ClientTag, state.StateId, state.StateLevel, hp);
        }
        catch (Exception exception)
        {
            _logger.Error(exception, "Could not resurrect {clientTag} by state", client.ClientTag);
        }
    }
}
