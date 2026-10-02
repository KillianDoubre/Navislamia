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

    // Retail: AND within each group; satisfying any defined group is enough.
    public static bool Satisfied(IEnumerable<TitleCondition> conditions, IReadOnlyDictionary<int, long> counters)
    {
        var groups = conditions.GroupBy(c => c.Group).ToArray();
        return groups.Length == 0 || groups.Any(group => group.All(c => counters.GetValueOrDefault(c.TypeId) >= c.Count));
    }
}
