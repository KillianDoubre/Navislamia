using Navislamia.Game.Network.Clients;

namespace Navislamia.Game.Services.Interfaces;

/// <summary>
/// The merchant window of <c>TM_SC_MARKET</c> (250). <see cref="Open"/> answers the dialog trigger; the
/// transaction side of the same window lives in <see cref="IMarketTradeService"/>.
/// </summary>
public interface IMarketService
{
    /// <summary>
    /// Answers the merchant trigger of an NPC dialog with the catalogue of <paramref name="marketName"/>,
    /// and reports whether the window was opened: <c>false</c> when the handle is 0, when the trigger
    /// carries no market name (the truncated <c>open_market(</c> form of the 7.3 catalogue) or when the
    /// catalogue does not know it — in which case nothing is sent. A caller that has to remember which
    /// market is open (<c>TM_CS_BUY_ITEM</c>, 251) must therefore read this return value: the session
    /// only ever holds a market that really was announced.
    /// </summary>
    bool Open(GameClient client, uint npcHandle, string marketName);
}
