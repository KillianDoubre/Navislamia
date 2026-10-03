using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Navislamia.Game.DataAccess.Entities.Enums;
using Navislamia.Game.DataAccess.Repositories.Interfaces;
using Navislamia.Game.Services.Stats;

namespace Navislamia.Game.Services.Progression;

public sealed class TitleCatalog
{
    /// <summary><c>GameRule::SUB_TITLE_COUNT</c>: the secondary title slots.</summary>
    public const int SubTitleCount = 5;

    /// <summary><c>GameRule::SUB_TITLE_RATE_LIMIT</c>: the highest rate a secondary title may have.</summary>
    public const int SubTitleRateLimit = 5;

    /// <summary><c>GameRule::SUB_TITLE_RATE</c>: a secondary title gives 10 % of its options.</summary>
    public const float SubTitleRate = 0.1f;

    /// <summary><c>GameRule::TITLE_RESET_COOL_TIME</c>: 5 minutes before a main title may change again.</summary>
    public const uint MainTitleCoolTicks = 30000;

    public IReadOnlyDictionary<int, TitleResource> Titles { get; }
    public IReadOnlyDictionary<int, TitleConditionType> Types { get; }
    public IReadOnlyDictionary<int, TitleCondition[]> Conditions { get; }

    public TitleCatalog(ProgressionResources resources = null)
    {
        resources ??= ProgressionResources.Official;
        Titles = resources.Titles.ToDictionary(t => t.Id);
        Types = resources.ConditionTypes.ToDictionary(t => t.Id);
        Conditions = resources.Conditions.GroupBy(c => c.TitleId).ToDictionary(g => g.Key, g => g.ToArray());
    }

    public bool IsAvailable(int id, DateTime now) => Titles.TryGetValue(id, out var title)
        && (!title.Periodic || (DateTime.TryParse(title.Begin, CultureInfo.InvariantCulture, DateTimeStyles.None, out var begin)
            && DateTime.TryParse(title.End, CultureInfo.InvariantCulture, DateTimeStyles.None, out var end)
            && now >= begin && now <= end));

    public IReadOnlyList<StatEffect> GetEffects(int id) => IsAvailable(id, DateTime.UtcNow)
        ? ItemStatCatalog.BuildEffects(new ItemEffectFields(id, ItemType.Etc,
            Array.Empty<short>(), Array.Empty<decimal>(), Array.Empty<decimal>(),
            Titles[id].Types, Titles[id].Var1, Titles[id].Var2), 8) : Array.Empty<StatEffect>();

    public int RateOf(int id) => Titles.TryGetValue(id, out var title) ? title.Rate : 0;

    /// <summary>
    /// <c>applyStatByTitle</c>/<c>amplifyStatByTitle</c>: the main title's options in full, each secondary title's at
    /// <see cref="SubTitleRate"/>.
    /// </summary>
    public IReadOnlyList<StatEffect> GetEffects(int main, IReadOnlyList<int> subs)
    {
        var effects = GetEffects(main);
        if (subs is null)
        {
            return effects;
        }

        List<StatEffect> all = null;
        foreach (var sub in subs)
        {
            if (sub == 0)
            {
                continue;
            }

            foreach (var effect in GetEffects(sub))
            {
                (all ??= new List<StatEffect>(effects)).Add(effect with { Value = effect.Value * SubTitleRate });
            }
        }

        return (IReadOnlyList<StatEffect>)all ?? effects;
    }

    // Retail: AND within each group; satisfying any defined group is enough.
    public static bool Satisfied(IEnumerable<TitleCondition> conditions, IReadOnlyDictionary<int, long> counters)
    {
        var groups = conditions.GroupBy(c => c.Group).ToArray();
        return groups.Length == 0 || groups.Any(group => group.All(c => counters.GetValueOrDefault(c.TypeId) >= c.Count));
    }
}
