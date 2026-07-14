using System.Threading.Tasks;
using Navislamia.Game.Network.Clients;
using Navislamia.Game.Network.Packets.Game;

namespace Navislamia.Game.Services;

public interface ISkillService
{
    Task LearnAsync(GameClient client, GameActionPackets.LearnSkillRequest request);
}
