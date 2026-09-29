using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Navislamia.Game.DataAccess.Contexts;
using Navislamia.Game.DataAccess.Entities.Telecaster;
using Navislamia.Game.DataAccess.Repositories.Interfaces;
using Navislamia.Game.Services;

namespace Navislamia.Game.DataAccess.Repositories;

/// <summary>
/// Reads the commercial storage (item shop) container of one character and takes from it, on the table
/// <c>PaidItems</c> (docs/packet-specs/socle-stockage-commercial-conteneur.md §5.4).
/// <para>
/// One context per call, for the reason written on the storage side
/// (<see cref="StorageRepository"/>: a long-lived context serves stale rows and its change tracker grows
/// with every container ever read).
/// </para>
/// </summary>
public class PaidItemRepository : IPaidItemRepository
{
    private readonly DbContextOptions<TelecasterContext> _options;

    public PaidItemRepository(DbContextOptions<TelecasterContext> options)
    {
        _options = options;
    }

    public async Task<PaidItemEntity[]> GetVisibleAsync(string characterName)
    {
        await using var context = new TelecasterContext(_options);
        var character = await CharacterOf(context, characterName);
        if (character is null)
        {
            return Array.Empty<PaidItemEntity>();
        }

        return await VisibleRows(context, character).AsNoTracking().ToArrayAsync();
    }

    public async Task<PaidItemEntity?> ResolveAsync(string characterName, uint uid)
    {
        await using var context = new TelecasterContext(_options);
        var character = await CharacterOf(context, characterName);
        if (character is null)
        {
            return null;
        }

        // The comparison is on the 64 bits of Id: uint widens into long without loss. A comparison of
        // (uint)row.Id with uid would confuse two rows whose ids differ by 2^32 (§5.2.2).
        return await VisibleRows(context, character).AsNoTracking()
            .FirstOrDefaultAsync(row => row.Id == uid);
    }

    public async Task<CommercialTakeoutResult> ConsumeAsync(string characterName, uint uid, ushort count)
    {
        await using var context = new TelecasterContext(_options);
        var character = await CharacterOf(context, characterName);
        if (character is null)
        {
            return CommercialTakeoutResult.Refused(CommercialTakeoutOutcome.UnknownCharacter);
        }

        var row = await VisibleRows(context, character).FirstOrDefaultAsync(paid => paid.Id == uid);
        if (row is null)
        {
            return CommercialTakeoutResult.Refused(CommercialTakeoutOutcome.UnknownItem);
        }

        if (!CommercialStorageRules.TryTakeable(row, count, out _))
        {
            return CommercialTakeoutResult.Refused(CommercialTakeoutOutcome.InvalidCount);
        }

        row.RestItemCount -= count;
        row.TakenAccountId = character.AccountId;
        row.TakenCharacterId = character.Id;

        // The taker is the character in session, and its name comes from the session, not from the row:
        // the two are equal here, since the row was found by that very name (§5.6 point 6).
        row.TakenCharacterName = characterName;
        row.TakenTime = DateTime.UtcNow;

        await context.SaveChangesAsync();

        return CommercialTakeoutResult.Consumed(row, row.RestItemCount);
    }

    /// <summary>The character in session, resolved by name like every other repository of the depot.</summary>
    private static Task<CharacterEntity?> CharacterOf(TelecasterContext context, string characterName)
        => context.Characters.AsNoTracking().FirstOrDefaultAsync(c => c.CharacterName == characterName);

    /// <summary>
    /// The rows of the container a character may see, in the four conditions of §5.2.3 — the same four
    /// <see cref="CommercialStorageRules.IsVisible"/> decides in memory. A query cannot call that method,
    /// so the two move together. Public so that the shape of the SQL is itself testable offline, without
    /// a database (<c>ToQueryString</c>): this predicate is the ownership rule of the family.
    /// </summary>
    public static IQueryable<PaidItemEntity> VisibleRows(TelecasterContext context, CharacterEntity character)
        => context.PaidItems
            .Where(row => row.AccountId == character.AccountId
                          && (row.CharacterId == null || row.CharacterId == character.Id)
                          && !row.IsCancel
                          && row.RestItemCount > 0)
            .OrderBy(row => row.Id);
}
