using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Navislamia.Game.DataAccess.Entities.Enums;
using Navislamia.Game.DataAccess.Entities.Telecaster;
using Navislamia.Game.DataAccess.Repositories.Interfaces;

using Serilog;

namespace Navislamia.Game.Services;

public class CharacterService : ICharacterService
{
    private readonly ILogger<CharacterService> _logger;
    private readonly ICharacterRepository _characterRepository;
    private readonly IStarterItemsRepository _starterItemsRepository;

    public CharacterService(IStarterItemsRepository starterItemsRepository, ICharacterRepository characterRepository, ILogger<CharacterService> logger)
    {
        _starterItemsRepository = starterItemsRepository;
        _characterRepository = characterRepository;
        _logger = logger;
    }

    public async Task<IEnumerable<CharacterEntity>> GetCharactersByAccountNameAsync(string accountName, bool withItems = false)
    {
        var characters = (await _characterRepository.GetCharactersByAccountNameAsync(accountName, withItems)).ToList();
        var changed = false;
        foreach (var character in characters)
        {
            changed |= CharacterDefaults.Apply(character);
        }

        if (changed)
        {
            await _characterRepository.SaveChangesAsync();
        }

        return characters;
    }

    public async Task<CharacterEntity> CreateCharacterAsync(CharacterEntity character, bool withStarterItems = false)
    {
        CharacterDefaults.Apply(character);

        if (withStarterItems)
        {
            character.Items ??= new List<ItemEntity>();
            
            var starterItems = await _starterItemsRepository.GetStarterItemsByJobAsync((Race)character.Race);
            foreach (var starterItem in starterItems)
            {
                character.Items.Add(new ItemEntity
                {
                    ItemResourceId = starterItem.ItemId,
                    Level = starterItem.Level,
                    Enhance = starterItem.Enhancement,
                    Amount = starterItem.Amount,
                    RemainingTime = starterItem.ValidForSeconds
                });
            }
        }
        
        var result = await _characterRepository.CreateCharacterAsync(character);
        await _characterRepository.SaveChangesAsync();
        
        return result;
    }

    public bool CharacterExists(string characterName)
    {
        return _characterRepository.CharacterExists(characterName);
    }

    public int CharacterCount(int accountId)
    {
        return _characterRepository.CharacterCount(accountId);
    }

    public CharacterEntity GetCharacterByName(string characterName)
    {
        return _characterRepository.GetCharacterByName(characterName);
    }

    public async Task DeleteCharacterByNameAsync(string characterName)
    {
        var entity = _characterRepository.GetCharacterByName(characterName);
        if (entity is null)
        {
            _logger.LogWarning("Character Delete Failed! Character {name} not found!", characterName);
            return;
        }

        _characterRepository.Delete(entity);
    }

    public async Task<bool> UpdateClientInfoAsync(string characterName, string clientInfo)
    {
        var character = _characterRepository.GetCharacterByName(characterName);
        if (character is null)
        {
            return false;
        }

        character.ClientInfo = clientInfo;
        await _characterRepository.SaveChangesAsync();
        return true;
    }

    public async Task<bool> SaveLearnedSkillAsync(string characterName, int skillId, byte level, long remainingJp)
    {
        var character = _characterRepository.GetCharacterByName(characterName);
        if (character is null)
        {
            return false;
        }

        character.Skills ??= new List<CharacterSkillEntity>();
        var skill = character.Skills.FirstOrDefault(entry => entry.SkillId == skillId);
        if (skill is null)
        {
            character.Skills.Add(new CharacterSkillEntity
            {
                SkillId = skillId,
                Level = level
            });
        }
        else
        {
            skill.Level = level;
        }

        character.Jp = remainingJp;
        await _characterRepository.SaveChangesAsync();
        return true;
    }

    public async Task<ItemEntity> UnequipItemAsync(string characterName, ItemWearType position)
    {
        var character = _characterRepository.GetCharacterByNameWithItems(characterName);
        var item = character?.Items?.FirstOrDefault(entry => entry.WearInfo == position);
        if (item is null)
        {
            return null;
        }

        item.WearInfo = ItemWearType.None;
        await _characterRepository.SaveChangesAsync();
        return item;
    }

    public async Task<EquipItemResult> EquipItemAsync(string characterName, uint itemHandle, ItemWearType position)
    {
        var character = _characterRepository.GetCharacterByNameWithItems(characterName);
        var item = character?.Items?.FirstOrDefault(entry => (uint)entry.Id == itemHandle);
        if (item is null)
        {
            return new EquipItemResult(EquipItemOutcome.NotFound, null, null, null);
        }

        if (item.WearInfo != ItemWearType.None)
        {
            return new EquipItemResult(EquipItemOutcome.AlreadyWorn, character, null, null);
        }

        var displaced = character.Items.FirstOrDefault(entry => entry.WearInfo == position);
        if (displaced is not null)
        {
            displaced.WearInfo = ItemWearType.None;
        }

        item.WearInfo = position;
        await _characterRepository.SaveChangesAsync();
        return new EquipItemResult(EquipItemOutcome.Success, character, item, displaced);
    }

    public async Task<ItemEntity[]> ArrangeInventoryAsync(string characterName, IItemSortCatalog catalog)
    {
        var character = _characterRepository.GetCharacterByNameWithItems(characterName);
        if (character is null)
        {
            return null;
        }

        var all = character.Items?.ToArray() ?? Array.Empty<ItemEntity>();
        var bag = all.Where(item => item.WearInfo == ItemWearType.None).ToArray();
        var keys = new ItemOrderKey[bag.Length];
        for (var i = 0; i < bag.Length; i++)
        {
            keys[i] = new ItemOrderKey(catalog.GetResourceKey(bag[i].ItemResourceId), bag[i].Id);
        }

        if (InventoryArrange.Apply(bag, keys))
        {
            await _characterRepository.SaveChangesAsync();
        }

        return all.Where(item => item.WearInfo != ItemWearType.None).Concat(bag).ToArray();
    }

    public async Task<ItemEntity[]> SwapItemPositionsAsync(string characterName, uint itemHandle1, uint itemHandle2)
    {
        var character = _characterRepository.GetCharacterByNameWithItems(characterName);
        if (character?.Items is null)
        {
            return null;
        }

        var bag = character.Items.Where(item => item.WearInfo == ItemWearType.None).ToArray();
        var first = bag.FirstOrDefault(item => (uint)item.Id == itemHandle1);
        var second = bag.FirstOrDefault(item => (uint)item.Id == itemHandle2);
        if (first is null || second is null || ReferenceEquals(first, second))
        {
            return null;
        }

        InventoryArrange.EnsureContiguousIndices(bag);
        (first.Idx, second.Idx) = (second.Idx, first.Idx);
        await _characterRepository.SaveChangesAsync();

        return bag;
    }

    public async Task SaveProgressAsync(string characterName, int level, int jobLevel, long exp, long jp, long gold, int chaos)
    {
        if (string.IsNullOrEmpty(characterName))
        {
            return;
        }

        var character = _characterRepository.GetCharacterByName(characterName);
        if (character is null)
        {
            return;
        }

        if (level > 0)
        {
            character.Lv = level;
            character.MaxReachedLv = Math.Max(character.MaxReachedLv, level);
        }

        if (jobLevel > 0)
        {
            character.Jlv = jobLevel;
        }

        character.Exp = exp;
        character.Jp = jp;
        character.Gold = gold;
        character.Chaos = chaos;
        await _characterRepository.SaveChangesAsync();
    }

    public async void SaveChanges()
    {
        await _characterRepository.SaveChangesAsync();
    }
}
