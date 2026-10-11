namespace Navislamia.Game.DataAccess.Entities.Telecaster;

/// <summary>
/// The item shop points of an account, one row per account. The official shop kept its money outside the game
/// databases (the web shop's own billing, <c>TS_AG_ITEM_PURCHASED</c> from the auth server): this table is the
/// Navislamia stand-in, credited by <c>/addcash</c> and spent by the item shop page
/// (docs/packet-specs/10000-open-item-shop.md, *Boutique livrée*).
/// </summary>
public class AccountCashPointEntity : Entity
{
    public long AccountId { get; set; }

    public long Points { get; set; }
}
