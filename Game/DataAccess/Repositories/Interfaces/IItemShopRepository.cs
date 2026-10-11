using System.Threading.Tasks;
using Navislamia.Game.Services.ItemShop;

namespace Navislamia.Game.DataAccess.Repositories.Interfaces;

/// <summary>
/// The points of an account and the purchase that turns them into an Item Box line (<c>PaidItems</c>). Every call
/// opens its own context; the caller serialises the calls of one account (<see cref="ItemShopService"/>).
/// </summary>
public interface IItemShopRepository
{
    /// <summary>The account's points, 0 for an account that never had any.</summary>
    Task<long> GetPointsAsync(long accountId);

    /// <summary>
    /// Adds <paramref name="delta"/> (negative to take) and returns the new balance; <c>null</c> when the balance would
    /// fall below zero, in which case nothing is written.
    /// </summary>
    Task<long?> AddPointsAsync(long accountId, long delta);

    /// <summary>
    /// Takes the price off the points and writes the Item Box line, in one save: both happen or neither does.
    /// </summary>
    Task<ItemShopPurchaseResult> PurchaseAsync(ItemShopPurchase purchase);
}
