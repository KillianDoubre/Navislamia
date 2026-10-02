using System;
using Navislamia.Game.DataAccess.Repositories.Interfaces;
using Navislamia.Game.Network.Clients;
using Navislamia.Game.Network.Packets.Game;
using Navislamia.Game.Services.Buffs;
using Navislamia.Game.Services.Interfaces;
using Serilog;

namespace Navislamia.Game.Services.MonsterSkills;

public interface IMonsterSkillService
{
    /// <summary>
    /// At the moment a monster may strike <paramref name="target"/>, rolls its skills and casts the first
    /// one that comes up. True when a skill was cast — it replaces the swing — with the time the monster
    /// spent casting in <paramref name="castTicks"/>.
    /// </summary>
    bool TryCast(GameClient target, long instanceId, uint monsterHandle, uint now, out uint castTicks);
}

/// <summary>
/// Monster skills (docs/packet-specs/socle-competences-monstres.md): the official pick of
/// <see cref="MonsterSkillRules.Pick"/>, then the same <c>TS_SC_SKILL</c> sequence a player cast sends —
/// <c>ST_Casting</c>, <c>ST_Fire</c> with the hit, <c>ST_Complete</c> — with the monster as caster.
/// </summary>
/// <remarks>
/// A damage skill rolls through <see cref="ICombatService.RollMonsterHit(long, GameClient, float, DamageKind, int, int)"/>,
/// so the hit, block, critical and defence rules are the swing's; a harmful state lands on the player through
/// <see cref="ISkillCastService.ApplyState"/> (icon, countdown, expiry and stat refresh of a buff); a self
/// state lands in <see cref="MonsterWorldState"/>, where the combat rule reads it; a heal restores the
/// monster's own HP. Must not inject <c>NetworkService</c>, the DI cycle that only fails at runtime.
/// </remarks>
public class MonsterSkillService : IMonsterSkillService
{
    private readonly ILogger _logger = Log.ForContext<MonsterSkillService>();
    private readonly IMonsterSkillCatalog _catalog;
    private readonly MonsterWorldState _world;
    private readonly ICombatService _combat;
    private readonly ISkillCastService _skillCast;
    private readonly ICombatRandom _random;
    private readonly IPlayerVisibilityService _players;

    public MonsterSkillService(IMonsterSkillCatalog catalog, MonsterWorldState world, ICombatService combat,
        ISkillCastService skillCast, ICombatRandom random = null, IPlayerVisibilityService players = null)
    {
        _players = players;
        _catalog = catalog;
        _world = world;
        _combat = combat;
        _skillCast = skillCast;
        _random = random ?? CombatRandom.Shared;
    }

    public bool TryCast(GameClient target, long instanceId, uint monsterHandle, uint now, out uint castTicks)
    {
        castTicks = 0;
        if (!_world.TryGetInstance(instanceId, out var instance))
        {
            return false;
        }

        var skills = _catalog.Get(instance.SkillLinkId);
        if (skills.Count == 0)
        {
            return false;
        }

        var skill = MonsterSkillRules.Pick(skills,
            candidate => _world.IsSkillReady(instanceId, candidate.Fields.SkillId, now), _random);
        if (skill is null)
        {
            return false;
        }

        castTicks = BuffCurve.CastDelayTicks(skill.Fields, skill.Level);
        var cooldown = BuffCurve.CooldownTicks(skill.Fields, skill.Level);
        if (cooldown > 0)
        {
            _world.SetSkillCooldown(instanceId, skill.Fields.SkillId, unchecked(now + cooldown));
        }

        Cast(target, instanceId, instance, monsterHandle, skill, now, castTicks);
        return true;
    }

