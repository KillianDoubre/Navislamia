using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Navislamia.Game.DataAccess.Contexts;
using Navislamia.Game.DataAccess.Entities.Telecaster;
using Navislamia.Game.DataAccess.Repositories.Interfaces;
using Navislamia.Game.Services;

namespace Navislamia.Game.DataAccess.Repositories;

/// <summary>
/// Reads and moves the storage rows of an account. Both the storage list and the inventory list are the
/// item table, so one context owns the whole move: the destination slot has to be counted on the very
/// rows the moved stack is about to leave or join.
/// <para>
/// That context lives for one call only. It used to live as long as the server, next to the character
/// service's own long-lived context: each kept whatever it had loaded, so an item the other one had moved
/// or deleted could be read stale here, and its change tracker grew with every storage ever opened.
/// </para>
/// </summary>
public class StorageRepository : IStorageRepository
{
    private readonly DbContextOptions<TelecasterContext> _options;

    public StorageRepository(DbContextOptions<TelecasterContext> options)
    {
        _options = options;
    }

    public async Task<ItemEntity[]> GetStorageItemsAsync(string characterName)
    {
        await using var context = new TelecasterContext(_options);
        var character = await context.Characters.AsNoTracking()
            .FirstOrDefaultAsync(c => c.CharacterName == characterName);
        if (character is null)
        {
            return Array.Empty<ItemEntity>();
        }

        return await StorageRows(context, AccountIdOf(character)).AsNoTracking().OrderBy(item => item.Idx)
            .ToArrayAsync();
    }

    public async Task<StorageMoveResult> MoveAsync(string characterName, uint itemHandle, bool toStorage, long count)
    {
        await using var context = new TelecasterContext(_options);
        var character = await context.Characters.FirstOrDefaultAsync(c => c.CharacterName == characterName);
        if (character is null)
        {
            return StorageMoveResult.Refused(StorageMoveOutcome.UnknownCharacter);
        }

        var item = await context.Items.FirstOrDefaultAsync(row => row.Id == itemHandle);
        if (item is null)
        {
            return StorageMoveResult.Refused(StorageMoveOutcome.UnknownHandle);
        }

        var accountId = AccountIdOf(character);
        var inInventory = StorageRules.IsInventoryRow(item, character.Id);
        var inStorage = StorageRules.IsStorageRow(item, accountId);
        if (!inInventory && !inStorage)
        {
            return StorageMoveResult.Refused(StorageMoveOutcome.AccessDenied);
        }

        // NGemity resolves the handle globally and only acts when the item sits on the side the mode
        // names (WorldSession.cpp:1628-1637) — an item already in its destination list is left alone.
        if (toStorage ? !inInventory : !inStorage)
        {
            return StorageMoveResult.Refused(StorageMoveOutcome.Ignored);
        }

        // A worn item stays in the inventory, silently, the way NGemity's IsErasable (Player.cpp:3142)
        // makes MoveInventoryToStorage return false into the call site's "do nothing" branch.
        if (toStorage && !StorageRules.IsStorable(item))
        {
            return StorageMoveResult.Refused(StorageMoveOutcome.Ignored);
        }

        var moved = StorageRules.MoveCount(count, item.Amount);
        if (moved <= 0)
        {
            return StorageMoveResult.Refused(StorageMoveOutcome.Ignored);
        }

        var slot = StorageRules.NextFreeIndex(await DestinationIndicesAsync(context, character, accountId, toStorage));

        if (moved == item.Amount)
        {
            StorageRules.Own(item, toStorage, character.Id, accountId);
            item.Idx = slot;
            await context.SaveChangesAsync();
            return new StorageMoveResult(StorageMoveOutcome.Moved, item, null, 0);
        }

        var destination = StorageRules.Divide(item, moved, slot);
        StorageRules.Own(destination, toStorage, character.Id, accountId);
        item.Amount -= moved;
        context.Items.Add(destination);
        await context.SaveChangesAsync();
        return new StorageMoveResult(StorageMoveOutcome.Split, destination, item, item.Amount);
    }

    /// <summary>
    /// The account id of a character, on the item table's own type: <c>CharacterEntity.AccountId</c> is a
    /// <c>long</c> and <c>ItemEntity.AccountId</c> an <c>int</c>, the type <c>ConnectionInfo.AccountId</c>
    /// carries everywhere else.
    /// </summary>
    private static int AccountIdOf(CharacterEntity character) => Convert.ToInt32(character.AccountId);

    /// <summary>
    /// The storage side of the item table, with the four conditions both references write
    /// (Chihiro/src/Database/Implementation/CharacterDatabase.cpp:93-97 ;
    /// rzu rzgame/src/Database/DB_StorageItem.cpp:7). <c>ItemStorageEntity</c> is the auction house
    /// storage, hence the <c>AuctionId</c>/<c>StorageId</c> conditions: an auction row must not show up
    /// in the counter storage.
    /// </summary>
    private static IQueryable<ItemEntity> StorageRows(TelecasterContext context, int accountId)
        => context.Items.Where(row => row.AccountId == accountId
                                       && row.CharacterId == null
                                       && row.AuctionId == null
                                       && row.StorageId == null);

    private static async Task<int[]> DestinationIndicesAsync(TelecasterContext context, CharacterEntity character,
        int accountId, bool toStorage)
    {
        var indices = toStorage
            ? StorageRows(context, accountId).Select(row => row.Idx)
            : context.Items.Where(row => row.CharacterId == character.Id).Select(row => row.Idx);

        return await indices.ToArrayAsync();
    }
}
