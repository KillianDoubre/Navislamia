using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Navislamia.Game.DataAccess.Entities.Telecaster;

namespace Navislamia.Game.DataAccess.Repositories.Interfaces;

/// <summary>
/// One unit of work on the character tables. Obtained from <see cref="ICharacterRepositoryFactory"/> for
/// a single operation and disposed with it: entities it returns are detached afterwards, so they are
/// read, never mutated to be saved later.
/// </summary>
public interface ICharacterRepository : IDisposable
{
    Task<IEnumerable<CharacterEntity>> GetCharactersByAccountNameAsync(string accountName, bool withItems = false);

    /// <summary>
    /// The one character world entry needs, with its items and skills, provided it belongs to the
    /// account. World entry used to load every character of the account with all of their items to keep
    /// one of them.
    /// </summary>
    Task<CharacterEntity> GetAccountCharacterWithItemsAsync(string accountName, string characterName);

    Task<CharacterEntity> CreateCharacterAsync(CharacterEntity character);

    /// <summary>The character row alone, without any collection.</summary>
    Task<CharacterEntity> GetCharacterByNameAsync(string characterName);

    /// <summary>The character with its learned skills, the collection a skill write must see.</summary>
    Task<CharacterEntity> GetCharacterByNameWithSkillsAsync(string characterName);

    Task<CharacterEntity> GetCharacterByNameWithItemsAsync(string characterName);

    Task<bool> CharacterExistsAsync(string characterName);

    Task<int> CharacterCountAsync(int accountId);

    void Delete(CharacterEntity entity);

    void DeleteItem(ItemEntity item);

    /// <summary>
    /// The character's carried quests, ordered by code so one state always yields the same 600 frame.
    /// Read no-tracking: it is projected into a packet, never mutated here.
    /// </summary>
    Task<List<CharacterQuestEntity>> GetQuestsAsync(string characterName);

    /// <summary>One carried quest, tracked, so <see cref="DeleteQuest"/> can remove it.</summary>
    Task<CharacterQuestEntity> GetQuestAsync(string characterName, int code);

    void DeleteQuest(CharacterQuestEntity quest);

    /// <summary>The pet row of a cage item, tracked so a rename can be saved; null when none exists yet.</summary>
    Task<PetEntity> GetPetByItemAsync(long itemId);

    void AddPet(PetEntity pet);

    /// <summary>The summon rows of a character, tracked (docs/packet-specs/socle-apprivoisement-invocation.md §15).</summary>
    Task<List<SummonEntity>> GetSummonsAsync(long characterId);

    void AddSummon(SummonEntity summon);

    /// <summary>The summon rows bound to these card items, whoever owns them, tracked.</summary>
    Task<List<SummonEntity>> GetSummonsOfCardsAsync(IReadOnlyCollection<long> cardItemIds);

    /// <summary>The skills of every summon of a character, tracked.</summary>
    Task<List<SummonSkillEntity>> GetSummonSkillsAsync(long characterId);

    void AddSummonSkill(SummonSkillEntity skill);

    /// <summary>
    /// The skills of the summon bound to a card item, whoever holds the card (a bag, an auction, an offline player),
    /// read no-tracking: null when no item with stock has that id, empty when the card holds no summon.
    /// </summary>
    Task<List<SummonSkillEntity>> GetCardSkillsAsync(long cardItemId);

    /// <summary>
    /// Avoid using SaveChanges directly from context as it applies modifications directly to the database.
    /// Finish all required operations for a step then call this method
    /// </summary>
    Task SaveChangesAsync();
}

public interface ICharacterRepositoryFactory
{
    /// <summary>A new unit of work with its own context. The caller disposes it.</summary>
    ICharacterRepository Create();
}
