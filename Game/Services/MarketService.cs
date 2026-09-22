using Navislamia.Game.Network.Clients;
using Navislamia.Game.Network.Packets.Game;
using Navislamia.Game.Services.Interfaces;
using Serilog;

namespace Navislamia.Game.Services;

/// <summary>
/// Answers the merchant dialog trigger with <c>TM_SC_MARKET</c> (250), the packet that opens the trade
/// window. Reference chain: <c>onContact</c> then <c>onDialog</c> runs the trigger
/// (<c>WorldSession.cpp:707-729</c>), <c>SCRIPT_ShowMarket</c> resolves the catalogue
/// (<c>XLua.cpp:485-496</c>) and <c>Messages::SendMarketInfo</c> fills the packet
/// (<c>Messages.cpp:229-247</c>).
/// </summary>
public class MarketService : IMarketService
{
    private readonly ILogger _logger = Log.ForContext<MarketService>();
    private readonly IMarketCatalog _catalog;

    public MarketService(IMarketCatalog catalog)
    {
        _catalog = catalog;
    }

    public void Open(GameClient client, uint npcHandle, string marketName)
    {
        if (npcHandle == 0)
        {
            _logger.Warning("Refused a market opening for {clientTag} without an NPC handle", client.ClientTag);
            return;
        }

        if (string.IsNullOrWhiteSpace(marketName))
        {
            // Every merchant trigger in npc-dialogs.73.json is the truncated form "open_market(": the
            // market name was concatenated in the client's Lua and was not captured at generation. The
            // NPC to market link is therefore unknown, and guessing a catalogue would open an empty or
            // wrong window. Nothing is sent.
            _logger.Warning("NPC handle {handle} announced a truncated open_market() trigger for {clientTag}: "
                            + "no market name, so no TM_SC_MARKET was sent", npcHandle, client.ClientTag);
            return;
        }

        if (!_catalog.TryGetMarket(marketName, out var lines) || lines.Count == 0)
        {
            // Same refusal as the reference, which returns before sending when the catalogue is empty
            // (Messages.cpp:231-232). A size-13 TM_SC_MARKET (n = 0) has no known producer.
            _logger.Warning("Unknown market {market} for NPC handle {handle} of {clientTag}: no TM_SC_MARKET "
                            + "was sent", marketName, npcHandle, client.ClientTag);
            return;
        }

        client.Connection.Send(GameTradePackets.BuildMarketInfo(npcHandle, lines));
        _logger.Debug("{clientTag} opened market {market} of NPC handle {handle} with {lines} lines",
            client.ClientTag, marketName, npcHandle, lines.Count);
    }
}
