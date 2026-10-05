using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Navislamia.Game.DataAccess.Contexts;
using Navislamia.Game.DataAccess.Entities.Telecaster;
using Navislamia.Game.DataAccess.Repositories.Interfaces;

namespace Navislamia.Game.DataAccess.Repositories;

/// <summary>
/// One unit of work on the character tables: it owns a context for its own lifetime only, and is created
/// by <see cref="CharacterRepositoryFactory"/> for each operation.
/// </summary>
/// <remarks>
/// It used to be a singleton holding one context for the whole server. Every character, item and skill
/// loaded since startup stayed in that change tracker, so each <c>SaveChangesAsync</c> scanned all of
/// them and got slower with uptime, the memory never came back, and one context forced every player's
/// database work through a single gate. A short-lived context reads the database as it is, which also
/// removes the stale reads between this context and the storage's. It also removes the identity map
/// that used to hide a missing <c>Include</c>: whatever an operation needs, it must load.
/// </remarks>
public sealed class CharacterRepository : ICharacterRepository
{
    private readonly TelecasterContext _context;

    public CharacterRepository(DbContextOptions<TelecasterContext> options)
    {
        _context = new TelecasterContext(options);
    }

    public async Task<IEnumerable<CharacterEntity>> GetCharactersByAccountNameAsync(string accountName, bool withItems = false)
    {
        var query = _context.Characters.Where(c => c.AccountName == accountName);

        if (withItems)
        {
            // Two collection includes in one JOIN return items x skills rows: split them.
            query = query.Include(c => c.Items).Include(c => c.Skills).AsSplitQuery();
        }

        return await query.ToListAsync();
    }

    public Task<CharacterEntity> GetAccountCharacterWithItemsAsync(string accountName, string characterName)
    {
        return _context.Characters
            .Where(c => c.AccountName == accountName && c.CharacterName == characterName)
            .Include(c => c.Items)
            .Include(c => c.Skills)
            .AsSplitQuery()
            // Each split query re-runs the row limit, so it needs an order to target the same row.
            .OrderBy(c => c.Id)
            .FirstOrDefaultAsync();
    }

    public async Task<CharacterEntity> CreateCharacterAsync(CharacterEntity character)
    {
        var result = (await _context.Characters.AddAsync(character)).Entity;
        return result;
    }

    public Task<CharacterEntity> GetCharacterByNameAsync(string characterName)
    {
        return _context.Characters.FirstOrDefaultAsync(c => c.CharacterName == characterName);
    }

    public Task<List<AuctionListingEntity>> GetNamedAuctionsAsync(long characterId) =>
        _context.AuctionListings.Where(a => a.SellerId == characterId || a.HighestBidderId == characterId).ToListAsync();

    public Task<CharacterEntity> GetCharacterByNameWithSkillsAsync(string characterName)
    {
        return _context.Characters
            .Include(c => c.Skills)
            .OrderBy(c => c.Id)
            .FirstOrDefaultAsync(c => c.CharacterName == characterName);
    }

    public Task<CharacterEntity> GetCharacterByNameWithItemsAsync(string characterName)
    {
        return _context.Characters
            .Include(c => c.Items)
            .Include(c => c.Skills)
            .AsSplitQuery()
            // Each split query re-runs the row limit, so it needs an order to target the same row.
            .OrderBy(c => c.Id)
            .FirstOrDefaultAsync(c => c.CharacterName == characterName);
    }

    public Task<bool> CharacterExistsAsync(string characterName)
    {
        return _context.Characters.AnyAsync(c => c.CharacterName == characterName);
    }

    public void Delete(CharacterEntity entity)
    {
        _context.Characters.Remove(entity);
    }

    public void DeleteItem(ItemEntity item)
    {
        _context.Remove(item);
    }

    public async Task<List<CharacterQuestEntity>> GetQuestsAsync(string characterName)
    {
        return await QuestsOf(characterName).AsNoTracking().OrderBy(quest => quest.Code).ToListAsync();
    }

    public Task<CharacterQuestEntity> GetQuestAsync(string characterName, int code)
    {
        return QuestsOf(characterName).FirstOrDefaultAsync(quest => quest.Code == code);
    }

    public void DeleteQuest(CharacterQuestEntity quest)
    {
        _context.CharacterQuests.Remove(quest);
    }

    public Task<PetEntity> GetPetByItemAsync(long itemId)
    {
        return _context.Pets.FirstOrDefaultAsync(pet => pet.ItemId == itemId);
    }

    public void AddPet(PetEntity pet)
    {
        _context.Pets.Add(pet);
    }

    public Task<List<SummonEntity>> GetSummonsAsync(long characterId)
    {
        return _context.Summons.Where(summon => summon.CharacterId == characterId).ToListAsync();
    }

    public void AddSummon(SummonEntity summon)
    {
        _context.Summons.Add(summon);
    }

    public Task<List<SummonEntity>> GetSummonsOfCardsAsync(IReadOnlyCollection<long> cardItemIds)
    {
        var ids = cardItemIds.ToArray();
        return _context.Summons.Where(summon => ids.Contains(summon.CardItemId)).ToListAsync();
    }

    public Task<List<SummonSkillEntity>> GetSummonSkillsAsync(long characterId)
    {
        return _context.SummonSkills.Where(skill => skill.Summon.CharacterId == characterId).ToListAsync();
    }

    public void AddSummonSkill(SummonSkillEntity skill)
    {
        _context.SummonSkills.Add(skill);
    }

    public async Task<List<SummonSkillEntity>> GetCardSkillsAsync(long cardItemId)
    {
        if (!await _context.Items.AnyAsync(item => item.Id == cardItemId && item.Amount > 0))
        {
            return null;
        }

        return await _context.SummonSkills.AsNoTracking().Where(skill => skill.Summon.CardItemId == cardItemId)
            .OrderBy(skill => skill.SkillId).ToListAsync();
    }

    private IQueryable<CharacterQuestEntity> QuestsOf(string characterName)
    {
        return from quest in _context.CharacterQuests
               join character in _context.Characters on quest.CharacterId equals character.Id
               where character.CharacterName == characterName
               select quest;
    }

    public Task<int> CharacterCountAsync(int accountId)
    {
        return _context.Characters.CountAsync(c => c.AccountId == accountId);
    }

    public async Task SaveChangesAsync()
    {
        await _context.SaveChangesAsync();
    }

    public void Dispose()
    {
        _context.Dispose();
    }
}

/// <summary>Creates one <see cref="CharacterRepository"/>, hence one context, per operation.</summary>
public sealed class CharacterRepositoryFactory : ICharacterRepositoryFactory
{
    private readonly DbContextOptions<TelecasterContext> _options;

    public CharacterRepositoryFactory(DbContextOptions<TelecasterContext> options)
    {
        _options = options;
    }

    public ICharacterRepository Create() => new CharacterRepository(_options);
}
