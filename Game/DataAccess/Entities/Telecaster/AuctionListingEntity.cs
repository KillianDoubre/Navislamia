using System;

namespace Navislamia.Game.DataAccess.Entities.Telecaster;

/// <summary>
/// One running auction (<c>AuctionInfo</c> of the official <c>AuctionManager</c>, socle-encheres-mecanique.md §2).
/// The item is an <c>Items</c> row that belongs to nobody while it is on sale (no character, no account), which keeps
/// it out of every bag and storage query. <see cref="Entity.Id"/> is the wire <c>auction_uid</c>.
/// </summary>
public class AuctionListingEntity : Entity
{
    public long ItemId { get; set; }
    public long SellerId { get; set; }
    public string SellerName { get; set; } = string.Empty;
    public DateTime EndTime { get; set; }
    public long StartPrice { get; set; }
    public long InstantPurchasePrice { get; set; }
    public long RegistrationTax { get; set; }

    /// <summary><c>nHighestBiddingPrice</c>: the start price until the first bid.</summary>
    public long HighestBiddingPrice { get; set; }
    public long HighestBidderId { get; set; }
    public string HighestBidderName { get; set; } = string.Empty;

    /// <summary><c>vBidderUID</c>: the earlier bidders, the highest one excluded, at most 100.</summary>
    public long[] BidderIds { get; set; } = Array.Empty<long>();
}

/// <summary>
/// One entry of the auction storage (<c>ItemKeepingInfo</c>): an item or an amount of gold the owner takes back
/// within 15 days. <see cref="Entity.Id"/> is the wire <c>keeping_uid</c>; a gold entry has no item row.
/// </summary>
public class AuctionKeepingEntity : Entity
{
    public long OwnerId { get; set; }
    public long? ItemId { get; set; }
    public long Gold { get; set; }

    /// <summary>The official <c>KEEPING_TYPE_*</c> (<see cref="Enums.StorageType"/>).</summary>
    public int KeepingType { get; set; }
    public long RelatedAuctionId { get; set; }
    public int RelatedItemCode { get; set; }
    public int RelatedItemEnhance { get; set; }
    public int RelatedItemLevel { get; set; }
    public DateTime ExpireTime { get; set; }
}
