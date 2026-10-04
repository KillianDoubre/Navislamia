using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Navislamia.Game.DataAccess.Entities.Enums;
using Navislamia.Game.DataAccess.Entities.Telecaster;
using Navislamia.Game.DataAccess.Repositories.Interfaces;
using Navislamia.Game.Network.Packets;
using Navislamia.Game.Network.Packets.Game;

using Serilog;

namespace Navislamia.Game.Services;

public class CharacterService : ICharacterService
{
    private readonly ILogger<CharacterService> _logger;
    private readonly ICharacterRepositoryFactory _repositories;
    private readonly IItemMatchCatalog _itemTemplates;
    private readonly IStarterItemsRepository _starterItemsRepository;
    private readonly CharacterGate _gate;
    private readonly Weight.IInventoryChangeFeed _inventoryFeed;

    /// <summary>
    /// An item carries four chassis at most and the frame names four handles
    /// (rzu <c>TS_CS_SOULSTONE_CRAFT.h</c>, NGemity <c>WorldSession.cpp:1520,1568</c>).
    /// </summary>
    private const int SoulstoneSocketCount = 4;

    /// <summary>
    /// Each operation gets its own repository, hence its own context, and runs under the gate of the
    /// character it touches (<see cref="CharacterGate"/>): two players no longer wait on each other.
    /// </summary>
    public CharacterService(IStarterItemsRepository starterItemsRepository, ICharacterRepositoryFactory repositories,
        CharacterGate gate, ILogger<CharacterService> logger, Weight.IInventoryChangeFeed inventoryFeed = null,
        IItemMatchCatalog itemTemplates = null)
    {
        _itemTemplates = itemTemplates;
        _inventoryFeed = inventoryFeed;
        _starterItemsRepository = starterItemsRepository;
        _repositories = repositories;
        _gate = gate;
        _logger = logger;
    }

    public Task<IReadOnlyList<ItemEntity>> ConsumeEtherealAsync(string characterName, Func<ItemEntity, int> amount) =>
        RunInventoryAsync<IReadOnlyList<ItemEntity>>(characterName, async repository =>
        {
            var character = await repository.GetCharacterByNameWithItemsAsync(characterName);
            var changed = new List<ItemEntity>();
            foreach (var item in character?.Items ?? Array.Empty<ItemEntity>())
            {
                var loss = amount(item);
                if (loss <= 0 || item.EtherealDurability <= 0) continue;
                item.EtherealDurability = Math.Max(0, item.EtherealDurability - loss);
                changed.Add(item);
            }
            if (changed.Count > 0) await repository.SaveChangesAsync();
            return changed;
        });

    public Task<SkillResetCommit> ApplySkillResetAsync(string characterName, SkillResetWrite write) =>
        RunInventoryAsync(characterName, async repository =>
        {
            var character = await repository.GetCharacterByNameWithSkillsAsync(characterName);
            if (character is null)
            {
                return new SkillResetCommit(false);
            }

            ItemEntity stone = null;
            if (write.Race is { } race)
            {
                // The same context: the items join the tracked character.
                await repository.GetCharacterByNameWithItemsAsync(characterName);
                stone = character.Items?.Where(item => item.ItemResourceId == race.StoneResourceId && item.Amount > 0
                        && item.WearInfo == ItemWearType.None && item.EquippedBySummonId is null)
                    .OrderBy(item => item.Idx).FirstOrDefault();
                if (stone is null)
                {
                    return new SkillResetCommit(false);
                }

                stone.Amount -= 1;
                if (stone.Amount == 0)
                {
                    character.Items.Remove(stone);
                    repository.DeleteItem(stone);
                }

                // ResetJob(0) then ChangeJob(base job of the race): the base job of the new race, depth 0, no history.
                character.Race = race.Race;
                character.CurrentJob = (Job)race.Job;
                character.PreviousJobs = new Job[3];
                character.JobLvs = new int[3];
                character.JobDepth = (JobDepth)1;
            }

            character.Skills ??= new List<CharacterSkillEntity>();
            foreach (var skill in character.Skills.ToArray())
            {
                if (write.Skills.TryGetValue(skill.SkillId, out var level) && level > 0)
                {
                    skill.Level = level;
                }
                else
                {
                    character.Skills.Remove(skill);
                }
            }

            character.Jp = write.Jp;
            character.TalentPoint = write.TalentPoint;
            character.Jlv = write.JobLevel;
            if (write.Gold is { } gold)
            {
                character.Gold = gold;
            }

            if (write.ResetCount is { } count)
            {
                var flags = (character.FlagList ?? Array.Empty<string>())
                    .Where(flag => !flag.StartsWith(Jobs.SkillResetRules.ResetCountFlag + ":", StringComparison.Ordinal))
                    .Append($"{Jobs.SkillResetRules.ResetCountFlag}:{count}");
                character.FlagList = flags.ToArray();
            }

            await repository.SaveChangesAsync();
            return new SkillResetCommit(true, stone);
        });

