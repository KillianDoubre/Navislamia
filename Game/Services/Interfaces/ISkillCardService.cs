using System.Threading.Tasks;
using Navislamia.Game.Network.Clients;
using Navislamia.Game.Network.Packets.Game;

namespace Navislamia.Game.Services;

public interface ISkillCardService
{
    Task BindAsync(GameClient client, GameActionPackets.BindSkillCardRequest request);
}
