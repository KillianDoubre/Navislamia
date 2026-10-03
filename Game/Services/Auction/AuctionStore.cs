using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Navislamia.Game.DataAccess.Contexts;
using Navislamia.Game.DataAccess.Entities.Enums;
using Navislamia.Game.DataAccess.Entities.Telecaster;
using Navislamia.Game.Network.Packets;
using Navislamia.Game.Services.Weight;

namespace Navislamia.Game.Services.Auction;

/// <summary>A gold balance to write with an auction change: the session's value, the source of truth.</summary>
public readonly record struct GoldWrite(string CharacterName, long CharacterId, long Gold);

/// <summary>
/// One auction change, applied in a single transaction: the listings to add, update or remove, the keeping
/// entries to add or remove, the item rows that move into a keeping entry, and the actor's gold.
/// </summary>
public sealed class AuctionChange
{
    public GoldWrite? Gold { get; init; }
    public List<AuctionListingEntity> UpdatedListings { get; } = new();
    public List<long> RemovedListings { get; } = new();
    public List<AuctionKeepingEntity> AddedKeepings { get; } = new();
    public List<long> RemovedKeepings { get; } = new();
    public List<long> DeletedItems { get; } = new();
}

public sealed record RegisterOutcome(ResultCode Result, AuctionListingEntity Listing = null, ItemEntity AuctionItem = null,
    ItemEntity RemainingBagItem = null, bool BagItemRemoved = false);

public sealed record TakeOutcome(ResultCode Result, ItemEntity Item = null);

/// <summary>The auction tables (<c>AuctionListings</c>, <c>AuctionKeepings</c>) and the item rows they hold.</summary>
public interface IAuctionStore
{
    Task<IReadOnlyList<(AuctionListingEntity Listing, ItemEntity Item)>> LoadListingsAsync();
    Task<IReadOnlyList<(AuctionKeepingEntity Keeping, ItemEntity Item)>> LoadKeepingsAsync();

    /// <summary>
    /// <c>RegisterItemToSell</c>'s database half: the item leaves the bag (a part of a stack becomes a new row), the
    /// listing is inserted and the seller's gold written, in one save. <paramref name="check"/> judges the bag item
    /// under the character's gate before anything moves.
    /// </summary>
    Task<RegisterOutcome> RegisterAsync(GoldWrite gold, uint itemHandle, long count, AuctionListingEntity listing,
        Func<ItemEntity, ResultCode> check, Func<long> goldAfterCheck);

    Task CommitAsync(AuctionChange change);

    /// <summary><c>TakeKeepedItem</c>: the item joins the bag (or the gold the balance), the entry is removed.</summary>
    Task<TakeOutcome> TakeAsync(GoldWrite gold, AuctionKeepingEntity keeping);
}

public sealed class AuctionStore : IAuctionStore
{
    private readonly DbContextOptions<TelecasterContext> _options;
    private readonly CharacterGate _gate;
    private readonly IInventoryChangeFeed _feed;

    public AuctionStore(DbContextOptions<TelecasterContext> options, CharacterGate gate, IInventoryChangeFeed feed = null)
    {
        _options = options;
        _gate = gate;
        _feed = feed;
    }

    public async Task<IReadOnlyList<(AuctionListingEntity Listing, ItemEntity Item)>> LoadListingsAsync()
    {
        await using var context = new TelecasterContext(_options);
        var listings = await context.AuctionListings.AsNoTracking().ToListAsync();
        var ids = listings.Select(l => l.ItemId).ToArray();
        var items = await context.Items.AsNoTracking().Where(i => ids.Contains(i.Id)).ToDictionaryAsync(i => i.Id);
        return listings.Where(l => items.ContainsKey(l.ItemId)).Select(l => (l, items[l.ItemId])).ToList();
    }

    public async Task<IReadOnlyList<(AuctionKeepingEntity Keeping, ItemEntity Item)>> LoadKeepingsAsync()
    {
        await using var context = new TelecasterContext(_options);
        var keepings = await context.AuctionKeepings.AsNoTracking().ToListAsync();
        var ids = keepings.Where(k => k.ItemId.HasValue).Select(k => k.ItemId.Value).ToArray();
        var items = await context.Items.AsNoTracking().Where(i => ids.Contains(i.Id)).ToDictionaryAsync(i => i.Id);
        return keepings.Where(k => !k.ItemId.HasValue || items.ContainsKey(k.ItemId.Value))
            .Select(k => (k, k.ItemId is { } id ? items[id] : null)).ToList();
    }

