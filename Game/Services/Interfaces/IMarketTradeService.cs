using System.Threading.Tasks;
using Navislamia.Game.Network.Clients;

namespace Navislamia.Game.Services.Interfaces;

/// <summary>
/// The gestures of an open trade window. <c>MarketService</c> owns the window itself (the
/// <c>TM_SC_MARKET</c> 250 that opens it); this is the transaction side, one 251 frame at a time.
/// See docs/packet-specs/251-buy-item.md.
/// </summary>
public interface IMarketTradeService
{
    /// <summary>
    /// Handles one <c>TM_CS_BUY_ITEM</c> (251): the catalogue line named by <paramref name="itemCode"/> is
    /// bought for <paramref name="buyCount"/> units, the gold is debited and the transaction is
    /// acknowledged with <c>TS_SC_RESULT</c> then echoed with <c>TM_SC_NPC_TRADE_INFO</c> (240). Refusals
    /// answer <c>TS_SC_RESULT</c> alone; a line the open market does not carry is answered with nothing at
    /// all, exactly like the reference. The market is the one the open dialog announced, and a
    /// <c>buyCount</c> of zero, a closed dialog or an unresolvable market are refused with
    /// <c>ResultCode.Unknown</c> (7).
    /// </summary>
    Task BuyAsync(GameClient client, int itemCode, ushort buyCount);
}
