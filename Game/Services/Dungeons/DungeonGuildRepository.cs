using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Navislamia.Game.DataAccess.Contexts;

namespace Navislamia.Game.Services.Dungeons;

public sealed record DungeonGuildState(long? OwnerGuild, long? ChallengerGuild);
public interface IDungeonGuildRepository
{
    Task<DungeonGuildState> GetAsync(int dungeonId);
    Task<int> OwnedDungeonAsync(long guildId);
}

public sealed class DungeonGuildRepository : IDungeonGuildRepository
{
    private readonly DbContextOptions<TelecasterContext> _options;
    public DungeonGuildRepository(DbContextOptions<TelecasterContext> options) => _options = options;

    public async Task<DungeonGuildState> GetAsync(int dungeonId)
    {
        await using var context = new TelecasterContext(_options);
        var siege = await context.GuildSieges.AsNoTracking().Where(s => s.DungeonId == dungeonId && s.FinishedAt == null)
            .OrderByDescending(s => s.Week).FirstOrDefaultAsync();
        if (siege is not null) return new DungeonGuildState(siege.DefenderId, siege.AttackerId);
        return await context.Dungeons.AsNoTracking().Where(d => d.Id == dungeonId)
            .Select(d => new DungeonGuildState(d.OwnerGuildId, d.RaidGuildId)).FirstOrDefaultAsync()
            ?? new DungeonGuildState(null, null);
    }

    public async Task<int> OwnedDungeonAsync(long guildId)
    {
        await using var context = new TelecasterContext(_options);
        var allianceId = await context.Guilds.Where(g => g.Id == guildId).Select(g => g.AllianceId).FirstOrDefaultAsync();
        if (allianceId is > 0) guildId = await context.Alliances.Where(a => a.Id == allianceId).Select(a => a.LeadGuildId).FirstOrDefaultAsync();
        return (int)await context.Dungeons.AsNoTracking().Where(d => d.OwnerGuildId == guildId)
            .OrderBy(d => d.Id).Select(d => d.Id).FirstOrDefaultAsync();
    }
}
