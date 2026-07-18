using System;
using System.Collections.Generic;

namespace Navislamia.Game.Services;

public static class DropRoll
{
    /// <summary>A group reference that loops back to itself would spin forever; give up after this depth.</summary>
    private const int MaxGroupDepth = 8;

    private static readonly IReadOnlyDictionary<int, DropGroupEntry[]> NoGroups =
        new Dictionary<int, DropGroupEntry[]>();

    /// <summary>Convenience for a table of direct items only; a group reference resolves to nothing.</summary>
    public static IReadOnlyList<DroppedItem> Roll(IReadOnlyList<DropEntry> entries, Random random,
        double chanceMultiplier = 1)
    {
        return Roll(entries, NoGroups, random, chanceMultiplier);
    }

    public static IReadOnlyList<DroppedItem> Roll(IReadOnlyList<DropEntry> entries,
        IReadOnlyDictionary<int, DropGroupEntry[]> groups, Random random, double chanceMultiplier = 1)
    {
        if (entries.Count == 0)
        {
            return Array.Empty<DroppedItem>();
        }

        List<DroppedItem> dropped = null;
        for (var i = 0; i < entries.Count; i++)
        {
            var entry = entries[i];
            var chance = Math.Min(1, entry.Chance * chanceMultiplier);
            if (random.NextDouble() >= chance)
            {
                continue;
            }

            var count = Roll(entry.MinCount, entry.MaxCount, random);

            if (entry.ItemId > 0)
            {
                Add(ref dropped, entry.ItemId, count);
                continue;
            }

            // A negative id is a drop group. The reference resolves the group once per rolled count,
            // each pick yielding one item, so a slot with count 6-20 drops that many group picks.
            for (var n = 0; n < count; n++)
            {
                if (TryResolveGroup(entry.ItemId, groups, random, out var itemId))
                {
                    Add(ref dropped, itemId, 1);
                }
            }
        }

        return (IReadOnlyList<DroppedItem>)dropped ?? Array.Empty<DroppedItem>();
    }

    /// <summary>
    /// Picks one item from a drop group by weight, following nested group references until a real item
    /// falls out. Mirrors the reference's <c>SelectItemIDFromDropGroup</c> called in a
    /// <c>do … while (id &lt; 0)</c> loop.
    /// </summary>
    public static bool TryResolveGroup(int groupId, IReadOnlyDictionary<int, DropGroupEntry[]> groups,
        Random random, out int itemId)
    {
        itemId = 0;
        var current = groupId;

        for (var depth = 0; depth < MaxGroupDepth && current < 0; depth++)
        {
            if (groups is null || !groups.TryGetValue(current, out var members) || members.Length == 0)
            {
                return false;
            }

            current = PickWeighted(members, random);
            if (current == 0)
            {
                return false;
            }
        }

        if (current <= 0)
        {
            return false;
        }

        itemId = current;
        return true;
    }

    private static int PickWeighted(DropGroupEntry[] members, Random random)
    {
        var total = 0.0;
        for (var i = 0; i < members.Length; i++)
        {
            total += members[i].Weight;
        }

        if (total <= 0)
        {
            return 0;
        }

        // Group weights sum to ~1; scaling by the actual total keeps the pick correct if they do not.
        var target = random.NextDouble() * total;
        var cumulative = 0.0;
        for (var i = 0; i < members.Length; i++)
        {
            cumulative += members[i].Weight;
            if (target < cumulative)
            {
                return members[i].ItemId;
            }
        }

        return members[members.Length - 1].ItemId;
    }

    private static int Roll(int min, int max, Random random)
    {
        return min >= max ? min : random.Next(min, max + 1);
    }

    private static void Add(ref List<DroppedItem> dropped, int itemId, long count)
    {
        (dropped ??= new List<DroppedItem>()).Add(new DroppedItem(itemId, count));
    }
}
