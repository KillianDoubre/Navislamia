using System.Threading.Tasks;
using Navislamia.Game.Network.Clients;
using Navislamia.Game.Network.Packets.Game;

namespace Navislamia.Game.Services;

public interface IEquipmentService
{
    Task UnequipAsync(GameClient client, GameActionPackets.PutoffItemRequest request);

    Task EquipAsync(GameClient client, GameActionPackets.PutonItemRequest request);
}
