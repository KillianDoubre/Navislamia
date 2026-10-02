using System;
using System.Collections.Frozen;
using System.Collections.Generic;
using System.Linq;

namespace Navislamia.Game.Services.Rates;

/// <summary>The rates a <c>/rate</c> event can multiply.</summary>
public enum RateType
{
    Exp,
    Jp,
    Gold,
    ItemDrop,
    CreatureCardDrop,

    /// <summary>
    /// The chance of the chaos a monster drops, the official <c>GameRule::fChaosDropRate</c>
    /// (<c>0x1404f7dc0</c>): a factor on the per-cent chance, not on the amount
    /// (docs/packet-specs/socle-recompenses-monstres.md §7.2, §9.1).
    /// </summary>
    Chaos
}

/// <summary>The chat names of the rate types, and the labels the announcements use.</summary>
public static class RateTypes
{
    public static IReadOnlyList<RateType> All { get; } = Enum.GetValues<RateType>();

    private static readonly FrozenDictionary<string, RateType> ByName = new Dictionary<string, RateType>
    {
        ["exp"] = RateType.Exp,
        ["jp"] = RateType.Jp,
        ["gold"] = RateType.Gold,
        ["drop"] = RateType.ItemDrop,
        ["card"] = RateType.CreatureCardDrop,
        ["chaos"] = RateType.Chaos
    }.ToFrozenDictionary();

    /// <summary>
    /// Resolves the type argument of <c>/rate</c>: one of the names above, or <c>all</c> for every type.
    /// </summary>
    public static bool TryParse(string name, out IReadOnlyList<RateType> types)
    {
        var key = name?.ToLowerInvariant();
        if (key == "all")
        {
            types = All;
            return true;
        }

        if (key != null && ByName.TryGetValue(key, out var type))
        {
            types = new[] { type };
            return true;
        }

        types = Array.Empty<RateType>();
        return false;
    }

    /// <summary>The names <c>/rate</c> accepts, for its usage line.</summary>
    public static string Names => string.Join("|", ByName.Keys.OrderBy(Order).Append("all"));

    public static string Label(RateType type) => type switch
    {
        RateType.Exp => "EXP",
        RateType.Jp => "JP",
        RateType.Gold => "Gold",
        RateType.ItemDrop => "Drop",
        RateType.CreatureCardDrop => "Card drop",
        RateType.Chaos => "Chaos",
        _ => type.ToString()
    };

    /// <summary>"all rates" when a set covers every type, otherwise the labels in declaration order.</summary>
    public static string Label(IReadOnlyCollection<RateType> types)
    {
        if (types.Count == All.Count && All.All(types.Contains))
        {
            return "all rates";
        }

        return string.Join(", ", All.Where(types.Contains).Select(Label));
    }

    private static int Order(string name) => (int)ByName[name];
}
