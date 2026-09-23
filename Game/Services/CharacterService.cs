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
    private readonly ICharacterRepositoryFactory _repositories;
    private readonly IStarterItemsRepository _starterItemsRepository;
    private readonly CharacterGate _gate;

    /// <summary>
    /// Each operation gets its own repository, hence its own context, and runs under the gate of the
    /// character it touches (<see cref="CharacterGate"/>): two players no longer wait on each other.
    /// </summary>
    public CharacterService(IStarterItemsRepository starterItemsRepository, ICharacterRepositoryFactory repositories,
        CharacterGate gate, ILogger<CharacterService> logger)
    {
        _starterItemsRepository = starterItemsRepository;
        _repositories = repositories;
        _gate = gate;
        _logger = logger;
    }

    public Task<IEnumerable<CharacterEntity>> GetCharactersByAccountNameAsync(string accountName, bool withItems = false)
    {
        return RunExclusiveAsync<IEnumerable<CharacterEntity>>(accountName, async repository =>
        {
            var characters = (await repository.GetCharactersByAccountNameAsync(accountName, withItems)).ToList();
            var changed = false;
            foreach (var character in characters)
            {
                changed |= CharacterDefaults.Apply(character);
            }

            if (changed)
            {
                await repository.SaveChangesAsync();
            }

            return characters;
        });
    }

    public Task<CharacterEntity> GetCharacterForWorldEntryAsync(string accountName, string characterName)
    {
        return RunExclusiveAsync(characterName, async repository =>
        {
            var character = await repository.GetAccountCharacterWithItemsAsync(accountName, characterName);
            if (character is not null && CharacterDefaults.Apply(character))
            {
                await repository.SaveChangesAsync();
            }

            return character;
        });
    }

    public Task<CharacterEntity> CreateCharacterAsync(CharacterEntity character, bool withStarterItems = false)
    {
        return RunExclusiveAsync(character.CharacterName, async repository =>
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

            var result = await repository.CreateCharacterAsync(character);
            await repository.SaveChangesAsync();

            return result;
        });
    }

    public Task<bool> CharacterExistsAsync(string characterName)
    {
        return ReadAsync(repository => repository.CharacterExistsAsync(characterName));
    }

    public Task<int> CharacterCountAsync(int accountId)
    {
        return ReadAsync(repository => repository.CharacterCountAsync(accountId));
    }

    public Task<CharacterEntity> GetCharacterByNameAsync(string characterName)
    {
        return ReadAsync(repository => repository.GetCharacterByNameAsync(characterName));
    }

    /// <summary>
    /// Deletes and persists in one step. The delete used to be staged on the shared context and saved by
    /// a separate, unawaited <c>SaveChanges</c> call racing it; a context per operation has nothing left
    /// to save later.
    /// </summary>
    public Task DeleteCharacterByNameAsync(string characterName)
    {
        return RunExclusiveAsync(characterName, async repository =>
        {
            var entity = await repository.GetCharacterByNameAsync(characterName);
            if (entity is null)
            {
                _logger.LogWarning("Character Delete Failed! Character {name} not found!", characterName);
                return;
            }

            repository.Delete(entity);
            await repository.SaveChangesAsync();
        });
    }

    public Task<bool> UpdateClientInfoAsync(string characterName, string clientInfo)
    {
        return RunExclusiveAsync(characterName, async repository =>
        {
            var character = await repository.GetCharacterByNameAsync(characterName);
            if (character is null)
            {
                return false;
            }

            character.ClientInfo = clientInfo;
            await repository.SaveChangesAsync();
            return true;
        });
    }

    public Task<bool> SaveLearnedSkillAsync(string characterName, int skillId, byte level, long remainingJp)
    {
        return RunExclusiveAsync(characterName, async repository =>
        {
            // The skills must be loaded here: with the shared context they came from the login's identity
            // map, and without them an already learned skill would be inserted a second time.
            var character = await repository.GetCharacterByNameWithSkillsAsync(characterName);
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
            await repository.SaveChangesAsync();
            return true;
        });
    }

    public Task<CharacterQuestEntity[]> GetQuestsAsync(string characterName)
    {
        return ReadAsync(async repository => (await repository.GetQuestsAsync(characterName)).ToArray());
    }

    public Task<bool> DropQuestAsync(string characterName, int code)
    {
        return RunExclusiveAsync(characterName, async repository =>
        {
            // The only eligibility condition is NGemity's own: the quest is in the character's list
            // (Player::DropQuest, Chihiro/src/Entities/Player/Player.cpp:3173-3187). No flag, no
            // cool-down and no quest-type exclusion is invented here (fiche §8.1).
            var quest = await repository.GetQuestAsync(characterName, code);
            if (quest is null)
            {
                return false;
            }

            repository.DeleteQuest(quest);
            await repository.SaveChangesAsync();
            return true;
        });
    }

    public Task<ItemEntity> UnequipItemAsync(string characterName, ItemWearType position)
    {
        return RunExclusiveAsync(characterName, async repository =>
        {
            var character = await repository.GetCharacterByNameWithItemsAsync(characterName);
            var item = character?.Items?.FirstOrDefault(entry => entry.WearInfo == position);
            if (item is null)
            {
                return null;
            }

            item.WearInfo = ItemWearType.None;
            await repository.SaveChangesAsync();
            return item;
        });
    }

    public Task<EquipItemResult> EquipItemAsync(string characterName, uint itemHandle, ItemWearType position)
    {
        return RunExclusiveAsync(characterName, async repository =>
        {
            var character = await repository.GetCharacterByNameWithItemsAsync(characterName);
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
            await repository.SaveChangesAsync();
            return new EquipItemResult(EquipItemOutcome.Success, character, item, displaced);
        });
    }

    public Task<ItemEntity> GetItemByHandleAsync(string characterName, uint itemHandle)
    {
        return RunExclusiveAsync(characterName, async repository => FindByHandle(
            (await repository.GetCharacterByNameWithItemsAsync(characterName))?.Items, itemHandle));
    }

    public Task<ItemEntity[]> ArrangeInventoryAsync(string characterName, IItemSortCatalog catalog)
    {
        return RunExclusiveAsync(characterName, async repository =>
        {
            var character = await repository.GetCharacterByNameWithItemsAsync(characterName);
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
                await repository.SaveChangesAsync();
            }

            return items;
        });
    }

    public Task<IReadOnlyList<(uint Handle, long Count)>> EraseItemsAsync(string characterName,
        IReadOnlyList<GameActionPackets.EraseItemRequest> requests)
    {
        return RunExclusiveAsync<IReadOnlyList<(uint Handle, long Count)>>(characterName, async repository =>
        {
            var erased = new List<(uint Handle, long Count)>(requests.Count);
            var character = await repository.GetCharacterByNameWithItemsAsync(characterName);
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

                erased.Add((request.ItemHandle, RemoveAmount(repository, character, item, request.Count)));
            }

            if (erased.Count == 0)
            {
                return erased;
            }

            InventoryArrange.EnsureContiguousIndices(character.Items.ToArray());
            await repository.SaveChangesAsync();
            return erased;
        });
    }

    public Task<long?> ConsumeItemAsync(string characterName, uint itemHandle, long count)
    {
        return RunExclusiveAsync<long?>(characterName, async repository =>
        {
            var character = await repository.GetCharacterByNameWithItemsAsync(characterName);
            var item = FindByHandle(character?.Items, itemHandle);
            if (item is null || count <= 0)
            {
                return null;
            }

            RemoveAmount(repository, character, item, count);
            InventoryArrange.EnsureContiguousIndices(character.Items.ToArray());
            await repository.SaveChangesAsync();
            return character.Items.Contains(item) ? item.Amount : 0;
        });
    }

    public Task<(ItemEntity Item, long Remaining)?> ConsumeFirstAsync(string characterName,
        Func<ItemEntity, bool> match)
    {
        return RunExclusiveAsync<(ItemEntity Item, long Remaining)?>(characterName, async repository =>
        {
            var character = await repository.GetCharacterByNameWithItemsAsync(characterName);
            var item = character?.Items?
                .Where(entry => entry.WearInfo == ItemWearType.None && entry.Amount > 0 && match(entry))
                .OrderBy(entry => entry.Idx)
                .FirstOrDefault();
            if (item is null)
            {
                return null;
            }

            RemoveAmount(repository, character, item, 1);
            InventoryArrange.EnsureContiguousIndices(character.Items.ToArray());
            await repository.SaveChangesAsync();
            return (item, character.Items.Contains(item) ? item.Amount : 0);
        });
    }

    public Task<ItemRemoval> RemoveItemAsync(string characterName, uint itemHandle,
        Func<ItemEntity, long> resolveCount)
    {
        return RunExclusiveAsync(characterName, async repository =>
        {
            var character = await repository.GetCharacterByNameWithItemsAsync(characterName);
            var item = FindByHandle(character?.Items, itemHandle);
            if (item is null)
            {
                return new ItemRemoval(null, 0);
            }

            var count = resolveCount(item);
            if (count <= 0)
            {
                return new ItemRemoval(item, 0);
            }

            var removed = RemoveAmount(repository, character, item, count);
            InventoryArrange.EnsureContiguousIndices(character.Items.ToArray());
            await repository.SaveChangesAsync();
            return new ItemRemoval(item, removed);
        });
    }

    /// <summary>
    /// Takes up to <paramref name="count"/> units off a stack and returns how many were taken. A stack
    /// that runs out is deleted through the repository: removing it from <c>character.Items</c> alone
    /// would only orphan the row, since <c>ItemEntity.CharacterId</c> is nullable.
    /// </summary>
    private static long RemoveAmount(ICharacterRepository repository, CharacterEntity character, ItemEntity item,
        long count)
    {
        var removed = Math.Min(count, item.Amount);
        if (removed >= item.Amount)
        {
            character.Items.Remove(item);
            repository.DeleteItem(item);
        }
        else
        {
            item.Amount -= removed;
        }

        return removed;
    }

    public Task<ItemEntity> AddItemAsync(string characterName, int itemResourceId, long count)
    {
        return RunExclusiveAsync(characterName, async repository =>
        {
            var character = await repository.GetCharacterByNameWithItemsAsync(characterName);
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
            await repository.SaveChangesAsync();
            return added;
        });
    }

    public Task<ItemEntity[]> SwapItemPositionsAsync(string characterName, uint itemHandle1, uint itemHandle2)
    {
        return RunExclusiveAsync(characterName, async repository =>
        {
            var character = await repository.GetCharacterByNameWithItemsAsync(characterName);
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
            await repository.SaveChangesAsync();

            return items;
        });
    }

    public Task SaveProgressAsync(string characterName, int level, int jobLevel, long exp, long jp,
        long gold, int chaos, float x, float y, bool pkMode)
    {
        if (string.IsNullOrEmpty(characterName))
        {
            return Task.CompletedTask;
        }

        return RunExclusiveAsync(characterName, async repository =>
        {
            var character = await repository.GetCharacterByNameAsync(characterName);
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
            character.PkMode = pkMode;

            // Without this a warp is undone by the next login: the position was never persisted
            // during play, so the character always reloaded where it last logged in.
            if (x > 0 && y > 0)
            {
                character.Position = new[] { (int)x, (int)y, 0 };
            }

            await repository.SaveChangesAsync();
        });
    }

    private static ItemEntity FindByHandle(IEnumerable<ItemEntity> items, uint handle)
    {
        return items?.FirstOrDefault(item => (uint)item.Id == handle);
    }

    /// <summary>A read-modify-write on one character: its own repository, under that character's gate.</summary>
    private Task<T> RunExclusiveAsync<T>(string key, Func<ICharacterRepository, Task<T>> operation)
    {
        return _gate.RunAsync(key, async () =>
        {
            using var repository = _repositories.Create();
            return await operation(repository);
        });
    }

    private Task RunExclusiveAsync(string key, Func<ICharacterRepository, Task> operation)
    {
        return _gate.RunAsync(key, async () =>
        {
            using var repository = _repositories.Create();
            await operation(repository);
        });
    }

    /// <summary>A pure read: its own repository, and no gate, since it changes nothing.</summary>
    private async Task<T> ReadAsync<T>(Func<ICharacterRepository, Task<T>> operation)
    {
        using var repository = _repositories.Create();
        return await operation(repository);
    }
}
