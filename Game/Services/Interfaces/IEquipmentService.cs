using System.Threading.Tasks;
using Navislamia.Game.Network.Clients;
using Navislamia.Game.Network.Packets.Game;

namespace Navislamia.Game.Services;

public interface IEquipmentService
{
    Task UnequipAsync(GameClient client, GameActionPackets.PutoffItemRequest request);

    Task EquipAsync(GameClient client, GameActionPackets.PutonItemRequest request);

    /// <summary>
    /// <c>TM_CS_PUTON_ITEM_SET</c> (281): the 24 positional handles of one equipment set, already
    /// read from the frame. Zero entries are empty slots.
    /// </summary>
    Task EquipSetAsync(GameClient client, uint[] handles);
}
