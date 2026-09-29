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
    /// Handles a <c>TM_CS_TAKEOUT_COMMERCIAL_ITEM</c> (10005): resolve, bound, validate the code, hand the
    /// goods to the bag through the ordinary inventory packets, consume the line and refresh the pair. A
    /// refusal writes nothing and answers nothing — there is no result packet for this family (§5.6).
    /// </summary>
    Task HandleTakeoutAsync(GameClient client, GameActionPackets.TakeoutCommercialItemRequest request);
}
