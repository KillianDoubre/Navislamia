using System.Threading.Tasks;
using Navislamia.Game.Network.Clients;
using Navislamia.Game.Network.Packets.Game;

namespace Navislamia.Game.Services;

public interface IStorageService
{
    /// <summary>
    /// Opens the account storage of the character in session: <c>TM_SC_OPEN_STORAGE</c> (211), the stored
    /// contents in <c>TM_SC_INVENTORY</c> (207) and the <c>storage_gold</c> property.
    /// </summary>
    Task OpenAsync(GameClient client);

    /// <summary>Handles a <c>TM_CS_STORAGE</c> (212) request: item move, gold move or close.</summary>
    Task HandleAsync(GameClient client, GameActionPackets.StorageRequest request);
}
