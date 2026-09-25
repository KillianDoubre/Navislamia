namespace Navislamia.Game.Services;

/// <summary>
/// The unit sell price of one item instance, exactly as NGemity computes it in
/// <c>GameContent::GetItemSellPrice</c> (<c>Chihiro/src/Globals/GameContent.cpp:195-220</c>), called by
/// <c>WorldSession::onSellItem</c> (<c>Chihiro/src/Network/GameNetwork/WorldSession.cpp:1037</c>).
/// See <c>docs/packet-specs/252-sell-item.md</c> §5.3 and §6.
/// <para>
/// The scale is indexed by <c>rank - 1</c> (:198), read with <c>f[rank]</c> for rank 0 only (:205).
/// One increment is added per character level above 1, and the reference truncates the increment to an
/// integer on <b>every</b> turn instead of once at the end: rank 2 with <c>price = 2</c> at level 5 yields
/// 0, because the first increment is 0.80000001 and truncates to 0. That per-turn truncation is a measured
/// property of the reference and is reproduced here; the width of its <c>float_t</c> is not established
/// (fiche §6 écart 7), so the two rules only have to be told apart by the tests named below.
/// </para>
/// </summary>
public static class MarketSellPrice
{
    /// <summary>
    /// The lowest rank the scale covers: rank 0 reads <c>f[0]</c> like rank 1
    /// (<c>GameContent.cpp:205</c>, <c>:208</c>).
    /// </summary>
    public const int MinRank = 0;

    /// <summary>
    /// The highest rank the scale covers. The reference's guard is written backwards —
    /// <c>ASSERT(rank &gt; 8)</c> fires for every legitimate rank (<c>GameContent.cpp:200</c>) — but it
    /// still names 8 as the bound the author meant.
    /// </summary>
    public const int MaxRank = 8;

    /// <summary>
    /// The factor per character level above 1, indexed by <c>rank - 1</c>: 1.35 for rank 0 and rank 1,
    /// then 0.4 (rank 2), 0.2 (rank 3), 0.13 (rank 4) and 0.1 down to rank 8
    /// (<c>GameContent.cpp:198</c>).
    /// </summary>
    private static readonly float[] RankFactors = { 1.35f, 0.4f, 0.2f, 0.13f, 0.1f, 0.1f, 0.1f, 0.1f };

    /// <summary>
    /// First item code whose sale pays its whole price and not a quarter of it
    /// (<c>WorldSession.cpp:1038</c>, <c>602700</c>).
    /// </summary>
    public const int SamePriceForBuyingFirstCode = 602700;

    /// <summary>
    /// Last item code of the same inclusive band (<c>WorldSession.cpp:1038</c>, <c>602799</c>).
    /// </summary>
    public const int SamePriceForBuyingLastCode = 602799;

    /// <summary>
    /// Whether the item code falls in the <c>602700</c>-<c>602799</c> band the reference passes as
    /// <c>same_price_for_buying</c>: such an item is bought back at its full price.
    /// </summary>
    public static bool IsSamePriceForBuying(int itemCode)
    {
        return itemCode >= SamePriceForBuyingFirstCode && itemCode <= SamePriceForBuyingLastCode;
    }

    /// <summary>
    /// The unit sell price of an item of resource <paramref name="price"/> and
    /// <paramref name="rank"/>, worn at character level <paramref name="level"/>.
    /// <para>
    /// Returns <c>false</c> when <paramref name="rank"/> falls outside
    /// <see cref="MinRank"/>..<see cref="MaxRank"/>: the reference indexes <c>f[rank - 1]</c> without a
    /// bound and reads past its array, so no price can be reproduced for such an item. The caller refuses
    /// the sale and logs the item (docs/packet-specs/252-sell-item.md §6 écart 2, A VERIFIER 6).
    /// </para>
    /// </summary>
    public static bool TryComputeUnitPrice(int price, int rank, uint level, bool samePriceForBuying,
        out long unitPrice)
    {
        if (rank < MinRank || rank > MaxRank)
        {
            unitPrice = 0;
            return false;
        }

        var total = (long)price;

        for (uint step = 2; step <= level; step++)
        {
            var factor = RankFactors[rank == 0 ? 0 : rank - 1];
            var increment = rank switch
            {
                0 or 1 => price * factor * 0.1f * 10f,
                2 => price * factor * 0.01f * 100f,
                _ => price * factor * 0.001f * 1000f
            };

            // The cast truncates towards zero, as the reference's int64_t += float does.
            total += (long)increment;
        }

        // (k * 0.25f) at :219: the reference pays a quarter of the accumulated price, a whole one in the
        // 602700-602799 band.
        unitPrice = (long)(total * (samePriceForBuying ? 1.0f : 0.25f));
        return true;
    }
}
