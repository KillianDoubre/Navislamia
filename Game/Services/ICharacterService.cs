using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Navislamia.Game.DataAccess.Entities.Enums;
using Navislamia.Game.DataAccess.Entities.Telecaster;
using Navislamia.Game.Network.Packets.Game;

namespace Navislamia.Game.Services;

public interface ICharacterService
{
    Task<IEnumerable<CharacterEntity>> GetCharactersByAccountNameAsync(string accountName, bool withItems = false);

    /// <summary>
    /// The character entering the world, with its items and skills, or <c>null</c> when it does not
    /// belong to the account.
    /// </summary>
    Task<CharacterEntity> GetCharacterForWorldEntryAsync(string accountName, string characterName);

    Task<CharacterEntity> CreateCharacterAsync(CharacterEntity character, bool withStarterItems = false);

    Task<bool> CharacterExistsAsync(string characterName);

    Task<int> CharacterCountAsync(int accountId);

    Task<CharacterEntity> GetCharacterByNameAsync(string characterName);

    Task DeleteCharacterByNameAsync(string characterName);

    Task<bool> UpdateClientInfoAsync(string characterName, string clientInfo);

    Task<bool> SaveLearnedSkillAsync(string characterName, int skillId, byte level, long remainingJp);

    /// <summary>
    /// The character's carried quests, ordered by code: the state <c>TM_SC_QUEST_LIST</c> (600) exposes
    /// and <c>TM_CS_DROP_QUEST</c> (603) erases.
    /// </summary>
    Task<CharacterQuestEntity[]> GetQuestsAsync(string characterName);

    /// <summary>
    /// Erases the quest <paramref name="code"/> from the character's state and reports whether a row was
    /// removed — the equivalent of NGemity's <c>CHARACTER_DEL_QUEST</c>. The character's own list is the
    /// only condition.
    /// </summary>
    Task<bool> DropQuestAsync(string characterName, int code);

    Task<ItemEntity> UnequipItemAsync(string characterName, ItemWearType position);

    Task<EquipItemResult> EquipItemAsync(string characterName, uint itemHandle, ItemWearType position);

    Task<ItemEntity[]> ArrangeInventoryAsync(string characterName, IItemSortCatalog catalog);

    Task<ItemEntity> GetItemByHandleAsync(string characterName, uint itemHandle);

    /// <summary>
    /// Takes <paramref name="count"/> units off one of the character's stacks and returns the amount
    /// left, <c>0</c> when the stack ran out and was deleted, or <c>null</c> when the handle resolves to
    /// none of the character's items.
    /// </summary>
    Task<long?> ConsumeItemAsync(string characterName, uint itemHandle, long count);

    /// <summary>
    /// Takes one unit off the first carried item, in bag order, that <paramref name="match"/> accepts, and
    /// reports it with the amount left (<c>0</c> when the stack ran out and was deleted). <c>null</c> when
    /// no carried item matches. Worn items are never considered. The search and the removal share one
    /// gated operation, so two requests cannot both take the last unit.
    /// </summary>
    Task<(ItemEntity Item, long Remaining)?> ConsumeFirstAsync(string characterName, Func<ItemEntity, bool> match);

    /// <summary>
    /// Resolves one of the character's items and removes the units <paramref name="resolveCount"/>
    /// returns for it, both inside the database gate: a rule judged by <paramref name="resolveCount"/>
    /// cannot be invalidated by a packet handled in between (an equip racing a drop). A count of zero or
    /// less refuses. <see cref="ItemRemoval.Item"/> is <c>null</c> for an unknown handle.
    /// </summary>
    Task<ItemRemoval> RemoveItemAsync(string characterName, uint itemHandle, Func<ItemEntity, long> resolveCount);

    Task<ItemEntity[]> SwapItemPositionsAsync(string characterName, uint itemHandle1, uint itemHandle2);

    Task<ItemEntity> AddItemAsync(string characterName, int itemResourceId, long count);

    Task<IReadOnlyList<(uint Handle, long Count)>> EraseItemsAsync(string characterName,
        IReadOnlyList<GameActionPackets.EraseItemRequest> requests);

    /// <summary>
    /// Persists the progress a session accumulated. <paramref name="pkMode"/> is the PK mode of the
    /// session, written back to the pre-existing <c>Characters.PkMode</c> column: it is the only
    /// piece of that state that no other writer touches.
    /// </summary>
    Task SaveProgressAsync(string characterName, int level, int jobLevel, long exp, long jp, long gold,
        int chaos, float x, float y, bool pkMode);

}
