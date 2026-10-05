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
    IReadOnlyList<StoredPartyMember> Members);

/// <summary>
/// The party as it must be stored after a change (<c>DB_InsertParty</c>, <c>DB_SetParty</c>,
/// <c>DB_SetPartyLeader</c>); no member means <c>DB_DeleteParty</c>.
/// </summary>
public sealed record PartySnapshot(int Id, string Name, long LeaderId, PartyShareMode ShareMode, int Type,
    IReadOnlyList<long> Members);

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

    public PartyStore(DbContextOptions<TelecasterContext> options) => _options = options;

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

        var stored = parties.Select(p => new StoredParty((int)p.Id, p.Name ?? string.Empty, p.LeaderId,
            (PartyShareMode)(int)p.ShareMode, (int)p.PartyType,
            members.TryGetValue(p.Id, out var list) ? list : new List<StoredPartyMember>())).ToList();
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
        row.LeadPartyId = null;
        row.DeletedOn = null;
        foreach (var character in linked)
        {
            character.PartyId = members.Contains(character.Id) ? snapshot.Id : null;
        }

        await db.SaveChangesAsync();
    }
}
