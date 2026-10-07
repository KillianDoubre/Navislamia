using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Navislamia.Game.DataAccess.Contexts;
using Navislamia.Game.DataAccess.Entities.Enums;
using Navislamia.Game.DataAccess.Entities.Telecaster;
using Navislamia.Game.Network.Packets.Game;
using Serilog;

namespace Navislamia.Game.Services.Creatures;

/// <summary>
/// A farm row as the <c>6001</c> needs it: the stored columns plus the card's own motif, its summon's
/// experience and name (§3.2). <see cref="Name"/> and <see cref="Experience"/> come from the summon the
/// card carries, <see cref="CardInfo"/> from the card item itself — the official fills the entry from
/// <c>creatureCard->GetSummonStruct()</c> and <c>fillItemBaseInfo</c> (§3.2).
/// </summary>
public sealed record FarmedSummon(long Id, int Slot, long CardItemId, int MaxLevel, bool IsUsingCracker,
    bool IsCash, DateTime RegistrationTime, int Duration, DateTime? NursingTime, long Experience, string Name,
    ItemFixedInfo CardInfo);

/// <summary>
/// What a deposition writes (<c>DB_InsertFarmInfo</c>, <c>DB_Commands.h:2566-2632</c>): the columns of
/// §5.6 point 1 except the row id. The ticket's duration and the frozen <c>max_level</c> belong to the
/// depositing gesture, which owns the ticket table and the player's level, so the store derives neither.
/// </summary>
public sealed record FarmedSummonDeposit(string CharacterName, int Slot, long CardItemId, int MaxLevel,
    bool IsUsingCracker, bool IsCash, DateTime RegistrationTime, int Duration);

/// <summary>
/// A nursing request's target, as <c>NurseSummon</c> reads it (<c>StructPlayer.cpp:11423-11456</c>): the
/// card's identifier — the UID the reference compares against the farm rows — its flag, whether the
/// character owns a farm row naming it, and that row's nursing time. The store returns null instead when
/// the handle resolves no card of the character at all, which is the reference's first refusal.
/// </summary>
public sealed record FarmNursingTarget(long CardItemId, ItemFlag Flag, bool IsInFarm, DateTime? NursingTime);

/// <summary>
/// The creature farm's storage (docs/packet-specs/socle-ferme-creatures-officielle.md §5.6 point 1): the
/// <c>CreatureFarms</c> table, plus the card's <c>ITEM_FLAG_FARMED_SUMMON</c> bit, which the official
/// poses and clears in the same gestures as the row itself
/// (<c>StructPlayer::FarmSummon</c> <c>orl $0x8000000</c>, <c>RegainSummon</c>).
/// <para>
/// Writes pose or clear the flag <b>and</b> touch the row in one <c>SaveChanges</c>; a store that only
/// wrote the farm row would leave a deposited card looking free to every other item path.
/// </para>
/// </summary>
public interface ICreatureFarmStore
{
    /// <summary>The character's farm, by slot, with the card and its summon resolved.</summary>
    Task<IReadOnlyList<FarmedSummon>> LoadAsync(string characterName);

    /// <summary>
    /// Writes a deposition: the farm row and the card's <c>ITEM_FLAG_FARMED_SUMMON</c>. Returns the new
    /// row's id, or 0 when the character or the card is unknown.
    /// </summary>
    Task<long> InsertAsync(FarmedSummonDeposit deposit);

    /// <summary>
    /// Writes a nursing time (<c>DB_UpdateNursingTime</c>) and nothing else: the 7.3 <c>NurseSummon</c>
    /// poses no flag (<c>StructPlayer.cpp:11423-11467</c>). Returns false when the card is not farmed.
    /// </summary>
    Task<bool> SetNursingTimeAsync(string characterName, long cardItemId, DateTime nursingTime);

    /// <summary>
    /// Reads everything a nursing decides on, by the card's handle: the card the handle names and the farm
    /// row that names it. The <c>creature_card_handle</c> of 6006 is the item's id, as every other handle of
    /// the farm family (<c>ItemFixedInfo.FromItem</c>: <c>Handle = (uint)item.Id</c>). Null when the
    /// character owns no such card — the reference's <c>FindItem</c> failure. Two reads, and no write.
    /// </summary>
    Task<FarmNursingTarget> LoadNursingTargetAsync(string characterName, long cardItemHandle);

    /// <summary>
    /// Removes a farm row and clears the card's <c>ITEM_FLAG_FARMED_SUMMON</c>. The retrieval's experience
    /// is <b>not</b> applied here: <c>RegainSummon</c> grants it before the row disappears, and the
    /// summon curve's source of truth is still open (A VERIFIER 6), so the retrieval lot calls this only
    /// once it can grant it.
    /// </summary>
    Task<bool> RemoveAsync(string characterName, long cardItemId);
}

public sealed class CreatureFarmStore : ICreatureFarmStore
{
    private readonly ILogger _logger = Log.ForContext<CreatureFarmStore>();
    private readonly DbContextOptions<TelecasterContext> _options;
    private readonly ICreatureCatalog _catalog;

    public CreatureFarmStore(DbContextOptions<TelecasterContext> options, ICreatureCatalog catalog = null)
    {
        _options = options;
        _catalog = catalog;
    }

