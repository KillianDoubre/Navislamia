using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Navislamia.Game.DataAccess.Contexts;
using Navislamia.Game.DataAccess.Entities.Enums;
using Navislamia.Game.DataAccess.Entities.Telecaster;

namespace Navislamia.Game.Services.Party;

/// <summary>A member as the party table knows it: <c>smp_load_party_member_info</c> (sid, name, level, job), and race.</summary>
public sealed record StoredPartyMember(long CharacterId, string Name, int Level, int Job, int Race);

/// <summary>A row of <c>smp_load_party_list</c> with its members.</summary>
public sealed record StoredParty(int Id, string Name, long LeaderId, PartyShareMode ShareMode, int Type,
    IReadOnlyList<StoredPartyMember> Members, long AttackGuild = 0, int DungeonId = 0, long LeadPartyId = 0, int MaxParties = 0);

/// <summary>
/// The party as it must be stored after a change (<c>DB_InsertParty</c>, <c>DB_SetParty</c>,
/// <c>DB_SetPartyLeader</c>); no member means <c>DB_DeleteParty</c>.
/// </summary>
public sealed record PartySnapshot(int Id, string Name, long LeaderId, PartyShareMode ShareMode, int Type,
    IReadOnlyList<long> Members, long? LeadPartyId = null);

/// <summary>
/// The parties across restarts (docs/packet-specs/socle-groupe.md, *Persistance*): the <c>Parties</c> table and
/// <c>Characters.PartyId</c>, like the official <c>Party</c> table and <c>Character.party_id</c>.
/// </summary>
public interface IPartyStore
{
    /// <summary>The live parties with their members, and the highest id ever given (deleted rows included).</summary>
    Task<(IReadOnlyList<StoredParty> Parties, int MaxId)> LoadAsync();

    Task SaveAsync(PartySnapshot snapshot);
}

public sealed class PartyStore : IPartyStore
{
    private readonly DbContextOptions<TelecasterContext> _options;
    private readonly Dungeons.DungeonCatalog _dungeons;

    public PartyStore(DbContextOptions<TelecasterContext> options, Dungeons.DungeonCatalog dungeons = null)
    { _options = options; _dungeons = dungeons; }

    public async Task<(IReadOnlyList<StoredParty> Parties, int MaxId)> LoadAsync()
    {
        await using var db = new TelecasterContext(_options);
        var maxId = await db.Parties.IgnoreQueryFilters().MaxAsync(p => (long?)p.Id) ?? 0;
        var parties = await db.Parties.AsNoTracking().OrderBy(p => p.Id).ToListAsync();
        // The query filter drops a deleted character, as the official join on Character does.
        var members = (await db.Characters.AsNoTracking().Where(c => c.PartyId != null)
                .OrderBy(c => c.Id)
                .Select(c => new { c.Id, c.PartyId, c.CharacterName, c.Lv, c.CurrentJob, c.Race })
                .ToListAsync())
            .GroupBy(c => c.PartyId.Value)
            .ToDictionary(g => g.Key, g => g.Select(c =>
                new StoredPartyMember(c.Id, c.CharacterName, c.Lv, (int)c.CurrentJob, c.Race)).ToList());

        // Official Community/PartyLoader.cpp:281-359: guild of leader, raid dungeon, then linked parties.
        var leaders = parties.Select(p => p.LeaderId).ToArray();
        var leaderGuilds = await db.Characters.AsNoTracking().Where(c => leaders.Contains(c.Id)).ToDictionaryAsync(c => c.Id, c => c.GuildId);
        var guilds = await db.Guilds.AsNoTracking().ToDictionaryAsync(g => g.Id);
        var alliances = await db.Alliances.AsNoTracking().ToDictionaryAsync(a => a.Id);
        var stored = new List<StoredParty>();
        foreach (var p in parties)
        {
            long effective = 0; int dungeon = 0, max = 0;
            if (p.PartyType is PartyType.RaidAttackTeam or PartyType.SiegeAttackTeam
                && leaderGuilds.TryGetValue(p.LeaderId, out var guildId) && guildId is > 0
                && guilds.TryGetValue(guildId.Value, out var guild))
            {
                effective = guild.AllianceId is > 0 && alliances.TryGetValue(guild.AllianceId.Value, out var alliance) ? alliance.LeadGuildId : guild.Id;
                if (guilds.TryGetValue(effective, out var lead) && lead.DungeonId is > 0
                    && _dungeons?.Dungeons.TryGetValue((int)lead.DungeonId, out var definition) == true)
                { dungeon = definition.Id; max = p.PartyType == PartyType.RaidAttackTeam ? definition.RaidParties : definition.GuildParties; }
            }
            stored.Add(new StoredParty((int)p.Id, p.Name ?? string.Empty, p.LeaderId,
                (PartyShareMode)(int)p.ShareMode, (int)p.PartyType,
                members.TryGetValue(p.Id, out var list) ? list : new List<StoredPartyMember>(),
                effective, dungeon, p.LeadPartyId ?? 0, max));
        }
        return (stored, (int)Math.Min(maxId, int.MaxValue));
    }

    public async Task SaveAsync(PartySnapshot snapshot)
    {
        await using var db = new TelecasterContext(_options);
        var row = await db.Parties.IgnoreQueryFilters().FirstOrDefaultAsync(p => p.Id == snapshot.Id);
        var members = (snapshot.Members ?? Array.Empty<long>()).ToArray();
        var linked = await db.Characters.Where(c => c.PartyId == snapshot.Id || members.Contains(c.Id)).ToListAsync();

        if (members.Length == 0)
        {
            // DB_DeleteParty: Character.party_id back to 0, then the row (soft-deleted by the context).
            foreach (var character in linked)
            {
                character.PartyId = null;
            }

            if (row is not null && row.DeletedOn is null)
            {
                db.Parties.Remove(row);
            }

            await db.SaveChangesAsync();
            return;
        }

        if (row is null)
        {
            row = new PartyEntity { Id = snapshot.Id };
            db.Parties.Add(row);
        }

        row.Name = snapshot.Name;
        row.LeaderId = snapshot.LeaderId;
        row.ShareMode = (PartyItemShareMode)(int)snapshot.ShareMode;
        row.PartyType = (PartyType)snapshot.Type;
        row.LeadPartyId = snapshot.Type is 1 or 2 ? snapshot.LeadPartyId ?? snapshot.Id : null;
        row.DeletedOn = null;
        foreach (var character in linked)
        {
            character.PartyId = members.Contains(character.Id) ? snapshot.Id : null;
        }

        await db.SaveChangesAsync();
    }
}
