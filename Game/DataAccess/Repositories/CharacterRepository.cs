using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Navislamia.Game.DataAccess.Contexts;
using Navislamia.Game.DataAccess.Entities.Telecaster;
using Navislamia.Game.DataAccess.Repositories.Interfaces;

namespace Navislamia.Game.DataAccess.Repositories;

public class CharacterRepository : ICharacterRepository
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

    public async Task<CharacterEntity> CreateCharacterAsync(CharacterEntity character)
    {
        var result = (await _context.Characters.AddAsync(character)).Entity;
        return result;
    }

    public CharacterEntity GetCharacterByName(string characterName)
    {
        return _context.Characters.FirstOrDefault(c => c.CharacterName == characterName);
    }

    public CharacterEntity GetCharacterByNameWithItems(string characterName)
    {
        return _context.Characters
            .Include(c => c.Items)
            .Include(c => c.Skills)
            .AsSplitQuery()
            // Each split query re-runs the row limit, so it needs an order to target the same row.
            .OrderBy(c => c.Id)
            .FirstOrDefault(c => c.CharacterName == characterName);
    }

    public bool CharacterExists(string characterName)
    {
        return _context.Characters.Any(c => c.CharacterName == characterName);
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

    private IQueryable<CharacterQuestEntity> QuestsOf(string characterName)
    {
        return from quest in _context.CharacterQuests
               join character in _context.Characters on quest.CharacterId equals character.Id
               where character.CharacterName == characterName
               select quest;
    }

    public int CharacterCount(int accountId)
    {
        return _context.Characters.Count(c => c.AccountId == accountId);
    }

    public async Task SaveChangesAsync()
    {
        await _context.SaveChangesAsync();
    }

}
