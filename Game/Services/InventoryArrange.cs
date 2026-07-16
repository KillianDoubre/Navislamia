using System;
using Navislamia.Game.DataAccess.Entities.Telecaster;

namespace Navislamia.Game.Services;

public readonly record struct ItemOrderKey(ulong ResourceKey, long ItemId) : IComparable<ItemOrderKey>
{
    public int CompareTo(ItemOrderKey other)
    {
        var result = ResourceKey.CompareTo(other.ResourceKey);
        return result != 0 ? result : ItemId.CompareTo(other.ItemId);
    }
}

public static class InventoryArrange
{
    private const int ByteMax = 255;
    private const int GroupShift = 48;
    private const int TypeShift = 40;
    private const int RankShift = 32;

    public static ulong BuildResourceKey(int group, int itemType, int rank, long resourceId)
    {
        var packedGroup = (ulong)ClampByte(group) << GroupShift;
        var packedType = (ulong)ClampByte(itemType) << TypeShift;
        var packedRank = (ulong)(ByteMax - ClampByte(rank)) << RankShift;
        return packedGroup | packedType | packedRank | (uint)resourceId;
    }

    public static ulong BuildUnknownResourceKey(long resourceId)
    {
        return BuildResourceKey(ByteMax, ByteMax, 0, resourceId);
    }

    public static bool EnsureContiguousIndices(ItemEntity[] bag)
    {
        if (IsContiguous(bag))
        {
            return false;
        }

        Renumber(bag);
        return true;
    }

    public static bool Apply(ItemEntity[] items, ItemOrderKey[] keys)
    {
        Array.Sort(keys, items);
        return Renumber(items);
    }

    private static bool Renumber(ItemEntity[] items)
    {
        var changed = false;
        for (var i = 0; i < items.Length; i++)
        {
            if (items[i].Idx == i)
            {
                continue;
            }

            items[i].Idx = i;
            changed = true;
        }

        return changed;
    }

    private static bool IsContiguous(ItemEntity[] bag)
    {
        var seen = new bool[bag.Length];
        foreach (var item in bag)
        {
            var index = item.Idx;
            if (index < 0 || index >= bag.Length || seen[index])
            {
                return false;
            }

            seen[index] = true;
        }

        return true;
    }

    private static int ClampByte(int value)
    {
        return Math.Clamp(value, 0, ByteMax);
    }
}