    private void Cast(GameClient client, long instanceId, MonsterInstance instance, uint monsterHandle,
        MonsterSkill skill, uint now, uint castTicks)
    {
        var info = client.ConnectionInfo;
        var (mx, my) = _world.GetPosition(instanceId);
        var (x, y, z) = skill.OnSelf ? (mx, my, instance.Z) : (info.X, info.Y, info.Z);

        // One frame per recipient: the monster's handle is that client's, and so is the target's when the
        // monster casts on itself. The players who see the target watch the cast too.
        byte[] Frame(uint handle, SkillPacketType type, uint delay, SkillHit? hit)
        {
            var target = skill.OnSelf ? handle : info.CharacterHandle;
            var rehit = hit is { } h && skill.OnSelf ? h with { TargetHandle = handle } : hit;
            return GameSkillPackets.BuildSkill((ushort)skill.Fields.SkillId,
                (byte)Math.Clamp(skill.Level, 0, byte.MaxValue), handle, target, x, y, z, (byte)info.Layer, type, 0,
                0, _world.GetHp(instanceId), 0, delay, 0, rehit);
        }

        void Send(SkillPacketType type, uint delay = 0, SkillHit? hit = null)
        {
            client.Connection.Send(Frame(monsterHandle, type, delay, hit));
            ObserverFrames.SendMonsterFrame(_players, client, instanceId,
                (_, handle) => Frame(handle, type, delay, hit));
        }

        Send(SkillPacketType.Casting, castTicks);
        var fire = Apply(client, instanceId, instance, monsterHandle, skill, now);
        Send(SkillPacketType.Fire, 0, fire);
        Send(SkillPacketType.Complete);

        _logger.Debug("Monster {instanceId} cast {effect} {skillId} level {level} on {target}", instanceId,
            skill.Effect, skill.Fields.SkillId, skill.Level, skill.OnSelf ? "itself" : client.ClientTag);
    }

    private SkillHit? Apply(GameClient client, long instanceId, MonsterInstance instance, uint monsterHandle,
        MonsterSkill skill, uint now)
    {
        var info = client.ConnectionInfo;
        var fields = skill.Fields;

        switch (skill.Effect)
        {
            case MonsterSkillEffect.State when skill.OnSelf:
            {
                var duration = BuffCurve.DurationTicks(fields, skill.Level);
                if (duration == 0)
                {
                    return null;
                }

                var stateLevel = BuffCurve.StateLevel(fields, skill.Level);
                var state = _world.AddState(instanceId, fields.StateId, fields.SkillId, stateLevel, now,
                    unchecked(now + duration));
                client.Connection.Send(GameSkillPackets.BuildState(monsterHandle, state.StateHandle,
                    (uint)state.StateId, (ushort)stateLevel, state.EndTick, now));
                ObserverFrames.SendMonsterFrame(_players, client, instanceId, (_, watcherHandle) =>
                    GameSkillPackets.BuildState(watcherHandle, state.StateHandle, (uint)state.StateId,
                        (ushort)stateLevel, state.EndTick, now));
                return null;
            }

            case MonsterSkillEffect.State:
            {
                var duration = BuffCurve.DurationTicks(fields, skill.Level);
                var chance = Casting.CastRules.StateLandingChance(fields.EffectType,
                    _combat.GetMonsterStats(instanceId)?.MagicAccuracy ?? 0f,
                    _combat.GetPlayerStats(client)?.MagicAvoid ?? 0f,
                    SkillDamageCurve.HitBonus(fields, instance.Level, info.CharacterLevel), fields.ProbabilityOnHit,
                    fields.ProbabilityIncBySlv, skill.Level);
                if (duration > 0 && !info.IsImmortal && Casting.CastRules.StateLands(chance, _random.Next(100)))
                {
                    _skillCast.ApplyState(client, fields.StateId, BuffCurve.StateLevel(fields, skill.Level),
                        duration);
                }

                return null;
            }

            case MonsterSkillEffect.Heal:
            {
                var stats = _combat.GetMonsterStats(instanceId);
                var amount = HealCurve.Amount(fields.Vars, skill.Level, stats?.MagicPoint ?? 0f, instance.Hp);
                var healed = _world.Heal(instanceId, amount);
                return new SkillHit(SkillHitType.AddHp, monsterHandle, _world.GetHp(instanceId), healed);
            }

            default:
            {
                var stats = _combat.GetMonsterStats(instanceId);
                var magical = fields.Kind == SkillCastKind.MagicAttack;
                var baseDamage = MonsterSkillRules.BaseDamage(skill, stats?.AttackPointRight ?? 0f,
                    stats?.MagicPoint ?? 0f);
                var hit = _combat.RollMonsterHit(instanceId, client, baseDamage,
                    magical ? DamageKind.Magical : DamageKind.Physical,
                    SkillDamageCurve.HitBonus(fields, instance.Level, info.CharacterLevel),
                    SkillDamageCurve.CriticalBonus(fields, skill.Level));

                _combat.DamagePlayer(client, hit.Damage);
                return new SkillHit(magical ? SkillHitType.MagicDamage : SkillHitType.Damage, info.CharacterHandle,
                    info.CharacterHp, hit.Damage, (byte)hit.Flags);
            }
        }
    }
}