    public async Task<IReadOnlyList<FarmedSummon>> LoadAsync(string characterName)
    {
        if (string.IsNullOrEmpty(characterName))
        {
            return Array.Empty<FarmedSummon>();
        }

        await using var db = new TelecasterContext(_options);
        var rows = await db.CreatureFarms.AsNoTracking()
            .Where(f => f.Character.CharacterName == characterName)
            .OrderBy(f => f.Slot)
            .ToListAsync();
        if (rows.Count == 0)
        {
            return Array.Empty<FarmedSummon>();
        }

        var cardIds = rows.Select(f => f.CardItemId).ToArray();
        var cards = await db.Items.AsNoTracking().Where(i => cardIds.Contains(i.Id)).ToDictionaryAsync(i => i.Id);
        // A card names one summon; should two rows name the same card, the first wins rather than the read throwing.
        var summons = (await db.Summons.AsNoTracking().Where(s => cardIds.Contains(s.CardItemId)).OrderBy(s => s.Id)
                .ToListAsync())
            .GroupBy(s => s.CardItemId).ToDictionary(g => g.Key, g => g.First());

        var farm = new List<FarmedSummon>(rows.Count);
        foreach (var row in rows)
        {
            if (!cards.TryGetValue(row.CardItemId, out var card))
            {
                // The farm row names a card that no longer exists: its 75-byte motif cannot be built and a
                // zeroed one would show a blank cell to the player. The row is skipped, never invented.
                _logger.Warning("Farm row {rowId} of {characterName} names the missing card {cardId}: skipped",
                    row.Id, characterName, row.CardItemId);
                continue;
            }

            summons.TryGetValue(row.CardItemId, out var summon);
            var name = summons.ContainsKey(row.CardItemId)
                ? summon.Name ?? string.Empty
                : _catalog?.FirstSummonForCard((int)card.ItemResourceId)?.Name ?? string.Empty;
            farm.Add(new FarmedSummon(row.Id, row.Slot, row.CardItemId, row.MaxLevel, row.IsUsingCracker, row.IsCash,
                row.RegistrationTime, row.Duration, row.NursingTime, summon?.Exp ?? 0, name,
                ItemFixedInfo.FromItem(card)));
        }

        return farm;
    }

    public async Task<long> InsertAsync(FarmedSummonDeposit deposit)
    {
        await using var db = new TelecasterContext(_options);
        var character = await db.Characters.FirstOrDefaultAsync(c => c.CharacterName == deposit.CharacterName);
        var card = await db.Items.FirstOrDefaultAsync(i => i.Id == deposit.CardItemId);
        if (character is null || card is null)
        {
            _logger.Warning("Refused a deposition for {characterName}: {what} unknown",
                deposit.CharacterName, character is null ? "the character" : "the card");
            return 0;
        }

        var row = new CreatureFarmEntity
        {
            Slot = deposit.Slot,
            CharacterId = character.Id,
            CardItemId = deposit.CardItemId,
            MaxLevel = deposit.MaxLevel,
            IsUsingCracker = deposit.IsUsingCracker,
            IsCash = deposit.IsCash,
            RegistrationTime = deposit.RegistrationTime,
            Duration = deposit.Duration,
            NursingTime = null,
        };
        db.CreatureFarms.Add(row);
        card.Flag = CreatureFarmRules.WithFarmedSummon(card.Flag);
        await db.SaveChangesAsync();
        return row.Id;
    }

    public async Task<bool> SetNursingTimeAsync(string characterName, long cardItemId, DateTime nursingTime)
    {
        await using var db = new TelecasterContext(_options);
        var row = await FarmRowAsync(db, characterName, cardItemId);
        if (row is null)
        {
            return false;
        }

        row.NursingTime = nursingTime;
        await db.SaveChangesAsync();
        return true;
    }

    public async Task<FarmNursingTarget> LoadNursingTargetAsync(string characterName, long cardItemHandle)
    {
        if (string.IsNullOrEmpty(characterName))
        {
            return null;
        }

        await using var db = new TelecasterContext(_options);
        // The card must belong to the character: the reference resolves the handle inside the player's own
        // inventory (FindItem, StructPlayer.cpp:11425-11426), never across characters.
        var card = await db.Items.AsNoTracking()
            .Where(i => i.Id == cardItemHandle && i.Character.CharacterName == characterName)
            .Select(i => new { i.Id, i.Flag })
            .FirstOrDefaultAsync();
        if (card is null)
        {
            return null;
        }

        // The reference compares the card's UID against the farm rows (GetItemUID, :11432-11437); here the
        // row's own CardItemId is that UID, so the match is the row's existence.
        var row = await FarmRowAsync(db, characterName, card.Id);
        return new FarmNursingTarget(card.Id, card.Flag, row is not null, row?.NursingTime);
    }

    public async Task<bool> RemoveAsync(string characterName, long cardItemId)
    {
        await using var db = new TelecasterContext(_options);
        var card = await db.Items.FirstOrDefaultAsync(i => i.Id == cardItemId
            && i.Character.CharacterName == characterName);
        var row = await FarmRowAsync(db, characterName, cardItemId);
        if (card is null && row is null)
        {
            return false;
        }

        if (card is not null)
        {
            card.Flag = CreatureFarmRules.WithoutFarmedSummon(card.Flag);
        }

        if (row is not null)
        {
            db.CreatureFarms.Remove(row);
        }

        await db.SaveChangesAsync();
        return true;
    }

    private static Task<CreatureFarmEntity> FarmRowAsync(TelecasterContext db, string characterName, long cardItemId) =>
        db.CreatureFarms.FirstOrDefaultAsync(f => f.CardItemId == cardItemId
            && f.Character.CharacterName == characterName);
}