    public async Task<RegisterOutcome> RegisterAsync(GoldWrite gold, uint itemHandle, long count,
        AuctionListingEntity listing, Func<ItemEntity, ResultCode> check, Func<long> goldAfterCheck)
    {
        var outcome = await _gate.RunAsync(gold.CharacterName, async () =>
        {
            await using var context = new TelecasterContext(_options);
            var item = await context.Items.FirstOrDefaultAsync(i => i.CharacterId == gold.CharacterId && i.Id == itemHandle);
            if (item is null || item.Amount < count)
            {
                return new RegisterOutcome(ResultCode.NotExist);
            }

            var verdict = check(item);
            if (verdict != ResultCode.Success)
            {
                return new RegisterOutcome(verdict);
            }

            ItemEntity auctioned;
            var removed = false;
            if (item.Amount > count)
            {
                item.Amount -= count;
                auctioned = CopyOf(item, count);
                context.Items.Add(auctioned);
            }
            else
            {
                auctioned = item;
                item.CharacterId = null;
                item.AccountId = null;
                item.Idx = 0;
                removed = true;
            }

            var character = await context.Characters.FirstAsync(c => c.Id == gold.CharacterId);
            character.Gold = goldAfterCheck();
            await context.SaveChangesAsync();

            listing.ItemId = auctioned.Id;
            context.AuctionListings.Add(listing);
            await context.SaveChangesAsync();
            return new RegisterOutcome(ResultCode.Success, listing, auctioned, removed ? null : item, removed);
        });
        if (outcome.Result == ResultCode.Success) _feed?.Publish(gold.CharacterName);
        return outcome;
    }

    public async Task CommitAsync(AuctionChange change)
    {
        async Task Apply()
        {
            await using var context = new TelecasterContext(_options);
            await using var transaction = await context.Database.BeginTransactionAsync();
            if (change.Gold is { } gold)
            {
                var character = await context.Characters.FirstAsync(c => c.Id == gold.CharacterId);
                character.Gold = gold.Gold;
            }

            foreach (var listing in change.UpdatedListings) context.AuctionListings.Update(listing);
            if (change.RemovedListings.Count > 0)
                context.AuctionListings.RemoveRange(await context.AuctionListings.Where(l => change.RemovedListings.Contains(l.Id)).ToListAsync());
            if (change.RemovedKeepings.Count > 0)
                context.AuctionKeepings.RemoveRange(await context.AuctionKeepings.Where(k => change.RemovedKeepings.Contains(k.Id)).ToListAsync());
            if (change.DeletedItems.Count > 0)
                context.Items.RemoveRange(await context.Items.Where(i => change.DeletedItems.Contains(i.Id)).ToListAsync());
            context.AuctionKeepings.AddRange(change.AddedKeepings);
            await context.SaveChangesAsync();
            await transaction.CommitAsync();
        }

        if (change.Gold is { } actor)
        {
            await _gate.RunAsync(actor.CharacterName, Apply);
        }
        else
        {
            await Apply();
        }
    }

    public async Task<TakeOutcome> TakeAsync(GoldWrite gold, AuctionKeepingEntity keeping)
    {
        var outcome = await _gate.RunAsync(gold.CharacterName, async () =>
        {
            await using var context = new TelecasterContext(_options);
            await using var transaction = await context.Database.BeginTransactionAsync();
            var row = await context.AuctionKeepings.FirstOrDefaultAsync(k => k.Id == keeping.Id);
            if (row is null)
            {
                return new TakeOutcome(ResultCode.NotExist);
            }

            var character = await context.Characters.FirstAsync(c => c.Id == gold.CharacterId);
            character.Gold = gold.Gold;
            ItemEntity item = null;
            if (row.ItemId is { } itemId)
            {
                item = await context.Items.FirstOrDefaultAsync(i => i.Id == itemId);
                if (item is null)
                {
                    return new TakeOutcome(ResultCode.NotExist);
                }

                var last = await context.Items.Where(i => i.CharacterId == gold.CharacterId)
                    .Select(i => (int?)i.Idx).MaxAsync() ?? 0;
                item.CharacterId = gold.CharacterId;
                item.AccountId = null;
                item.Idx = Math.Max(InventoryArrange.FirstIndex, last + 1);

                // A creature card brings its creature along (the official summon row follows the card's owner).
                foreach (var summon in await context.Summons.Where(s => s.CardItemId == itemId).ToListAsync())
                {
                    summon.CharacterId = gold.CharacterId;
                    summon.AccountId = character.AccountId;
                }
            }

            context.AuctionKeepings.Remove(row);
            await context.SaveChangesAsync();
            await transaction.CommitAsync();
            return new TakeOutcome(ResultCode.Success, item);
        });
        if (outcome.Item is not null) _feed?.Publish(gold.CharacterName);
        return outcome;
    }

    private static ItemEntity CopyOf(ItemEntity item, long count) => new()
    {
        ItemResourceId = item.ItemResourceId,
        Amount = count,
        Level = item.Level,
        Enhance = item.Enhance,
        EtherealDurability = item.EtherealDurability,
        Endurance = item.Endurance,
        Flag = item.Flag,
        GenerateBySource = item.GenerateBySource,
        WearInfo = ItemWearType.None,
        SocketItemIds = (long[])(item.SocketItemIds ?? new long[4]).Clone(),
        RemainingTime = item.RemainingTime,
        AppearanceCode = item.AppearanceCode,
        ElementalEffectType = item.ElementalEffectType,
        ElementalEffectExpireTime = item.ElementalEffectExpireTime,
        ElementalEffectAttackPoint = item.ElementalEffectAttackPoint,
        ElementalEffectMagicPoint = item.ElementalEffectMagicPoint
    };
}
