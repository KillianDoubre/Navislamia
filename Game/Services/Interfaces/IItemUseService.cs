using System.Threading.Tasks;
using Navislamia.Game.Network.Clients;
using Navislamia.Game.Network.Packets.Game;

namespace Navislamia.Game.Services;

public interface IItemUseService
{
    Task UseAsync(GameClient client, GameActionPackets.UseItemRequest request);
}