    /// <summary>The character's script flags (<c>get_flag</c>), read without the gate.</summary>
    public async Task<IReadOnlyDictionary<string, string>> GetFlagsAsync(string characterName)
    {
        using var repository = _repositories.Create();
        var character = await repository.GetCharacterByNameAsync(characterName);
        return (character?.FlagList ?? Array.Empty<string>())
            .Where(flag => flag?.Contains(':') == true)
            .Select(flag => flag.Split(':', 2))
            .GroupBy(pair => pair[0], StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.Last()[1], StringComparer.Ordinal);
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

            if (character is not null && !character.AutoUsed)
                character.AutoUsed = (await repository.GetCharactersByAccountNameAsync(accountName))
                    .Any(c => c.AutoUsed);
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

            foreach (var item in character.Items ?? Array.Empty<ItemEntity>()) EtherealWearRules.Initialize(item, _itemTemplates);
            var result = await repository.CreateCharacterAsync(character);
            await repository.SaveChangesAsync();

            return result;
        });
    }

    public Task<bool> SetAutoUsedAsync(string accountName, string characterName, bool value)
    {
        return RunExclusiveAsync("auto-account:" + accountName, async repository =>
        {
            var characters = (await repository.GetCharactersByAccountNameAsync(accountName)).ToArray();
            var target = characters.FirstOrDefault(c => c.CharacterName == characterName);
            if (target is null) return false;
            if (value) target.AutoUsed = true;
            else foreach (var character in characters) character.AutoUsed = false;
            await repository.SaveChangesAsync();
            return true;
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

    public Task<bool> UpdateHideEquipFlagAsync(string characterName, int hideEquipFlag)
    {
        return RunExclusiveAsync(characterName, async repository =>
        {
            var character = await repository.GetCharacterByNameAsync(characterName);
            if (character is null)
            {
                return false;
            }

            character.HideEquipFlag = hideEquipFlag;
            await repository.SaveChangesAsync();
            return true;
        });
    }

    public Task<bool> SaveLearnedSkillAsync(string characterName, int skillId, byte level, long remainingJp) =>
        SaveLearnedSkillAsync(characterName, skillId, level, remainingJp, null);

    public Task<bool> SaveLearnedSkillAsync(string characterName, int skillId, byte level, long remainingJp,
        int remainingTalentPoint) =>
        SaveLearnedSkillAsync(characterName, skillId, level, remainingJp, (int?)remainingTalentPoint);

    private Task<bool> SaveLearnedSkillAsync(string characterName, int skillId, byte level, long remainingJp,
        int? remainingTalentPoint)
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
            if (remainingTalentPoint is { } talentPoint)
            {
                character.TalentPoint = talentPoint;
            }

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
        return RunInventoryAsync(characterName, async repository =>
        {
            var character = await repository.GetCharacterByNameWithItemsAsync(characterName);
            var item = character?.Items?.FirstOrDefault(entry => ItemWearRules.IsWornByPlayerAt(entry, position));
            if (item is null)
            {
                return null;
            }

            item.WearInfo = ItemWearType.None;
            await repository.SaveChangesAsync();
            return item;
        });
    }

    /// <summary>The main slots and their spare twins (<c>WEAR_SPARE_*</c> = main + 24).</summary>
    private static readonly (ItemWearType Main, ItemWearType Spare)[] SwapPairs =
    {
        (ItemWearType.Weapon, ItemWearType.SpareWeapon), (ItemWearType.Shield, ItemWearType.SpareShield),
        (ItemWearType.DecoWeapon, ItemWearType.SpareDecoWeapon), (ItemWearType.DecoShield, ItemWearType.SpareDecoShield)
    };

    public Task<(CharacterEntity Character, IReadOnlyList<ItemEntity> Moved)?> SwapEquipAsync(string characterName,
        Func<ItemEntity, bool> mayWearMain)
    {
        return RunExclusiveAsync<(CharacterEntity, IReadOnlyList<ItemEntity>)?>(characterName, async repository =>
        {
            var character = await repository.GetCharacterByNameWithItemsAsync(characterName);
            if (character?.Items is null)
            {
                return null;
            }

            var moves = new List<(ItemEntity Item, ItemWearType To)>();
            foreach (var (main, spare) in SwapPairs)
            {
                var worn = character.Items.FirstOrDefault(item => ItemWearRules.IsWornByPlayerAt(item, main));
                var kept = character.Items.FirstOrDefault(item => ItemWearRules.IsWornByPlayerAt(item, spare));
                if (worn is not null)
                {
                    moves.Add((worn, spare));
                }

                if (kept is not null)
                {
                    if (!mayWearMain(kept))
                    {
                        return null;
                    }

                    moves.Add((kept, main));
                }
            }

            if (moves.Count == 0)
            {
                return (character, Array.Empty<ItemEntity>());
            }

            foreach (var (item, to) in moves)
            {
                item.WearInfo = to;
            }

            await repository.SaveChangesAsync();
            return (character, moves.Select(move => move.Item).ToList());
        });
    }

    public Task<SummonEquipResult> EquipSummonItemAsync(string characterName, uint itemHandle, long summonId,
        Func<ItemEntity, IReadOnlyList<ItemEntity>, int?> chooseSlot)
    {
        return RunInventoryAsync(characterName, async repository =>
        {
            var character = await repository.GetCharacterByNameWithItemsAsync(characterName);
            var item = FindByHandle(character?.Items, itemHandle);
            if (item is null)
            {
                return new SummonEquipResult(ResultCode.AccessDenied, null, null);
            }

            if (item.WearInfo != ItemWearType.None || item.EquippedBySummonId is not null)
            {
                return new SummonEquipResult(ResultCode.NotActable, null, null);
            }

            var worn = character.Items.Where(entry => Creatures.SummonWearRules.IsWornBy(entry, summonId)).ToList();
            if (chooseSlot(item, worn) is not { } slot)
            {
                return new SummonEquipResult(ResultCode.NotActable, null, null);
            }

            var displaced = worn.FirstOrDefault(entry => (int)entry.WearInfo == slot);
            if (displaced is not null)
            {
                displaced.WearInfo = ItemWearType.None;
                displaced.EquippedBySummonId = null;
            }

            item.WearInfo = (ItemWearType)slot;
            item.EquippedBySummonId = (int)summonId;
            await repository.SaveChangesAsync();
            return new SummonEquipResult(ResultCode.Success, item, displaced);
        });
    }

    public Task<ItemEntity> UnequipSummonItemAsync(string characterName, long summonId, int slot)
    {
        return RunInventoryAsync(characterName, async repository =>
        {
            var character = await repository.GetCharacterByNameWithItemsAsync(characterName);
            var item = character?.Items?.FirstOrDefault(entry =>
                Creatures.SummonWearRules.IsWornBy(entry, summonId) && (int)entry.WearInfo == slot);
            if (item is null)
            {
                return null;
            }

            item.WearInfo = ItemWearType.None;
            item.EquippedBySummonId = null;
            await repository.SaveChangesAsync();
            return item;
        });
    }

    public async Task<IReadOnlyList<ItemEntity>> GetSummonEquipmentAsync(string characterName)
    {
        using var repository = _repositories.Create();
        var character = await repository.GetCharacterByNameWithItemsAsync(characterName);
        return character?.Items?.Where(item => item.EquippedBySummonId is not null && item.WearInfo != ItemWearType.None)
            .ToList() ?? new List<ItemEntity>();
    }

    public Task UnwearItemsAsync(string characterName, IReadOnlyCollection<long> itemIds)
    {
        if (itemIds.Count == 0)
        {
            return Task.CompletedTask;
        }

        return RunExclusiveAsync<bool>(characterName, async repository =>
        {
            var character = await repository.GetCharacterByNameWithItemsAsync(characterName);
            var changed = false;
            foreach (var item in character?.Items ?? Enumerable.Empty<ItemEntity>())
            {
                if (itemIds.Contains(item.Id) && item.WearInfo != ItemWearType.None)
                {
                    item.WearInfo = ItemWearType.None;
                    changed = true;
                }
            }

            if (changed)
            {
                await repository.SaveChangesAsync();
            }

            return changed;
        });
    }

    public Task<EquipItemResult> EquipItemAsync(string characterName, uint itemHandle, ItemWearType position)
    {
        return RunInventoryAsync(characterName, async repository =>
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

            var displaced = character.Items.FirstOrDefault(entry => ItemWearRules.IsWornByPlayerAt(entry, position));
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

    public Task<SkillCardBindAttempt> BindSkillCardAsync(string characterName, uint itemHandle, uint targetHandle,
        IItemGroupCatalog itemGroups)
    {
        return RunInventoryAsync(characterName, async repository =>
        {
            var character = await repository.GetCharacterByNameWithItemsAsync(characterName);
            var item = FindByHandle(character?.Items, itemHandle);
            if (item is null)
            {
                return new SkillCardBindAttempt(SkillCardBindOutcome.NotFound, character, null);
            }

            if (!SkillCardBindRules.IsSelfTarget(targetHandle, (uint)character.Id))
            {
                return new SkillCardBindAttempt(SkillCardBindOutcome.NotActable, character, item);
            }

            ItemGroup? group = itemGroups.TryGetGroup(item.ItemResourceId, out var knownGroup) ? knownGroup : null;
            if (SkillCardBindRules.CheckBindable(group, item.WearInfo, item.SocketItemIds) != ResultCode.Success)
            {
                return new SkillCardBindAttempt(SkillCardBindOutcome.AccessDenied, character, item);
            }

            item.SocketItemIds = WriteBearerSocket(item.SocketItemIds, character.Id);
            await repository.SaveChangesAsync();
            return new SkillCardBindAttempt(SkillCardBindOutcome.Success, character, item);
        });
    }

    public Task<SkillCardBindResult> UnbindSkillCardAsync(string characterName, uint itemHandle,
        uint targetHandle, IItemGroupCatalog catalog)
    {
        // The judgement and the write share the character's gate: a bind handled in between could otherwise
        // put the card back on a bearer between the read and the socket write.
        return RunInventoryAsync(characterName, async repository =>
        {
            var character = await repository.GetCharacterByNameWithItemsAsync(characterName);
            var item = FindByHandle(character?.Items, itemHandle);
            var characterHandle = character is null ? 0u : (uint)character.Id;

            var verdict = SkillCardBindRules.CheckUnbindable(characterHandle, itemHandle, targetHandle, item,
                catalog);
            if (!verdict.Succeeded)
            {
                return verdict;
            }

            ClearBearerSocket(item);
            await repository.SaveChangesAsync();
            return verdict;
        });
    }

    public Task<CardSocketResult> SocketCardAsync(string characterName, ItemWearType position,
        uint cardHandle, ICardSocketCatalog catalog)
    {
        return RunInventoryAsync(characterName, async repository =>
        {
            var character = await repository.GetCharacterByNameWithItemsAsync(characterName);
            var target = character?.Items?.FirstOrDefault(entry => ItemWearRules.IsWornByPlayerAt(entry, position));
            if (target is null)
            {
                return new CardSocketResult(ResultCode.NotExist, null, null, 0);
            }

            var card = FindByHandle(character.Items, cardHandle);
            if (card is null || card.Id == target.Id)
            {
                return new CardSocketResult(ResultCode.NotExist, target, null, 0);
            }

            // An unknown resource carries no socket count, so the target cannot be judged at all.
            if (!catalog.TryGetTemplate(target.ItemResourceId, out var targetTemplate))
            {
                return new CardSocketResult(ResultCode.AccessDenied, target, card, card.Amount);
            }

            var cardIsSoulstone = catalog.TryGetTemplate(card.ItemResourceId, out var cardTemplate)
                && cardTemplate.IsSoulstone;

            var sockets = NormalizeSockets(target.SocketItemIds);
            var code = CardSocketRules.Judge(targetTemplate.SocketCount, sockets, cardIsSoulstone,
                card.ItemResourceId, out var socketIndex);
            if (code != ResultCode.Success)
            {
                return new CardSocketResult(code, target, card, card.Amount);
            }

            // A socket holds the resource code of its stone, on both sides of the protocol
            // (NGemity SetSocketIndex(i, ...GetCode())).
            sockets[socketIndex] = card.ItemResourceId;
            target.SocketItemIds = sockets;

            RemoveAmount(repository, character, card, 1);
            InventoryArrange.EnsureContiguousIndices(character.Items.ToArray());
            await repository.SaveChangesAsync();
            return new CardSocketResult(ResultCode.Success, target, card,
                character.Items.Contains(card) ? card.Amount : 0);
        });
    }

    /// <summary>
    /// The four-socket array of an item, padded: stored rows can carry a null or shorter column, and the
    /// inventory sheet serialises at most four sockets (<c>TelecasterContext.cs:55</c>).
    /// </summary>
    private static long[] NormalizeSockets(long[] sockets)
    {
        var normalized = new long[CardSocketRules.MaxSockets];
        if (sockets is null)
        {
            return normalized;
        }

        Array.Copy(sockets, normalized, Math.Min(sockets.Length, normalized.Length));
        return normalized;
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
        return RunInventoryAsync<IReadOnlyList<(uint Handle, long Count)>>(characterName, async repository =>
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
        return RunInventoryAsync<long?>(characterName, async repository =>
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

    public Task<SoulstoneCraftResult> SocketSoulstonesAsync(string characterName, uint craftItemHandle,
        IReadOnlyList<SoulstoneSlotAssignment> assignments)
    {
        return RunInventoryAsync(characterName, async repository =>
        {
            var character = await repository.GetCharacterByNameWithItemsAsync(characterName);
            var item = FindByHandle(character?.Items, craftItemHandle);
            if (item is null)
            {
                return new SoulstoneCraftResult(SoulstoneCraftOutcome.ItemNotFound, null, craftItemHandle, null);
            }

            var stones = new List<ItemEntity>(assignments.Count);
            foreach (var assignment in assignments)
            {
                var stone = FindByHandle(character.Items, assignment.StoneHandle);
                if (stone is null)
                {
                    return new SoulstoneCraftResult(SoulstoneCraftOutcome.StoneNotFound, item,
                        assignment.StoneHandle, null);
                }

                stones.Add(stone);
            }

            // The column always carries four chassis (ItemEntity.SocketItemIds is capped at four); an item whose
            // array is shorter - the migration writes a single zero - is padded rather than written out of bounds.
            var sockets = new long[SoulstoneSocketCount];
            if (item.SocketItemIds is not null)
            {
                Array.Copy(item.SocketItemIds, sockets, Math.Min(SoulstoneSocketCount, item.SocketItemIds.Length));
            }

            for (var index = 0; index < assignments.Count; index++)
            {
                sockets[assignments[index].Slot] = assignments[index].StoneCode;
            }

            item.SocketItemIds = sockets;

            var consumed = new List<(uint Handle, long Remaining)>(stones.Count);
            for (var index = 0; index < assignments.Count; index++)
            {
                var stone = stones[index];
                RemoveAmount(repository, character, stone, 1);
                consumed.Add((assignments[index].StoneHandle, character.Items.Contains(stone) ? stone.Amount : 0));
            }

            InventoryArrange.EnsureContiguousIndices(character.Items.ToArray());
            await repository.SaveChangesAsync();

            return new SoulstoneCraftResult(SoulstoneCraftOutcome.Success, item, 0, consumed);
        });
    }

    public Task<(ItemEntity Item, long Remaining)?> ConsumeFirstAsync(string characterName,
        Func<ItemEntity, bool> match)
    {
        return RunInventoryAsync<(ItemEntity Item, long Remaining)?>(characterName, async repository =>
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

    public Task<CraftCommitResult> ApplyCraftWithCreationAsync(string characterName,
        IReadOnlyList<CraftConsumption> consumed, IReadOnlyList<CraftCreation> created) =>
        ApplyCraftAsync(characterName, consumed, null, created, null, 0);

    public Task<CraftCommitResult> ApplyCraftAsync(string characterName, IReadOnlyList<CraftConsumption> consumed,
        CraftTargetChange? change) => ApplyCraftAsync(characterName, consumed, change, null, null, 0);

    public Task<CraftCommitResult> ApplyMixAsync(string characterName, CraftPlan plan) =>
        ApplyCraftAsync(characterName, plan.Consumed, plan.Change, plan.Created, plan.Mutations,
            plan.EtherealStoneDelta);

    private Task<CraftCommitResult> ApplyCraftAsync(string characterName, IReadOnlyList<CraftConsumption> consumed,
        CraftTargetChange? change, IReadOnlyList<CraftCreation> created, IReadOnlyList<CraftItemMutation> mutations,
        long etherealStoneDelta)
    {
        return RunInventoryAsync(characterName, async repository =>
        {
            var character = await repository.GetCharacterByNameWithItemsAsync(characterName);
            if (character?.Items is null)
            {
                return CraftCommitResult.Failed(CraftCommitOutcome.CharacterMissing);
            }

            // Everything is judged before anything changes: a craft applies whole or not at all.
            var asked = new Dictionary<uint, long>();
            foreach (var line in consumed)
            {
                var total = asked.GetValueOrDefault(line.ItemHandle);
                if (line.Count <= 0 || line.Count > long.MaxValue - total)
                {
                    return CraftCommitResult.Failed(CraftCommitOutcome.ItemMissing);
                }

                if (line.ExpectedMaterial is { } expected
                    && !MatchesCraftMaterial(FindByHandle(character.Items, line.ItemHandle), expected))
                {
                    return CraftCommitResult.Failed(CraftCommitOutcome.TargetChanged);
                }

                asked[line.ItemHandle] = total + line.Count;
            }

            foreach (var (handle, count) in asked)
            {
                var stack = FindByHandle(character.Items, handle);
                if (stack is null || count <= 0 || count > stack.Amount)
                {
                    return CraftCommitResult.Failed(CraftCommitOutcome.ItemMissing);
                }
            }

            ItemEntity target = null;
            if (change is { } planned)
            {
                target = FindByHandle(character.Items, planned.Handle);
                if (target is null || target.Amount < planned.MinimumAmount
                    || (!planned.SplitOne && planned.ExpectedMaterial is null && asked.ContainsKey(planned.Handle))
                    || (planned.SplitOne && !asked.ContainsKey(planned.Handle)))
                {
                    return CraftCommitResult.Failed(CraftCommitOutcome.ItemMissing);
                }

                if (target.Enhance != planned.ExpectedEnhance || (int)target.Flag != planned.ExpectedFlag
                    || (planned.ExpectedMaterial is { } expected && !MatchesCraftMaterial(target, expected)))
                {
                    return CraftCommitResult.Failed(CraftCommitOutcome.TargetChanged);
                }
            }

            foreach (var mutation in mutations ?? Array.Empty<CraftItemMutation>())
            {
                var item = FindByHandle(character.Items, mutation.Handle);
                if (item is null)
                {
                    return CraftCommitResult.Failed(CraftCommitOutcome.ItemMissing);
                }

                if (mutation.Expected is { } expected && !MatchesCraftMaterial(item, expected))
                {
                    return CraftCommitResult.Failed(CraftCommitOutcome.TargetChanged);
                }
            }

            // Copy before consuming: the same stack can supply both cards and disappear entirely.
            var replacement = change is { SplitOne: true, Destroy: false }
                ? CopyCraftCard(target, character) : null;
            var copies = new List<(ItemEntity Copy, CraftCreation Creation)>();
            foreach (var creation in created ?? Array.Empty<CraftCreation>())
            {
                if (creation.CopyOf != 0 && FindByHandle(character.Items, creation.CopyOf) is { } source)
                {
                    copies.Add((CopyCraftCard(source, character), creation));
                }
            }
            var remaining = new List<(uint Handle, long Remaining)>(asked.Count);
            foreach (var (handle, count) in asked)
            {
                var stack = FindByHandle(character.Items, handle);
                var before = stack.Amount;
                remaining.Add((handle, before - RemoveAmount(repository, character, stack, count)));
            }

            if (change is { } applied)
            {
                if (applied.SplitOne)
                {
                    target = replacement;
                    if (target is not null)
                    {
                        target.Enhance = (uint)applied.NewEnhance;
                        target.Flag = (ItemFlag)applied.NewFlag;
                        character.Items.Add(target);
                    }
                }
                else if (applied.Destroy)
                {
                    character.Items.Remove(target);
                    repository.DeleteItem(target);
                    target = null;
                }
                else
                {
                    target.Enhance = (uint)applied.NewEnhance;
                    target.Flag = (ItemFlag)applied.NewFlag;
                }
            }

            var mutated = new List<ItemEntity>();
            foreach (var mutation in mutations ?? Array.Empty<CraftItemMutation>())
            {
                if (FindByHandle(character.Items, mutation.Handle) is { } item)
                {
                    mutation.Apply(item);
                    mutated.Add(item);
                }
            }

            // Every made item is a new row of the bag, like any item the server adds; a copy keeps its source's fields.
            var made = new List<ItemEntity>();
            foreach (var (copy, creation) in copies)
            {
                copy.Amount = creation.Count;
                copy.Enhance = (uint)Math.Max(0, creation.Enhance);
                copy.Idx = character.Items.Max(entry => entry.Idx) + 1;
                character.Items.Add(copy);
                made.Add(copy);
            }

            foreach (var creation in (created ?? Array.Empty<CraftCreation>()).Where(entry => entry.CopyOf == 0))
            {
                var item = new ItemEntity
                {
                    Character = character,
                    CharacterId = character.Id,
                    ItemResourceId = creation.ItemCode,
                    Amount = creation.Count,
                    Level = (uint)Math.Max(0, creation.Level),
                    Enhance = (uint)Math.Max(0, creation.Enhance),
                    GenerateBySource = ItemGenerateSource.Mix,
                    WearInfo = ItemWearType.None,
                    Idx = character.Items.Count == 0
                        ? InventoryArrange.FirstIndex : character.Items.Max(entry => entry.Idx) + 1
                };
                character.Items.Add(item);
                EtherealWearRules.Initialize(item, _itemTemplates);
                made.Add(item);
            }

            long? stone = null;
            if (etherealStoneDelta != 0)
            {
                stone = Math.Clamp(character.EtherealStoneDurability + etherealStoneDelta, 0, CraftingEngine.MaxEtherealStone);
                character.EtherealStoneDurability = (int)stone.Value;
            }

            InventoryArrange.EnsureContiguousIndices(character.Items.ToArray());
            await repository.SaveChangesAsync();
            return new CraftCommitResult(CraftCommitOutcome.Success, remaining, target)
            {
                Created = made,
                Mutated = mutated,
                EtherealStone = stone
            };
        });
    }

    private static bool MatchesCraftMaterial(ItemEntity item, MixMaterial expected) =>
        item is not null && item.ItemResourceId == expected.ItemCode && item.Level == expected.Level
        && item.Enhance == expected.Enhance && (int)item.Flag == expected.Flag
        && item.WearInfo == ItemWearType.None && item.EquippedBySummonId is null
        && item.StorageId is null && item.AuctionId is null;

    private static ItemEntity CopyCraftCard(ItemEntity source, CharacterEntity character) => new()
    {
        Character = character,
        CharacterId = character.Id,
        ItemResourceId = source.ItemResourceId,
        Amount = 1,
        Level = source.Level,
        Enhance = source.Enhance,
        Flag = source.Flag,
        GenerateBySource = source.GenerateBySource,
        WearInfo = ItemWearType.None,
        Idx = character.Items.Max(item => item.Idx) + 1,
        SocketItemIds = source.SocketItemIds?.ToArray(),
        RandomOptionTypes = source.RandomOptionTypes?.ToArray(),
        RandomOptionVars = source.RandomOptionVars?.ToArray(),
        RandomOptionValues = source.RandomOptionValues?.ToArray(),
        Endurance = source.Endurance,
        EtherealDurability = source.EtherealDurability,
        RemainingTime = source.RemainingTime,
        ElementalEffectType = source.ElementalEffectType,
        ElementalEffectExpireTime = source.ElementalEffectExpireTime,
        ElementalEffectAttackPoint = source.ElementalEffectAttackPoint,
        ElementalEffectMagicPoint = source.ElementalEffectMagicPoint
    };

    public Task<ItemRemoval> RemoveItemAsync(string characterName, uint itemHandle,
        Func<ItemEntity, long> resolveCount)
    {
        return RunInventoryAsync(characterName, async repository =>
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
        return RunInventoryAsync(characterName, async repository =>
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

            added.Level = 1;
            added.SocketItemIds = new long[4];
            EtherealWearRules.Initialize(added, _itemTemplates);
            character.Items.Add(added);
            await repository.SaveChangesAsync();
            return added;
        });
    }

    public Task<ItemTransferResult> TransferItemsAsync(ItemTransfer transfer)
    {
        return RunPairInventoryAsync(transfer.GiverName, transfer.ReceiverName, async () =>
        {
            using var repository = _repositories.Create();
            var giver = await repository.GetCharacterByNameWithItemsAsync(transfer.GiverName);
            var receiver = await repository.GetCharacterByNameWithItemsAsync(transfer.ReceiverName);
            if (giver?.Items is null || receiver is null)
            {
                return ItemTransferResult.Failed(ItemTransferOutcome.CharacterMissing);
            }

            // Every line is judged before anything changes: a trade applies whole or not at all.
            var asked = new Dictionary<uint, long>();
            foreach (var line in transfer.Lines)
            {
                asked[line.ItemHandle] = asked.GetValueOrDefault(line.ItemHandle) + line.Count;
            }

            foreach (var (handle, count) in asked)
            {
                var item = FindByHandle(giver.Items, handle);
                if (item is null)
                {
                    return ItemTransferResult.Failed(ItemTransferOutcome.ItemMissing);
                }

                if (item.WearInfo != ItemWearType.None)
                {
                    return ItemTransferResult.Failed(ItemTransferOutcome.Worn);
                }

                if (count <= 0 || count > item.Amount)
                {
                    return ItemTransferResult.Failed(ItemTransferOutcome.NotEnough);
                }
            }

            receiver.Items ??= new List<ItemEntity>();
            var nextIndex = receiver.Items.Count == 0
                ? InventoryArrange.FirstIndex
                : receiver.Items.Max(item => item.Idx) + 1;
            var moved = new List<ItemTransferred>(asked.Count);

            foreach (var (handle, count) in asked)
            {
                var item = FindByHandle(giver.Items, handle);
                var remaining = item.Amount - count;
                ItemEntity received;
                if (remaining == 0)
                {
                    giver.Items.Remove(item);
                    item.CharacterId = receiver.Id;
                    item.Idx = nextIndex++;
                    receiver.Items.Add(item);
                    received = item;
                }
                else
                {
                    item.Amount = remaining;
                    received = SplitOff(item, count, nextIndex++);
                    receiver.Items.Add(received);
                }

                moved.Add(new ItemTransferred(handle, remaining, received));
            }

            giver.Gold = transfer.GiverGold;
            receiver.Gold = transfer.ReceiverGold;
            InventoryArrange.EnsureContiguousIndices(giver.Items.ToArray());
            await repository.SaveChangesAsync();
            return new ItemTransferResult(ItemTransferOutcome.Success, moved);
        });
    }

    public Task<ItemExchangeResult> ExchangeItemsAsync(ItemExchange exchange)
    {
        return RunPairInventoryAsync(exchange.FirstName, exchange.SecondName, async () =>
        {
            using var repository = _repositories.Create();
            var first = await repository.GetCharacterByNameWithItemsAsync(exchange.FirstName);
            var second = await repository.GetCharacterByNameWithItemsAsync(exchange.SecondName);
            if (first is null || second is null)
            {
                return ItemExchangeResult.Failed(ItemTransferOutcome.CharacterMissing);
            }

            first.Items ??= new List<ItemEntity>();
            second.Items ??= new List<ItemEntity>();

            // Both directions are judged before either moves: the trade applies whole or not at all.
            var firstOutcome = JudgeLines(first.Items, exchange.FirstGives, out var firstAsked);
            if (firstOutcome != ItemTransferOutcome.Success)
            {
                return ItemExchangeResult.Failed(firstOutcome);
            }

            var secondOutcome = JudgeLines(second.Items, exchange.SecondGives, out var secondAsked);
            if (secondOutcome != ItemTransferOutcome.Success)
            {
                return ItemExchangeResult.Failed(secondOutcome);
            }

            var firstGave = MoveLines(first, second, firstAsked);
            var secondGave = MoveLines(second, first, secondAsked);

            first.Gold = exchange.FirstGold;
            second.Gold = exchange.SecondGold;
            InventoryArrange.EnsureContiguousIndices(first.Items.ToArray());
            InventoryArrange.EnsureContiguousIndices(second.Items.ToArray());
            await repository.SaveChangesAsync();
            return new ItemExchangeResult(ItemTransferOutcome.Success, firstGave, secondGave);
        });
    }

    /// <summary>The checks of <see cref="TransferItemsAsync"/> for one giver: in the bag, not worn, enough units.</summary>
    private static ItemTransferOutcome JudgeLines(ICollection<ItemEntity> items, IReadOnlyList<ItemTransferLine> lines,
        out Dictionary<uint, long> asked)
    {
        asked = new Dictionary<uint, long>();
        foreach (var line in lines)
        {
            asked[line.ItemHandle] = asked.GetValueOrDefault(line.ItemHandle) + line.Count;
        }

        foreach (var (handle, count) in asked)
        {
            var item = FindByHandle(items, handle);
            if (item is null)
            {
                return ItemTransferOutcome.ItemMissing;
            }

            if (item.WearInfo != ItemWearType.None)
            {
                return ItemTransferOutcome.Worn;
            }

            if (count <= 0 || count > item.Amount)
            {
                return ItemTransferOutcome.NotEnough;
            }
        }

        return ItemTransferOutcome.Success;
    }

    /// <summary>
    /// Moves judged lines from <paramref name="giver"/> to <paramref name="receiver"/>: a whole stack keeps
    /// its row, part of one becomes a new row with the same attributes, at the end of the receiver's bag.
    /// </summary>
    private static List<ItemTransferred> MoveLines(CharacterEntity giver, CharacterEntity receiver,
        Dictionary<uint, long> asked)
    {
        var nextIndex = receiver.Items.Count == 0
            ? InventoryArrange.FirstIndex
            : receiver.Items.Max(item => item.Idx) + 1;
        var moved = new List<ItemTransferred>(asked.Count);

        foreach (var (handle, count) in asked)
        {
            var item = FindByHandle(giver.Items, handle);
            var remaining = item.Amount - count;
            ItemEntity received;
            if (remaining == 0)
            {
                giver.Items.Remove(item);
                item.CharacterId = receiver.Id;
                item.Idx = nextIndex++;
                receiver.Items.Add(item);
                received = item;
            }
            else
            {
                item.Amount = remaining;
                received = SplitOff(item, count, nextIndex++);
                receiver.Items.Add(received);
            }

            moved.Add(new ItemTransferred(handle, remaining, received));
        }

        return moved;
    }

    /// <summary>
    /// A new stack of <paramref name="count"/> units carrying every attribute of <paramref name="source"/>:
    /// part of a stack changes hands with the enhance, level, sockets and effects it had.
    /// </summary>
    private static ItemEntity SplitOff(ItemEntity source, long count, int index)
    {
        return new ItemEntity
        {
            ItemResourceId = source.ItemResourceId,
            Amount = count,
            Level = source.Level,
            Enhance = source.Enhance,
            EtherealDurability = source.EtherealDurability,
            Endurance = source.Endurance,
            Flag = source.Flag,
            GenerateBySource = source.GenerateBySource,
            WearInfo = ItemWearType.None,
            SocketItemIds = source.SocketItemIds?.ToArray(),
            RandomOptionTypes = source.RandomOptionTypes?.ToArray(),
            RandomOptionVars = source.RandomOptionVars?.ToArray(),
            RandomOptionValues = source.RandomOptionValues?.ToArray(),
            RemainingTime = source.RemainingTime,
            ElementalEffectType = source.ElementalEffectType,
            ElementalEffectExpireTime = source.ElementalEffectExpireTime,
            ElementalEffectAttackPoint = source.ElementalEffectAttackPoint,
            ElementalEffectMagicPoint = source.ElementalEffectMagicPoint,
            Idx = index
        };
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

    public Task<int?> ChangeJobAsync(string characterName, int job,
        IReadOnlyList<(int Job, int JobLevel)> previousJobs, int talentPoints)
    {
        if (string.IsNullOrEmpty(characterName) || previousJobs is null || previousJobs.Count > 3)
        {
            return Task.FromResult<int?>(null);
        }

        return RunExclusiveAsync<int?>(characterName, async repository =>
        {
            var character = await repository.GetCharacterByNameAsync(characterName);
            if (character is null)
            {
                return null;
            }

            // Three slots of previous jobs, like m_nPrevJobId[MAX_JOB_DEPTH - 1]; the slot is the depth left.
            var jobs = new Job[3];
            var jobLevels = new int[3];
            for (var depth = 0; depth < previousJobs.Count; depth++)
            {
                jobs[depth] = (Job)previousJobs[depth].Job;
                jobLevels[depth] = previousJobs[depth].JobLevel;
            }

            character.PreviousJobs = jobs;
            character.JobLvs = jobLevels;
            character.CurrentJob = (Job)job;
            character.Jlv = 1;
            // Telecaster keeps the depth as a single bit: 1 base, 2 first job, 4 second job, 8 master class.
            character.JobDepth = (JobDepth)(1 << previousJobs.Count);
            character.TalentPoint += Math.Max(0, talentPoints);

            await repository.SaveChangesAsync();
            return character.TalentPoint;
        });
    }

    public async Task<CreatureState> GetCreatureStateAsync(string characterName, IReadOnlyCollection<int> cardIds)
    {
        if (string.IsNullOrEmpty(characterName) || cardIds is null)
        {
            return null;
        }

        using var repository = _repositories.Create();
        var character = await repository.GetCharacterByNameWithItemsAsync(characterName);
        if (character is null)
        {
            return null;
        }

        var summons = await repository.GetSummonsAsync(character.Id);
        var byCard = summons.GroupBy(s => s.CardItemId).ToDictionary(g => g.Key, g => g.First());
        var cards = (character.Items ?? new List<ItemEntity>())
            .Where(item => cardIds.Contains((int)item.ItemResourceId))
            .Select(item => new CreatureCardRecord(item, byCard.GetValueOrDefault(item.Id)))
            .ToList();
        // The six columns hold summon sids, like the official character row (DB_Login: GetSummon(bindSummon[i]));
        // the session and the 303 speak card handles, so each sid is turned back into its card here.
        var cardOfSummon = summons.ToDictionary(s => s.Id, s => s.CardItemId);
        var slots = new long[Creatures.CreatureRules.MaxSlots];
        if (character.SummonSlotItemIds is { } saved)
        {
            for (var i = 0; i < Math.Min(saved.Length, slots.Length); i++)
            {
                slots[i] = cardOfSummon.GetValueOrDefault(saved[i]);
            }
        }

        return new CreatureState(cards, slots, character.MainSummonId);
    }

    public Task<TamingCommit> CommitTamingAsync(string characterName, long cardItemId, bool success, int summonCode,
        string summonName, int hp, int mp)
    {
        return RunInventoryAsync<TamingCommit>(characterName, async repository =>
        {
            var character = await repository.GetCharacterByNameWithItemsAsync(characterName);
            var card = character?.Items?.FirstOrDefault(item => item.Id == cardItemId);
            if (card is null || card.Amount <= 0)
            {
                return null;
            }

            // ProcTame: a Mirror of Taming Card in the bag is broken by the draw whatever its outcome (test, untradable,
            // tradable, in that order), and protects the card from a failure.
            ItemEntity mirror = null;
            foreach (var code in Creatures.CreatureRules.MirrorOfTamingCards)
            {
                mirror = character.Items.FirstOrDefault(item => item.ItemResourceId == code && item.Amount >= 1
                                                                 && item.WearInfo == ItemWearType.None);
                if (mirror is not null)
                {
                    break;
                }
            }

            long? mirrorId = null;
            long mirrorRemaining = 0;
            if (mirror is not null)
            {
                mirrorId = mirror.Id;
                mirrorRemaining = Consume(mirror);
            }

            // EraseItem(pItem, 1): one card of the stack, the empty one the taming marked — unless a mirror kept it.
            long remaining = card.Amount;
            if (success || mirror is null)
            {
                remaining = Consume(card);
            }

            if (!success)
            {
                await repository.SaveChangesAsync();
                return new TamingCommit(cardItemId, remaining, null, null) { MirrorItemId = mirrorId, MirrorRemaining = mirrorRemaining };
            }

            long Consume(ItemEntity item)
            {
                if (item.Amount > 1)
                {
                    item.Amount--;
                    return item.Amount;
                }

                character.Items.Remove(item);
                repository.DeleteItem(item);
                return 0;
            }

            // AllocItem(0, code, 1, BY_TAMING) with ITEM_FLAG_SUMMON: a bound card no longer stacks.
            var bound = new ItemEntity
            {
                ItemResourceId = card.ItemResourceId,
                Amount = 1,
                WearInfo = ItemWearType.None,
                Flag = Creatures.CreatureRules.WithSummonFlag(ItemFlag.None),
                GenerateBySource = ItemGenerateSource.Taming,
                Idx = character.Items.Count == 0
                    ? InventoryArrange.FirstIndex
                    : character.Items.Max(item => item.Idx) + 1
            };
            character.Items.Add(bound);
            await repository.SaveChangesAsync();

            var summon = NewSummon(character, bound.Id, summonCode, summonName, hp, mp);
            repository.AddSummon(summon);
            await repository.SaveChangesAsync();
            return new TamingCommit(cardItemId, remaining, bound, summon) { MirrorItemId = mirrorId, MirrorRemaining = mirrorRemaining };
        });
    }

    public Task<SummonEntity> CreateSummonAsync(string characterName, long cardItemId, int summonCode,
        string summonName, int hp, int mp)
    {
        return RunExclusiveAsync<SummonEntity>(characterName, async repository =>
        {
            var character = await repository.GetCharacterByNameWithItemsAsync(characterName);
            var card = character?.Items?.FirstOrDefault(item => item.Id == cardItemId);
            if (card is null || !Creatures.CreatureRules.IsBound(card.Flag))
            {
                return null;
            }

            var existing = (await repository.GetSummonsAsync(character.Id)).FirstOrDefault(s => s.CardItemId == cardItemId);
            if (existing is not null)
            {
                return existing;
            }

            var summon = NewSummon(character, cardItemId, summonCode, summonName, hp, mp);
            repository.AddSummon(summon);
            await repository.SaveChangesAsync();
            return summon;
        });
    }

    public Task<bool> SaveCreatureFormationAsync(string characterName, long[] slots, long? mainSummonId)
    {
        return RunExclusiveAsync(characterName, async repository =>
        {
            var character = await repository.GetCharacterByNameAsync(characterName);
            if (character is null)
            {
                return false;
            }

            var summonOfCard = (await repository.GetSummonsAsync(character.Id))
                .GroupBy(s => s.CardItemId).ToDictionary(g => g.Key, g => g.First().Id);
            character.SummonSlotItemIds = slots.Select(card => summonOfCard.GetValueOrDefault(card)).ToArray();
            character.MainSummonId = mainSummonId;
            await repository.SaveChangesAsync();
            return true;
        });
    }

    public Task<bool> SaveSummonProgressAsync(string characterName, IReadOnlyList<SummonProgress> summons)
    {
        if (summons is null || summons.Count == 0)
        {
            return Task.FromResult(true);
        }

        return RunExclusiveAsync(characterName, async repository =>
        {
            var character = await repository.GetCharacterByNameAsync(characterName);
            if (character is null)
            {
                return false;
            }

            var rows = (await repository.GetSummonsAsync(character.Id)).ToDictionary(s => s.Id);
            foreach (var progress in summons)
            {
                if (!rows.TryGetValue(progress.SummonId, out var row))
                {
                    continue;
                }

                row.SummonResourceId = progress.SummonResourceId;
                row.Lv = progress.Level;
                row.Jlv = progress.Level;
                row.Exp = progress.Exp;
                row.Jp = progress.Jp;
                row.MaxLevel = progress.MaxLevel;
                row.Hp = progress.Hp;
                row.Mp = progress.Mp;
                row.LastDecreasedExp = progress.LastDecreasedExp;
                row.PreviousSummonResourceIds = (long[])progress.PreviousSummonResourceIds.Clone();
                row.PreviousLevel = (int[])progress.PreviousLevels.Clone();
                if (!string.IsNullOrEmpty(progress.Name))
                {
                    row.Name = progress.Name;
                }
            }

            await repository.SaveChangesAsync();
            return true;
        });
    }

    public async Task<IReadOnlyList<SummonSkillRecord>> GetSummonSkillsAsync(string characterName)
    {
        if (string.IsNullOrEmpty(characterName))
        {
            return Array.Empty<SummonSkillRecord>();
        }

        using var repository = _repositories.Create();
        var character = await repository.GetCharacterByNameAsync(characterName);
        if (character is null)
        {
            return Array.Empty<SummonSkillRecord>();
        }

        return (await repository.GetSummonSkillsAsync(character.Id))
            .Select(skill => new SummonSkillRecord(skill.SummonId, skill.SkillId, skill.Level)).ToList();
    }

    public Task<bool> SaveSummonSkillAsync(string characterName, long summonId, int skillId, byte level, int remainingJp)
    {
        return RunExclusiveAsync(characterName, async repository =>
        {
            var character = await repository.GetCharacterByNameAsync(characterName);
            if (character is null)
            {
                return false;
            }

            var summon = (await repository.GetSummonsAsync(character.Id)).FirstOrDefault(s => s.Id == summonId);
            if (summon is null)
            {
                return false;
            }

            var row = (await repository.GetSummonSkillsAsync(character.Id))
                .FirstOrDefault(s => s.SummonId == summonId && s.SkillId == skillId);
            if (row is null)
            {
                repository.AddSummonSkill(new SummonSkillEntity { SummonId = summonId, SkillId = skillId, Level = level });
            }
            else
            {
                row.Level = level;
            }

            summon.Jp = remainingJp;
            await repository.SaveChangesAsync();
            return true;
        });
    }

    /// <summary><c>AllocNewSummon</c>: level 1, the drawn name, the card linked (<c>DB_InsertSummon</c>).</summary>
    private static SummonEntity NewSummon(CharacterEntity character, long cardItemId, int summonCode, string name,
        int hp, int mp) => new()
    {
        AccountId = character.AccountId,
        CharacterId = character.Id,
        SummonResourceId = summonCode,
        CardItemId = cardItemId,
        Name = Creatures.CreatureRules.TrimName(name),
        Lv = 1,
        Jlv = 1,
        MaxLevel = 1,
        Hp = hp,
        Mp = mp,
        PreviousLevel = new int[2],
        PreviousSummonResourceIds = new long[2]
    };

    public Task SaveProgressAsync(string characterName, int level, int jobLevel, long exp, long jp,
        long gold, int chaos, float x, float y, bool pkMode, PvpProgress? pvp = null, int? stamina = null,
        Huntaholic.HuntaholicProgress? huntaholic = null)
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
            if (stamina.HasValue) character.Stamina = Math.Max(0, stamina.Value);
            character.PkMode = pkMode;
            if (pvp is { } progress)
            {
                character.ImmoralPoint = MoralityRules.Normalize(progress.ImmoralPoint);
                character.PkCount = Math.Max(0, progress.PkCount);
                character.DkCount = Math.Max(0, progress.DkCount);
            }

            // The session's HuntaHolic balance and entries, and when it left: the next login owes a refill
            // only if a 06:00 passed since (HuntaholicEntryRefill.RefillAfterLogout).
            if (huntaholic is { } holic)
            {
                character.HuntaholicPoint = Math.Max(0, holic.Point);
                character.HuntaholicEnterCount = Math.Max(0, holic.EnterCount);
                character.LogoutTime = DateTime.UtcNow;
            }

            // Without this a warp is undone by the next login: the position was never persisted
            // during play, so the character always reloaded where it last logged in.
            if (x > 0 && y > 0)
            {
                character.Position = new[] { (int)x, (int)y, 0 };
            }

            await repository.SaveChangesAsync();
        });
    }

    public Task SaveReturnPointAsync(string characterName, ReturnPoints.ReturnPoint point)
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

            character.FlagList = ReturnPoints.ReturnPointRules.Write(character.FlagList, point);
            await repository.SaveChangesAsync();
        });
    }

    public Task<PetRecord> GetOrCreatePetAsync(string characterName, long characterId, int accountId,
        long cageItemId, int petResourceId, string defaultName)
    {
        return RunExclusiveAsync(characterName, async repository =>
        {
            var pet = await repository.GetPetByItemAsync(cageItemId);
            if (pet is null)
            {
                pet = new PetEntity
                {
                    AccountId = accountId,
                    CharacterId = characterId,
                    ItemId = cageItemId,
                    PetResourceId = petResourceId,
                    Name = defaultName ?? string.Empty,
                    WasNameChanged = false
                };
                repository.AddPet(pet);
                await repository.SaveChangesAsync();
            }

            return new PetRecord(pet.Name ?? string.Empty, pet.WasNameChanged);
        });
    }

    public Task<bool> RenamePetAsync(string characterName, long cageItemId, string name)
    {
        return RunExclusiveAsync(characterName, async repository =>
        {
            var pet = await repository.GetPetByItemAsync(cageItemId);
            if (pet is null)
            {
                return false;
            }

            pet.Name = name;
            pet.WasNameChanged = true;
            await repository.SaveChangesAsync();
            return true;
        });
    }

    private static ItemEntity FindByHandle(IEnumerable<ItemEntity> items, uint handle)
    {
        return items?.FirstOrDefault(item => (uint)item.Id == handle);
    }

    /// <summary>
    /// Zeroes the bearer socket of an item, rebuilding the array so the change tracker sees the row as
    /// modified (an in place write on the converted <c>SocketItemIds</c> can go unnoticed) while sockets
    /// 1 to 3 are carried over untouched. Socket 0 is the only state a skill card unbind changes: the
    /// enhance of the item is left alone by both references and the enhance of the skill has no model
    /// here (spec §5.2 g).
    /// </summary>
    private static void ClearBearerSocket(ItemEntity item)
    {
        var sockets = new long[SkillCardBindRules.SocketCount];
        for (var i = SkillCardBindRules.BearerSocketIndex + 1;
             i < SkillCardBindRules.SocketCount && i < (item.SocketItemIds?.Length ?? 0);
             i++)
        {
            sockets[i] = item.SocketItemIds[i];
        }

        item.SocketItemIds = sockets;
    }

    /// <summary>
    /// Writes <paramref name="bearerId"/> into the bearer socket, rebuilding the four-socket array so the
    /// change tracker sees the row as modified; sockets 1 to 3 are carried over untouched.
    /// </summary>
    private static long[] WriteBearerSocket(long[] sockets, long bearerId)
    {
        var updated = new long[SkillCardBindRules.SocketCount];
        if (sockets is not null)
        {
            Array.Copy(sockets, updated, Math.Min(sockets.Length, SkillCardBindRules.SocketCount));
        }

        updated[SkillCardBindRules.BearerSocketIndex] = bearerId;
        return updated;
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

    /// <summary>
    /// <see cref="RunExclusiveAsync{T}"/> for an operation that can change what the character carries: the
    /// change is announced afterwards (<see cref="Weight.IInventoryChangeFeed"/>), which is how the carried
    /// weight follows every item operation without each service having to say so.
    /// </summary>
    private async Task<T> RunInventoryAsync<T>(string characterName, Func<ICharacterRepository, Task<T>> operation)
    {
        var result = await RunExclusiveAsync(characterName, operation);
        _inventoryFeed?.Publish(characterName);
        return result;
    }

    /// <summary>The two-character counterpart of <see cref="RunInventoryAsync{T}"/>, under both gates.</summary>
    private async Task<T> RunPairInventoryAsync<T>(string first, string second, Func<Task<T>> operation)
    {
        var result = await _gate.RunPairAsync(first, second, operation);
        _inventoryFeed?.Publish(first);
        _inventoryFeed?.Publish(second);
        return result;
    }

    public Task<ItemEntity[]> GetCarriedItemsAsync(string characterName)
    {
        return ReadAsync(async repository =>
            (await repository.GetCharacterByNameWithItemsAsync(characterName))?.Items?.ToArray()
            ?? Array.Empty<ItemEntity>());
    }

    /// <summary>A pure read: its own repository, and no gate, since it changes nothing.</summary>
    private async Task<T> ReadAsync<T>(Func<ICharacterRepository, Task<T>> operation)
    {
        using var repository = _repositories.Create();
        return await operation(repository);
    }
}
