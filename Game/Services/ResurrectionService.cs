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

    public ResurrectionService(IWarpService warpService, IStatService statService, IStateCatalog stateCatalog,
        ISkillCastService skillCastService, ICharacterService characterService,
        IResurrectionItemCatalog resurrectionItems)
    {
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

            // The return point carries its own layer: the character may have died on another layer of
            // the same map. Warp stops the attack, drops the aggro, leaves every visible object and
            // re-streams the surroundings around the new position.
            info.Layer = info.RespawnLayer;
            _warpService.Warp(client, info.RespawnX, info.RespawnY);

            client.Connection.Send(GameStatPackets.BuildProperty(info.CharacterHandle, "hp", hp));
            client.Connection.Send(GameStatPackets.BuildProperty(info.CharacterHandle, "mp", mp));

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
    /// <c>RT_UsePotion</c>: the character comes back <b>where it fell</b> by using one resurrection item
    /// from its bag — in this data, the Resurrection Scroll (603002, skill 6001 level 1, 10% of max HP).
    /// NGemity leaves this branch empty; the effect is its item path (<c>Player::UseItem</c> →
    /// <c>ITEM_EFFECT_INSTANT::SKILL</c> → <c>SKILL_RESURRECTION</c>) applied by the dead character to itself.
    /// One unit is consumed and the stack update (255, or 254 for the last one) precedes the vitals and the
    /// result, the order of <c>TM_CS_USE_ITEM</c>. Without such an item: <c>NotActable</c>, nothing changes.
    /// </summary>
    private async Task ResurrectByItemAsync(GameClient client, ushort requestId)
    {
        var info = client.ConnectionInfo;
        try
        {
            ResurrectionItem used = default;
            var consumed = await _characterService.ConsumeFirstAsync(info.CharacterName,
                item => _resurrectionItems.TryGet((int)item.ItemResourceId, out used));

            if (consumed is not { } result || !_resurrectionItems.TryGet((int)result.Item.ItemResourceId, out used))
            {
                client.SendResult(requestId, (ushort)ResultCode.NotActable);
                return;
            }

            var handle = (uint)result.Item.Id;
            client.Connection.Send(result.Remaining == 0
                ? GameCharacterPackets.BuildDestroyItem(handle)
                : GameCharacterPackets.BuildUpdateItemCount(handle, result.Remaining));

            var stats = _statService.Compute(info).Total;
            var (hp, mp) = ResurrectionRules.VitalsBySkill(used.Effect, used.Vars, used.SkillLevel, stats.MaxHp,
                stats.MaxMp, info.CharacterMp);

            info.CharacterHp = hp;
            info.CharacterMp = mp;

            client.Connection.Send(GameStatPackets.BuildProperty(info.CharacterHandle, "hp", hp));
            client.Connection.Send(GameStatPackets.BuildProperty(info.CharacterHandle, "mp", mp));
            client.SendResult(requestId, (ushort)ResultCode.Success);

            _logger.Debug("{clientTag} resurrected in place by item {itemId} (skill {skillId} level {level}) with {hp} hp",
                client.ClientTag, used.ItemResourceId, used.SkillId, used.SkillLevel, hp);
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

            client.Connection.Send(GameStatPackets.BuildProperty(info.CharacterHandle, "hp", hp));
            client.Connection.Send(GameStatPackets.BuildProperty(info.CharacterHandle, "mp", mp));
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
