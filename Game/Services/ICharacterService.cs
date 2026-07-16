using System.Collections.Generic;
using System.Threading.Tasks;
using Navislamia.Game.DataAccess.Entities.Enums;
using Navislamia.Game.DataAccess.Entities.Telecaster;

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

    Task<ItemEntity[]> SwapItemPositionsAsync(string characterName, uint itemHandle1, uint itemHandle2);

    Task<ItemEntity> AddItemAsync(string characterName, int itemResourceId, long count);

    Task SaveProgressAsync(string characterName, int level, int jobLevel, long exp, long jp, long gold, int chaos);

    void SaveChanges();

}
