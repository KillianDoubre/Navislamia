using System;
using System.Linq;
using Navislamia.Game.DataAccess.Entities.Enums;
using Navislamia.Game.DataAccess.Repositories.Interfaces;
using Navislamia.Game.Network.Packets.Game;
using Navislamia.Game.Services.Buffs;
using Navislamia.Game.Services.Combat;

namespace Navislamia.Game.Services;

public partial class SkillCastService
{
    private void NotifySkillHit(CombatActor source, CombatActor target,
        CastableBuffFields fields, HitResult hit = default) =>
        _combatService.NotifyHit(source, target, hit,
            (fields.IsHarmful ? EnergyProcs.Harmful : EnergyProcs.Helpful)
                | (fields.Kind == SkillCastKind.PhysicalAttack ? EnergyProcs.PhysicalSkill : EnergyProcs.MagicalSkill),
            fields.ElementalType, skillId: fields.SkillId);

    /// <summary>StructStateProc::Proc: AddState from the proc owner, then subtract MP even if stacking refuses.</summary>
    public void ApplyCombatState(CombatActor target, CombatActor source, StateProc proc)
    {
        if (target.Owner?.ConnectionInfo.CharacterHandle is not > 0 || source.Owner is null
            || !_stateCatalog.Exists(proc.StateId)) return;
        var now = ServerClock.Now; var end = unchecked(now + proc.Duration);
        var stats = source.IsMonster ? _combatService.GetMonsterStats(source.MonsterId)
            : source.IsSummon ? Array.Find(source.Owner.ConnectionInfo.Summons, s => s.Handle == source.SummonHandle)?.Stats
            : _statService.Compute(source.Owner.ConnectionInfo).Total;
        var pulse = new StatePulse(now, _stateCatalog.Periodic(proc.StateId).SnapshotDamage(stats))
        { MonsterId = source.IsMonster ? source.MonsterId : -1, MonsterLife = source.IsMonster ? _monsterState.LifeVersion(source.MonsterId) : 0 };
        var sourceHandle = source.IsMonster ? target.Owner.ConnectionInfo.GetMonsterHandle(source.MonsterId) : source.Handle;
        var rule = _stateCatalog.GetRule(proc.StateId);
        var hp = target.IsMonster ? _monsterState.GetHp(target.MonsterId)
            : target.IsSummon ? Array.Find(target.Owner.ConnectionInfo.Summons, s => s.Handle == target.SummonHandle)?.Hp ?? 0
            : target.Owner.ConnectionInfo.CharacterHp;
        var refusesDead = hp <= 0 && (rule.TimeType & (StateTimeType.EraseOnDead | StateTimeType.EraseOnResurrect)) != 0;
        if (!refusesDead && target.IsMonster)
        {
            if (_monsterState.TryAddState(target.MonsterId, proc.StateId, proc.SkillId, proc.Level, now, end,
                rule, _stateCatalog.GetRule, out var state, out var removed, sourceHandle, pulse))
            {
                var recipients = (_players?.Registry.Clients ?? new[] { target.Owner }).Append(target.Owner).Distinct();
                foreach (var viewer in recipients)
                {
                    var handle = viewer.ConnectionInfo.GetMonsterHandle(target.MonsterId);
                    if (handle == 0) continue;
                    foreach (var old in removed) viewer.Connection.Send(GameSkillPackets.BuildStateRemoval(handle,
                        old.StateHandle, (uint)old.StateId));
                    viewer.Connection.Send(GameSkillPackets.BuildState(handle, state.StateHandle, (uint)state.StateId,
                        (ushort)state.StateLevel, end, now));
                }
            }
        }
        else if (!refusesDead)
        {
            if (target.IsSummon)
            {
                var summon = Array.Find(target.Owner.ConnectionInfo.Summons, s => s.Handle == target.SummonHandle);
                if (summon is null) return;
                var unit = new SupportTarget(target.Owner, summon);
                if (ApplySupportState(unit, proc.StateId, proc.SkillId, proc.Level, now, end, sourceHandle, suppliedPulse: pulse))
                    RefreshSupportStats(unit);
            }
            else if (ApplyState(target.Owner, proc.StateId, proc.SkillId, proc.Level, now, end, sourceHandle, suppliedPulse: pulse))
                SendStatRefresh(target.Owner, target.Owner.ConnectionInfo);
        }
        if (proc.MpCost == 0) return;
        if (source.IsMonster)
        { _monsterState.ChangeMp(source.MonsterId, -proc.MpCost, SupportAmount(stats?.MaxMp ?? 0)); return; }
        if (source.IsSummon)
        {
            var info = source.Owner.ConnectionInfo;
            Creatures.CreatureCard card; SummonPresence summon;
            lock (info.SummonLock)
            {
                card = info.CreatureCards.Values.FirstOrDefault(c => c.SummonHandle == source.SummonHandle);
                summon = Array.Find(info.Summons, s => s.Handle == source.SummonHandle);
            }
            if (card is null || summon is null) return;
            _creatures?.SetSummonVitals(source.Owner, card, summon.Hp,
                (int)Math.Clamp((long)summon.Mp - proc.MpCost, 0, SupportAmount(stats?.MaxMp ?? 0)));
        }
        else
        {
            var info = source.Owner.ConnectionInfo;
            lock (info.ProgressLock) info.CharacterMp = (int)Math.Clamp((long)info.CharacterMp - proc.MpCost, 0, SupportAmount(stats?.MaxMp ?? 0));
            source.Owner.SendVitalProperty(GameStatPackets.BuildProperty(info.CharacterHandle, "mp", info.CharacterMp));
        }
    }
}
