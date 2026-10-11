using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Navislamia.Game.DataAccess.Contexts;
using Navislamia.Game.DataAccess.Entities.Telecaster;
using Navislamia.Game.DataAccess.Repositories.Interfaces;
using Navislamia.Game.Services.ItemShop;

namespace Navislamia.Game.DataAccess.Repositories;

/// <summary>
/// <c>AccountCashPoints</c> and the <c>PaidItems</c> line a purchase writes, one context per call like
/// <see cref="PaidItemRepository"/>. A purchase is a single <c>SaveChangesAsync</c>, so EF wraps the debit and the
/// line in one transaction without a hand-opened one (which the retrying strategy would refuse).
/// </summary>
public class ItemShopRepository : IItemShopRepository
{
    private readonly DbContextOptions<TelecasterContext> _options;

    public ItemShopRepository(DbContextOptions<TelecasterContext> options)
    {
        _options = options;
    }

    public async Task<long> GetPointsAsync(long accountId)
    {
        await using var context = new TelecasterContext(_options);
        return await context.AccountCashPoints.AsNoTracking()
            .Where(row => row.AccountId == accountId)
            .Select(row => row.Points)
            .FirstOrDefaultAsync();
    }

    public async Task<long?> AddPointsAsync(long accountId, long delta)
    {
        await using var context = new TelecasterContext(_options);
        var row = await RowOf(context, accountId);
        if (!ItemShopRules.TryAdd(row.Points, delta, out var balance))
        {
            return null;
        }

        row.Points = balance;
        await context.SaveChangesAsync();
        return balance;
    }

    public async Task<ItemShopPurchaseResult> PurchaseAsync(ItemShopPurchase purchase)
    {
        await using var context = new TelecasterContext(_options);
        var row = await RowOf(context, purchase.AccountId);
        if (row.Points < purchase.Price)
        {
            return new ItemShopPurchaseResult(ItemShopPurchaseOutcome.NotEnoughPoints, row.Points);
        }

        row.Points -= purchase.Price;
        context.PaidItems.Add(new PaidItemEntity
        {
            AccountId = purchase.AccountId,
            // An account-wide line: any character of the account takes it (§5.2.3 of the container sheet), the
            // official "bought on the web site" case (avatar_id -1, DB_CommercialItemStorage.cpp:156).
            CharacterId = null,
            CharacterName = purchase.CharacterName,
            ItemCode = purchase.Code,
            ItemCount = purchase.Count,
            RestItemCount = purchase.Count,
            BoughtTime = DateTime.UtcNow,
            ServerName = purchase.ServerName
        });
        await context.SaveChangesAsync();
        return new ItemShopPurchaseResult(ItemShopPurchaseOutcome.Bought, row.Points);
    }

    private static async Task<AccountCashPointEntity> RowOf(TelecasterContext context, long accountId)
    {
        var row = await context.AccountCashPoints.FirstOrDefaultAsync(r => r.AccountId == accountId);
        if (row is null)
        {
            row = new AccountCashPointEntity { AccountId = accountId };
            context.AccountCashPoints.Add(row);
        }

        return row;
    }
}
