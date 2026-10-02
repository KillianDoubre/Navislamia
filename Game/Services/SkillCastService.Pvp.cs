using System;
using System.Collections.Generic;
using System.Linq;
using Navislamia.Game.DataAccess.Repositories.Interfaces;
using Navislamia.Game.Network.Clients;
using Navislamia.Game.Network.Packets;
using Navislamia.Game.Network.Packets.Game;
using Navislamia.Game.Services.Buffs;
using Navislamia.Game.Services.Casting;

namespace Navislamia.Game.Services;

public partial class SkillCastService
{
    // -1 is the existing no-target marker. Negative values below it identify player handles without
    // colliding with monster instance IDs (which may have the same numeric value as a player handle).
    private static long PlayerTargetId(uint handle) => -(long)handle - 2;
    private static bool IsPlayerTarget(long id) => id < -1;
    private static uint PlayerTargetHandle(long id) => checked((uint)(-id - 2));

    private GameClient ResolvePlayerTarget(long id) => IsPlayerTarget(id) && _players is not null
        && _players.Registry.TryResolve(PlayerTargetHandle(id), out var player) ? player : null;

    private bool TryHostilePlayer(GameClient caster, uint handle, CastableBuffFields fields,
        out GameClient target, out ResultCode error)
    {
        target = null;
        var info = caster.ConnectionInfo;
        bool seen;
        lock (info.PlayerVisibilityLock) seen = info.SpawnedPlayers.ContainsKey(handle);
        if (!seen || _players is null || !_players.Registry.TryResolve(handle, out target))
        { error = ResultCode.NotExist; return false; }
        if (!fields.UseOnCharacter || target.ConnectionInfo.CharacterHp <= 0
            || target.ConnectionInfo.Layer != info.Layer || !_combatService.ArePlayerEnemies(caster, target))
        { error = ResultCode.NotActable; return false; }
        error = ResultCode.Success;
        return true;
    }

    private bool PlayerCastInRange(ConnectionInfo info, CastableBuffFields fields, GameClient target, uint now)
    {
        var p = SkillCastRangeRules.PlayerPosition(info, now);
        var q = SkillCastRangeRules.PlayerPosition(target.ConnectionInfo, now);
        var weapon = _statService.Compute(info).Total.AttackRange;
        return SkillCastRangeRules.AppliesTo(fields.EffectType)
            ? SkillCastRangeRules.InRange(fields.CastRange, weapon, CombatRange.Distance(p.X, p.Y, q.X, q.Y),
                CombatRange.PlayerUnitSize, CombatRange.PlayerUnitSize, SkillCastRangeRules.IsPlayerMoving(target.ConnectionInfo, now))
            : CastRules.InRange(fields.CastRange, weapon, p.X, p.Y, CombatRange.PlayerUnitSize,
                q.X, q.Y, CombatRange.PlayerUnitSize, SkillCastRangeRules.IsPlayerMoving(target.ConnectionInfo, now));
    }

    private uint DamageHandle(GameClient recipient, long id)
    {
        if (!IsPlayerTarget(id)) return recipient.ConnectionInfo.GetMonsterHandle(id);
        var handle = PlayerTargetHandle(id);
        var info = recipient.ConnectionInfo;
        if (info.CharacterHandle == handle) return handle;
        lock (info.PlayerVisibilityLock) return info.SpawnedPlayers.ContainsKey(handle) ? handle : 0;
    }

    private sealed record DamageTarget(long Id, MonsterInstance? Monster = null, GameClient Player = null)
    {
        public int Level => Player?.ConnectionInfo.CharacterLevel ?? Monster.Value.Level;
    }

    private (float X, float Y) DamagePosition(DamageTarget target, uint tick) => target.Player is { } player
        ? SkillCastRangeRules.PlayerPosition(player.ConnectionInfo, tick) : _monsterState.GetPosition(target.Id);

    private bool DamageTargetAlive(GameClient caster, CastableBuffFields fields, DamageTarget target) => target.Player is { } player
        ? player.ConnectionInfo.CharacterHp > 0 && fields.UseOnCharacter && _combatService.ArePlayerEnemies(caster, player)
            && _players.Registry.TryResolve(player.ConnectionInfo.CharacterHandle, out var current) && ReferenceEquals(player, current)
        : _monsterState.IsAlive(target.Id) && _monsterState.GetHp(target.Id) > 0;

    private IEnumerable<DamageTarget> HostileDamagePlayers(GameClient caster, CastableBuffFields fields)
    {
        if (!fields.UseOnCharacter || _players is null) yield break;
        foreach (var player in _players.Registry.Clients)
            if (!ReferenceEquals(caster, player) && player.ConnectionInfo.CharacterHp > 0
                && _combatService.ArePlayerEnemies(caster, player))
                yield return new DamageTarget(PlayerTargetId(player.ConnectionInfo.CharacterHandle), Player: player);
    }

    private void ApplyPlayerDebuff(GameClient caster, GameClient target, CastableBuffFields fields, int level, uint now)
    {
        var chance = CastRules.StateLandingChance(fields.EffectType, _statService.Compute(caster.ConnectionInfo).Total.MagicAccuracy,
            _statService.Compute(target.ConnectionInfo).Total.MagicAvoid,
            SkillDamageCurve.HitBonus(fields, caster.ConnectionInfo.CharacterLevel, target.ConnectionInfo.CharacterLevel),
            fields.ProbabilityOnHit, fields.ProbabilityIncBySlv, level);
        if (!CastRules.StateLands(chance, _random.Next(100))) return;
        if (ApplyState(target, fields.StateId, fields.SkillId, BuffCurve.StateLevel(fields, level), now,
                unchecked(now + BuffCurve.DurationTicks(fields, level)), caster.ConnectionInfo.CharacterHandle))
            SendStatRefresh(target, target.ConnectionInfo);
    }
}
