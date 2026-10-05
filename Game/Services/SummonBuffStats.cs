using System.Linq;
using Navislamia.Game.Services.Stats;

namespace Navislamia.Game.Services;

public static class SummonBuffStats
{
    public static void Refresh(SummonPresence summon, IStateCatalog catalog)
    {
        lock (summon.BuffLock)
        {
            var stats = summon.Entry.BaseStats?.Copy()
                ?? new StatBlock { MaxHp = summon.Entry.MaxHp, MaxMp = summon.Entry.MaxMp };
            StatCalculator.ApplyEffects(stats, summon.ActiveBuffs
                .SelectMany(s => catalog.Resolve(s.StateId, s.StateLevel))
                .Select(e => e.Target == StatTarget.MaxStamina ? e with { Target = StatTarget.MaxSp } : e).ToArray());
            summon.Stats = stats;
            summon.Hp = System.Math.Min(summon.Hp, System.Math.Max(0, (int)stats.MaxHp));
            summon.Mp = System.Math.Min(summon.Mp, System.Math.Max(0, (int)stats.MaxMp));
        }
    }
}
