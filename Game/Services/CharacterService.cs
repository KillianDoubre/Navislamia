using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Navislamia.Game.DataAccess.Entities.Enums;
using Navislamia.Game.DataAccess.Entities.Telecaster;
using Navislamia.Game.DataAccess.Repositories.Interfaces;
using Navislamia.Game.Network.Packets.Game;

using Serilog;

namespace Navislamia.Game.Services;

public class CharacterService : ICharacterService
{
    private readonly ILogger<CharacterService> _logger;
    private readonly ICharacterRepository _characterRepository;
    private readonly IStarterItemsRepository _starterItemsRepository;
    private readonly SemaphoreSlim _databaseGate = new(1, 1);

    public CharacterService(IStarterItemsRepository starterItemsRepository, ICharacterRepository characterRepository, ILogger<CharacterService> logger)
    {
        _starterItemsRepository = starterItemsRepository;
        _characterRepository = characterRepository;
        _logger = logger;
    }

    public Task<IEnumerable<CharacterEntity>> GetCharactersByAccountNameAsync(string accountName, bool withItems = false)
    {
        return RunExclusiveAsync<IEnumerable<CharacterEntity>>(async () =>
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
        });
    }

    public Task<CharacterEntity> CreateCharacterAsync(CharacterEntity character, bool withStarterItems = false)
    {
        return RunExclusiveAsync(async () =>
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
        });
    }

    public bool CharacterExists(string characterName)
    {
        return RunExclusive(() => _characterRepository.CharacterExists(characterName));
    }

    public int CharacterCount(int accountId)
    {
        return RunExclusive(() => _characterRepository.CharacterCount(accountId));
    }

    public CharacterEntity GetCharacterByName(string characterName)
    {
        return RunExclusive(() => _characterRepository.GetCharacterByName(characterName));
    }

    public Task DeleteCharacterByNameAsync(string characterName)
    {
        return RunExclusiveAsync(() =>
        {
            var entity = _characterRepository.GetCharacterByName(characterName);
            if (entity is null)
            {
                _logger.LogWarning("Character Delete Failed! Character {name} not found!", characterName);
                return Task.CompletedTask;
            }

            _characterRepository.Delete(entity);
            return Task.CompletedTask;
        });
    }

    public Task<bool> UpdateClientInfoAsync(string characterName, string clientInfo)
    {
        return RunExclusiveAsync(async () =>
        {
            var character = _characterRepository.GetCharacterByName(characterName);
            if (character is null)
            {
                return false;
            }

            character.ClientInfo = clientInfo;
            await _characterRepository.SaveChangesAsync();
            return true;
        });
    }

    public Task<bool> SaveLearnedSkillAsync(string characterName, int skillId, byte level, long remainingJp)
    {
        return RunExclusiveAsync(async () =>
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
        });
    }

    public Task<ItemEntity> UnequipItemAsync(string characterName, ItemWearType position)
    {
        return RunExclusiveAsync(async () =>
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
        });
    }

    public Task<EquipItemResult> EquipItemAsync(string characterName, uint itemHandle, ItemWearType position)
    {
        return RunExclusiveAsync(async () =>
        {
            var character = _characterRepository.GetCharacterByNameWithItems(characterName);
            var item = FindByHandle(character?.Items, itemHandle);
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
        });
    }

    public Task<ItemEntity[]> ArrangeInventoryAsync(string characterName, IItemSortCatalog catalog)
    {
        return RunExclusiveAsync(async () =>
        {
            var character = _characterRepository.GetCharacterByNameWithItems(characterName);
            if (character is null)
            {
                return null;
            }

            var items = character.Items?.ToArray() ?? Array.Empty<ItemEntity>();
            var keys = new ItemOrderKey[items.Length];
            for (var i = 0; i < items.Length; i++)
            {
                keys[i] = new ItemOrderKey(catalog.GetResourceKey(items[i].ItemResourceId), items[i].Id);
            }

            if (InventoryArrange.Apply(items, keys))
            {
                await _characterRepository.SaveChangesAsync();
            }

            return items;
        });
    }

    public Task<IReadOnlyList<(uint Handle, long Count)>> EraseItemsAsync(string characterName,
        IReadOnlyList<GameActionPackets.EraseItemRequest> requests)
    {
        return RunExclusiveAsync<IReadOnlyList<(uint Handle, long Count)>>(async () =>
        {
            var erased = new List<(uint Handle, long Count)>(requests.Count);
            var character = _characterRepository.GetCharacterByNameWithItems(characterName);
            if (character?.Items is null)
            {
                return erased;
            }

            foreach (var request in requests)
            {
                var item = FindByHandle(character.Items, request.ItemHandle);
                if (item is null || request.Count <= 0)
                {
                    continue;
                }

                var removed = Math.Min(request.Count, item.Amount);
                if (removed >= item.Amount)
                {
                    character.Items.Remove(item);
                    _characterRepository.DeleteItem(item);
                }
                else
                {
                    item.Amount -= removed;
                }

                erased.Add((request.ItemHandle, removed));
            }

            if (erased.Count == 0)
            {
                return erased;
            }

            InventoryArrange.EnsureContiguousIndices(character.Items.ToArray());
            await _characterRepository.SaveChangesAsync();
            return erased;
        });
    }

    public Task<ItemEntity> AddItemAsync(string characterName, int itemResourceId, long count)
    {
        return RunExclusiveAsync(async () =>
        {
            var character = _characterRepository.GetCharacterByNameWithItems(characterName);
            if (character is null)
            {
                return null;
            }

            character.Items ??= new List<ItemEntity>();
            var nextIndex = character.Items.Count == 0
                ? InventoryArrange.FirstIndex
                : character.Items.Max(item => item.Idx) + 1;
            var added = new ItemEntity
            {
                ItemResourceId = itemResourceId,
                Amount = Math.Max(1, count),
                WearInfo = ItemWearType.None,
                Idx = nextIndex
            };

            character.Items.Add(added);
            await _characterRepository.SaveChangesAsync();
            return added;
        });
    }

    public Task<ItemEntity[]> SwapItemPositionsAsync(string characterName, uint itemHandle1, uint itemHandle2)
    {
        return RunExclusiveAsync(async () =>
        {
            var character = _characterRepository.GetCharacterByNameWithItems(characterName);
            if (character?.Items is null)
            {
                return null;
            }

            var items = character.Items.ToArray();
            var first = FindByHandle(items, itemHandle1);
            var second = FindByHandle(items, itemHandle2);
            if (first is null || second is null || ReferenceEquals(first, second))
            {
                return null;
            }

            InventoryArrange.EnsureContiguousIndices(items);
            (first.Idx, second.Idx) = (second.Idx, first.Idx);
            await _characterRepository.SaveChangesAsync();

            return items;
        });
    }

    public Task SaveProgressAsync(string characterName, int level, int jobLevel, long exp, long jp, long gold, int chaos)
    {
        if (string.IsNullOrEmpty(characterName))
        {
            return Task.CompletedTask;
        }

        return RunExclusiveAsync(async () =>
        {
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
        });
    }

    public async void SaveChanges()
    {
        await RunExclusiveAsync(() => _characterRepository.SaveChangesAsync());
    }

    private static ItemEntity FindByHandle(IEnumerable<ItemEntity> items, uint handle)
    {
        return items?.FirstOrDefault(item => (uint)item.Id == handle);
    }

    private async Task<T> RunExclusiveAsync<T>(Func<Task<T>> operation)
    {
        await _databaseGate.WaitAsync();
        try
        {
            return await operation();
        }
        finally
        {
            _databaseGate.Release();
        }
    }

    private async Task RunExclusiveAsync(Func<Task> operation)
    {
        await _databaseGate.WaitAsync();
        try
        {
            await operation();
        }
        finally
        {
            _databaseGate.Release();
        }
    }

    private T RunExclusive<T>(Func<T> operation)
    {
        _databaseGate.Wait();
        try
        {
            return operation();
        }
        finally
        {
            _databaseGate.Release();
        }
    }
}
