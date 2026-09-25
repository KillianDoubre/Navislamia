using System.Threading.Tasks;
using Navislamia.Game.Network.Clients;

namespace Navislamia.Game.Services;

public interface IMarketSellService
{
    /// <summary>
    /// Sells <paramref name="sellCount"/> units of the item <paramref name="itemHandle"/> of the character
    /// this client plays, the gesture of <c>TM_CS_SELL_ITEM</c> (252): the units leave the bag, the gold of
    /// their price enters and the client is told both. Refuses, always answered by a <c>TS_SC_RESULT</c>,
    /// when the handle does not resolve, when the count is zero, when the item is not sellable or the stack
    /// does not hold that many units. Nothing is answered to a malformed frame — that refusal is the
    /// caller's, before the gesture.
    /// </summary>
    Task SellAsync(GameClient client, uint itemHandle, ushort sellCount);
}
