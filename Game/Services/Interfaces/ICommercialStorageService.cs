using System.Threading.Tasks;
using Navislamia.Game.Network.Clients;
using Navislamia.Game.Network.Packets.Game;

namespace Navislamia.Game.Services;

/// <summary>
/// The commercial storage (item shop) container of the character in session: what it holds, and the real
/// takeout of one of its lines (docs/packet-specs/socle-stockage-commercial-conteneur.md §5.5-§5.7).
/// </summary>
public interface ICommercialStorageService
{
    /// <summary>
    /// Pushes the pair <c>TM_SC_COMMERCIAL_STORAGE_INFO</c> (10003) then
    /// <c>TM_SC_COMMERCIAL_STORAGE_LIST</c> (10004) for the character of the session, from the rows it may
    /// see. Called at world entry where the two constant frames used to be sent, and again after a
    /// successful takeout, because the client window is a snapshot and nothing asks the server to refresh
    /// it (§5.5, §5.7).
    /// </summary>
    Task SendContainerAsync(GameClient client);

    /// <summary>
    /// <c>DB_GetCommercialStorageInfo</c>: the counters alone (10003), what the official sends at world entry and
    /// after a purchase (<c>onItemPurchased</c>/<c>onItemSupplied</c>, GameMessage.cpp:11626-11669). The client asks
    /// for the list itself (<c>/cstorage</c>) when its Item Box window is open.
    /// </summary>
    Task SendInfoAsync(GameClient client);

    /// <summary>
    /// <c>DB_ReadCommercialStorageList</c>: the list alone (10004), the answer of <c>/cstorage</c>. Never the counters:
    /// the client answers a 10003 with a <c>/cstorage</c> while the window is open, so a counter here would loop.
    /// </summary>
    Task SendListAsync(GameClient client);

    /// <summary>
    /// Handles a <c>TM_CS_TAKEOUT_COMMERCIAL_ITEM</c> (10005): resolve, bound, validate the code, hand the
    /// goods to the bag through the ordinary inventory packets, consume the line and refresh the pair. A
    /// success answers <c>TS_SC_RESULT(10005, SUCCESS, item code)</c> and a refusal <c>ACCESS_DENIED</c>, as
    /// <c>DB_TakeOutCommercialItem</c> does (DB_CommercialItemStorage.cpp:268, 340, 346).
    /// </summary>
    Task HandleTakeoutAsync(GameClient client, GameActionPackets.TakeoutCommercialItemRequest request);
}
