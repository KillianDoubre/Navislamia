using System.Collections.Generic;
using System.Threading.Tasks;
using Navislamia.Game.DataAccess.Entities.Telecaster;

namespace Navislamia.Game.DataAccess.Repositories.Interfaces;

public interface ICharacterRepository
{
    Task<IEnumerable<CharacterEntity>> GetCharactersByAccountNameAsync(string accountName, bool withItems = false);

    Task<CharacterEntity> CreateCharacterAsync(CharacterEntity character);
 
    CharacterEntity GetCharacterByName(string characterName);

    CharacterEntity GetCharacterByNameWithItems(string characterName);

    bool CharacterExists(string characterName);

    int CharacterCount(int accountId);
    
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

    /// <summary>
    /// Avoid using SaveChanges directly from context as it applies modifications directly to the database.
    /// Finish all required operations for a step then call this method
    /// </summary>
    Task SaveChangesAsync();
}