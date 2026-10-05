using System;
using System.Collections.Generic;
using System.Linq;
using Navislamia.Game.Network.Packets.Game;
using Navislamia.Game.Services.Buffs;
using Navislamia.Game.Services.Combat;

namespace Navislamia.Game.Services;

public partial class SkillCastService
{
    /// <summary>
    /// <c>StructCooldownProc::Proc</c> then <c>StructCreature::AddRemainCoolTime</c> on the proc's owner, a player or a
    /// summon: each targeted skill still cooling gets <c>inc</c> seconds more (or less), and the changed skills go to
    /// the client in one <c>TS_SC_SKILL_LIST</c> (403) with their cool time left.
    /// </summary>
    public void ApplyCooldownProc(CombatActor owner, CooldownProc proc)
    {
        if (owner.Owner is null || owner.IsMonster) return;
        var now = ServerClock.Now;
        var changed = new List<SkillListEntry>();
        var info = owner.Owner.ConnectionInfo;

        if (owner.IsSummon)
        {
            lock (info.SummonLock)
            {
                var card = info.CreatureCards.Values.FirstOrDefault(c => c.SummonHandle == owner.SummonHandle);
                if (card is null) return;
                Shift(card.SkillCooldowns, card.Skills, ruling: false,
                    (id, level, remain) => new SkillListEntry(id, level, card.SkillCooldownDurations.GetValueOrDefault(id), remain));
            }
        }
        else
        {
            lock (info.CastLock)
            {
                // AddRemainCoolTime leaves out Ruler of Time while it is the skill being cast.
                var ruling = info.PendingCast?.Request.SkillId == CooldownProcs.RulerOfTime;
                Shift(info.SkillCooldowns, info.LearnedSkills, ruling,
                    (id, level, remain) => new SkillListEntry(id, level, TotalCoolTime(id, level, remain), remain));
            }
        }

        if (changed.Count > 0)
        {
            owner.Owner.Connection.Send(GameCharacterPackets.BuildSkillList(owner.Handle, changed));
        }

        void Shift(Dictionary<int, uint> cooldowns, IReadOnlyDictionary<int, byte> learned, bool ruling,
            Func<int, byte, uint, SkillListEntry> entry)
        {
            if (proc.AllSkills)
            {
                foreach (var id in cooldowns.Keys.ToArray())
                {
                    if (id == CooldownProcs.Grace || ruling && id == CooldownProcs.RulerOfTime) continue;
                    One(id, proc.Inc);
                }
            }

            // skill_id < 0 is the all-skill call; a named one must be learned (GetSkillUID() > 0) and is never Grace.
            if (proc.SkillId1 > 0) One(proc.SkillId1, proc.Inc1);
            if (proc.SkillId2 > 0) One(proc.SkillId2, proc.Inc2);

            void One(int id, int inc)
            {
                if (id == CooldownProcs.Grace || !learned.TryGetValue(id, out var level) || level == 0
                    || !cooldowns.TryGetValue(id, out var readyAt) || unchecked((int)(readyAt - now)) <= 0) return;
                var shifted = CooldownProcs.Shift(now, readyAt, inc);
                if (shifted is { } ready) cooldowns[id] = ready;
                else cooldowns.Remove(id);
                var remain = shifted is { } r ? (uint)unchecked((int)(r - now)) : 0u;
                changed.RemoveAll(e => e.SkillId == id);
                changed.Add(entry(id, level, remain));
            }
        }
    }

    private uint TotalCoolTime(int skillId, byte level, uint remain) =>
        _catalog.TryGet(skillId, out var fields) ? Math.Max(BuffCurve.CooldownTicks(fields, level), remain) : remain;
}
