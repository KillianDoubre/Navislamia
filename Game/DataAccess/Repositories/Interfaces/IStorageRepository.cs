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
}
