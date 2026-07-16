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
    public const int FirstIndex = 1;

    private const int ByteMax = 255;
    private const int CategoryShift = 48;
    private const int GroupShift = 40;
    private const int RankShift = 32;

    public static int CategoryOrder(int category)
    {
        return category switch
        {
            1 => 0,
            3 => 1,
            2 => 2,
            6 => 3,
            _ => 4
        };
    }

    public static ulong BuildResourceKey(int category, int group, int rank, long resourceId)
    {
        var packedCategory = (ulong)ClampByte(CategoryOrder(category)) << CategoryShift;
        var packedGroup = (ulong)ClampByte(group) << GroupShift;
        var packedRank = (ulong)(ByteMax - ClampByte(rank)) << RankShift;
        return packedCategory | packedGroup | packedRank | (uint)resourceId;
    }

    public static ulong BuildUnknownResourceKey(long resourceId)
    {
        return (((ulong)ByteMax << CategoryShift) | ((ulong)ByteMax << GroupShift)) | (uint)resourceId;
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
            if (items[i].Idx == i + FirstIndex)
            {
                continue;
            }

            items[i].Idx = i + FirstIndex;
            changed = true;
        }

        return changed;
    }

    private static bool IsContiguous(ItemEntity[] bag)
    {
        var seen = new bool[bag.Length];
        foreach (var item in bag)
        {
            var slot = item.Idx - FirstIndex;
            if (slot < 0 || slot >= bag.Length || seen[slot])
            {
                return false;
            }

            seen[slot] = true;
        }

        return true;
    }

    private static int ClampByte(int value)
    {
        return Math.Clamp(value, 0, ByteMax);
    }
}
