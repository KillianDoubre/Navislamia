namespace Navislamia.Game.DataAccess.Entities.Arcadia;

/// <summary>
/// The auction house category tree, back of the Arcadia table <c>AuctionCateryResource</c>
/// (<c>ArcadiaSchemaPSQL.sql:1-9</c>). The three identifiers are only exposed, never interpreted:
/// what <c>name_id</c> indexes, and what <c>local_flag</c>, <c>item_group</c> and <c>item_class</c>
/// mean, are not established (docs/packet-specs/socle-encheres.md §8.5).
/// </summary>
public class AuctionCateryResourceEntity
{
    public int CateryId { get; set; }
    public int SubCateryId { get; set; }
    public int NameId { get; set; }
    public int LocalFlag { get; set; }
    public int ItemGroup { get; set; }
    public int ItemClass { get; set; }
}
