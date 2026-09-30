using System.Threading.Tasks;
using Navislamia.Game.Network.Clients;

namespace Navislamia.Game.Services;

/// <summary>
/// The trade half of the player booth (docs/packet-specs/705-buy-from-booth.md): publishing an open booth
/// to the players who see its owner, naming booths (707/708), closing them (709), and the two trades,
/// buying from a sell booth (705) and selling to a buy booth (706), both reported to the owner with 710.
/// </summary>
public interface IBoothTradeService
{
    /// <summary>Sends the owner's status mask — booth bit included or cleared — to the owner and its observers.</summary>
    void PublishBoothStatus(GameClient owner);

    /// <summary>
    /// Closes the owner's booth if one is open: the status mask loses its booth bit for everyone who sees
    /// the owner, and every open window of the booth receives <c>TM_SC_BOOTH_CLOSED</c> (709).
    /// </summary>
    void CloseBooth(GameClient owner);

    /// <summary><c>TM_CS_GET_BOOTHS_NAME</c> (707), answered with <c>TM_SC_GET_BOOTHS_NAME</c> (708).</summary>
    void HandleGetBoothsName(GameClient client, byte[] packet);

    /// <summary><c>TM_CS_BUY_FROM_BOOTH</c> (705): buys items from a sell booth.</summary>
    Task HandleBuyAsync(GameClient buyer, byte[] packet);

    /// <summary><c>TM_CS_SELL_TO_BOOTH</c> (706): sells one item to a buy booth.</summary>
    Task HandleSellAsync(GameClient seller, byte[] packet);
}
