using System;
using System.Collections.Frozen;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Extensions.Options;
using Navislamia.Configuration.Options;
using Navislamia.Game.Network.Packets;

namespace Navislamia.Game.Services.Auction;

/// <summary>
/// The auction categories (<c>AuctionManager::RegisterAuctionCategory</c>'s two-level tree) and the item names the
/// keyword search reads, from <c>DevConsole/auction-catalog.73.json</c> (<c>tools/export_auction_catalog.py</c>).
/// </summary>
public interface IAuctionCatalog
{
    /// <summary>The highest category id, which is also the list unclassified items land in (<c>m_nMaxCategoryIndex</c>).</summary>
    int MaxCategoryIndex { get; }

    /// <summary><c>getCategoryIndex</c>: the first sub-category, then parent, that takes the group/class; else the max index.</summary>
    int CategoryIndexOf(int group, int itemClass);

    /// <summary>Whether a category exists, and whether its node (parent when <paramref name="subCategoryId"/> is -1) takes the item.</summary>
    bool TryGetNode(int categoryId, int subCategoryId, out Func<int, int, bool> takes);

    bool TryGetItem(int code, out AuctionItemRow item);
}

public sealed class AuctionCatalog : IAuctionCatalog
{
    private readonly List<Node> _parents = new();
    private readonly FrozenDictionary<int, AuctionItemRow> _items;

    public AuctionCatalog(IOptions<AuctionCatalogOptions> options)
    {
        var value = options?.Value ?? new AuctionCatalogOptions();
        foreach (var row in value.Categories)
        {
            var parent = _parents.FirstOrDefault(p => p.CategoryId == row.CategoryId);
            if (row.SubCategoryId == AuctionRules.SubCategoryNone)
            {
                if (parent is null) _parents.Add(parent = new Node(row.CategoryId, AuctionRules.SubCategoryNone));
                parent.Pairs.Add((row.ItemGroup, row.ItemClass));
                MaxCategoryIndex = Math.Max(MaxCategoryIndex, row.CategoryId);
                continue;
            }

            if (parent is null) continue;
            var child = parent.Children.FirstOrDefault(c => c.SubCategoryId == row.SubCategoryId);
            if (child is null) parent.Children.Add(child = new Node(row.CategoryId, row.SubCategoryId));
            child.Pairs.Add((row.ItemGroup, row.ItemClass));
        }

        _items = value.Items.GroupBy(i => i.Code).ToFrozenDictionary(g => g.Key, g => g.First());
    }

    public int MaxCategoryIndex { get; }

    public int CategoryIndexOf(int group, int itemClass)
    {
        foreach (var parent in _parents)
        {
            if (parent.Children.Any(c => c.Takes(group, itemClass))) return parent.CategoryId;
            if (parent.Takes(group, itemClass)) return parent.CategoryId;
        }

        return MaxCategoryIndex;
    }

    public bool TryGetNode(int categoryId, int subCategoryId, out Func<int, int, bool> takes)
    {
        takes = null;
        var parent = _parents.FirstOrDefault(p => p.CategoryId == categoryId);
        if (parent is null) return false;
        if (subCategoryId == AuctionRules.SubCategoryAll)
        {
            takes = parent.Takes;
            return true;
        }

        var child = parent.Children.FirstOrDefault(c => c.SubCategoryId == subCategoryId);
        if (child is null) return false;
        takes = child.Takes;
        return true;
    }

    public bool TryGetItem(int code, out AuctionItemRow item) => _items.TryGetValue(code, out item);

    private sealed class Node
    {
        public Node(int categoryId, int subCategoryId)
        {
            CategoryId = categoryId;
            SubCategoryId = subCategoryId;
        }

        public int CategoryId { get; }
        public int SubCategoryId { get; }
        public List<(int Group, int Class)> Pairs { get; } = new();
        public List<Node> Children { get; } = new();

        /// <summary><c>IsOverlappedItemCategory</c>: the group, and the class unless the row says any (-1).</summary>
        public bool Takes(int group, int itemClass) =>
            Pairs.Any(p => p.Group == group && (p.Class == -1 || p.Class == itemClass));
    }
}

/// <summary>The official auction constants (<c>GameRule.h</c>) and the pure rules of <c>AuctionManager</c>.</summary>
public static class AuctionRules
{
    public const int CategorySpecial = -1;
    public const int SubCategoryAll = -1;
    public const int SubCategoryEtc = 0;
    public const int SubCategoryNone = -1;

    public const int PerPage = 40;
    public const int MaxBidderList = 100;
    public static readonly TimeSpan KeepDuration = TimeSpan.FromDays(15);

    /// <summary><c>nAuctionSearchRequestMinInterval</c> / <c>nAuctionProcessRequestMinInterval</c>, ar_time ticks.</summary>
    public const uint SearchInterval = 300;
    public const uint ProcessInterval = 100;

    public const byte DurationShort = 1;
    public const byte DurationMid = 2;
    public const byte DurationLong = 3;

    public const byte FlagIsHighestBidder = 1 << 0;
    public const byte FlagNoOtherBidder = 1 << 1;
    public const byte StatusNoBidder = 1;
    public const byte StatusBidCalling = 2;
    public const byte StatusMine = 1;
    public const byte StatusOthers = 2;

    /// <summary><c>onAuctionRegister</c>: long, mid, or short for anything else.</summary>
    public static TimeSpan Duration(byte durationType) => durationType switch
    {
        DurationLong => TimeSpan.FromHours(72),
        DurationMid => TimeSpan.FromHours(24),
        _ => TimeSpan.FromHours(6)
    };

    /// <summary>The registration tax: 3 % (≤ 6 h), 4 % (≤ 24 h), 5 % beyond, of the start price, truncated.</summary>
    public static long RegistrationTax(TimeSpan duration, long startPrice) =>
        duration <= TimeSpan.FromHours(6) ? startPrice * 3 / 100
        : duration <= TimeSpan.FromHours(24) ? startPrice * 4 / 100
        : startPrice * 5 / 100;

    /// <summary><c>fillAuctionMessageBuffer</c>: the duration type the time left falls in.</summary>
    public static byte DurationTypeLeft(TimeSpan left) =>
        left > TimeSpan.FromHours(24) ? DurationLong : left > TimeSpan.FromHours(6) ? DurationMid : DurationShort;

    /// <summary>The seller's share: the price less the 3 % sell tax (truncated tax).</summary>
    public static long SoldPrice(long price) => price - price * 3 / 100;

    /// <summary>
    /// <c>BidForAuction</c>'s floor: at least the current price, and once there is a bidder at least 1.01 × it
    /// (truncated); never above a set instant price.
    /// </summary>
    public static ResultCode CheckBid(long bid, long highest, bool hasBidder, long instantPrice)
    {
        if ((hasBidder && bid < highest * 101 / 100) || bid < highest) return ResultCode.TooCheap;
        if (instantPrice != 0 && instantPrice < bid) return ResultCode.TooMuchMoney;
        return ResultCode.Success;
    }

    public static int TotalPages(int count) => (count - 1) / PerPage + 1;
}
