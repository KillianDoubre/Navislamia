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

    Task<CharacterEntity> CreateCharacterAsync(CharacterEntity character, bool withStarterItems = false);

    bool CharacterExists(string characterName);

    int CharacterCount(int accountId);
    
    CharacterEntity GetCharacterByName(string characterName);

    Task DeleteCharacterByNameAsync(string characterName);

    Task<bool> UpdateClientInfoAsync(string characterName, string clientInfo);

    Task<bool> SaveLearnedSkillAsync(string characterName, int skillId, byte level, long remainingJp);

    Task<ItemEntity> UnequipItemAsync(string characterName, ItemWearType position);

    Task<EquipItemResult> EquipItemAsync(string characterName, uint itemHandle, ItemWearType position);

    Task<ItemEntity[]> ArrangeInventoryAsync(string characterName, IItemSortCatalog catalog);

    Task<ItemEntity> GetItemByHandleAsync(string characterName, uint itemHandle);

    /// <summary>
    /// Binds one of the character's skill cards to the character, the whole judgement inside the
    /// database gate so a packet handled in between cannot invalidate it: the handle is resolved among
    /// the character's items, the target must be the character itself, then the card must be an
    /// inventory skill card, worn by nobody and not bound yet (<see cref="SkillCardBindRules"/>).
    /// <see cref="SkillCardBindOutcome.NotFound"/> for an unknown handle,
    /// <see cref="SkillCardBindOutcome.NotActable"/> when the target is not the character,
    /// <see cref="SkillCardBindOutcome.AccessDenied"/> when the card itself is refused. On success the
    /// bearer reference is written in socket 0 of the row and saved, as NGemity's
    /// <c>Item::SetBindTarget</c> does (Item.cpp:314-332).
    /// </summary>
    Task<SkillCardBindResult> BindSkillCardAsync(string characterName, uint itemHandle, uint targetHandle,
        IItemGroupCatalog itemGroups);

    /// <summary>
    /// Takes <paramref name="count"/> units off one of the character's stacks and returns the amount
    /// left, <c>0</c> when the stack ran out and was deleted, or <c>null</c> when the handle resolves to
    /// none of the character's items.
    /// </summary>
    Task<long?> ConsumeItemAsync(string characterName, uint itemHandle, long count);

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

    Task SaveProgressAsync(string characterName, int level, int jobLevel, long exp, long jp, long gold,
        int chaos, float x, float y);

    void SaveChanges();

}
