using System.Threading.Tasks;
using Navislamia.Game.Network.Clients;
using Navislamia.Game.Network.Packets.Game;

namespace Navislamia.Game.Services;

public interface IInventoryService
{
    Task ArrangeAsync(GameClient client, bool isStorage);

    Task SwapPositionsAsync(GameClient client, GameActionPackets.ChangeItemPositionRequest request);
}
