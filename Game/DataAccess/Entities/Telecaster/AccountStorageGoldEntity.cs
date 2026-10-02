namespace Navislamia.Game.DataAccess.Entities.Telecaster;

/// <summary>
/// The gold kept in the counter storage, one row per account. The official server writes it per account
/// (<c>smp_update_storage_gold</c>, called by <c>StructPlayer::ChangeStorageGold</c>), like the stored items;
/// NGemity hid it in an item row of code 0, which this repository does not reproduce
/// (docs/packet-specs/socle-entrepot-or.md §3).
/// </summary>
public class AccountStorageGoldEntity : Entity
{
    public long AccountId { get; set; }

    public long Gold { get; set; }
}
