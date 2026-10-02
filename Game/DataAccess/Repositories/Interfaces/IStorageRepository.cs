using System.Threading.Tasks;
using Navislamia.Game.DataAccess.Entities.Telecaster;
using Navislamia.Game.Services;

namespace Navislamia.Game.DataAccess.Repositories.Interfaces;

/// <summary>
/// The account-scoped storage, read and written on the same item table as the inventory
/// (docs/packet-specs/211-212-storage.md §5.1, §5.3 point 4). The character name is the entry point
/// rather than an account id: the account that owns a storage is the one of the character in session,
/// read from the row itself instead of from the session state.
/// </summary>
public interface IStorageRepository
{
    Task<ItemEntity[]> GetStorageItemsAsync(string characterName);

    Task<StorageMoveResult> MoveAsync(string characterName, uint itemHandle, bool toStorage, long count);

    /// <summary>The gold kept in the storage of the character's account, 0 when nothing was ever stored.</summary>
    Task<long> GetStorageGoldAsync(string characterName);

    /// <summary>
    /// Writes the two balances of one gold move in a single save: the character's carried gold and its
    /// account's stored gold, so a crash cannot keep one side of the move without the other.
    /// </summary>
    Task SaveGoldAsync(string characterName, long carried, long stored);
}
