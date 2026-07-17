using Navislamia.Game.Network.Clients;
using Navislamia.Game.Network.Packets.Game;

namespace Navislamia.Game.Services;

public interface ISkillCastService
{
    void Cast(GameClient client, GameActionPackets.SkillRequest request);

    void Register(GameClient client);

    void Unregister(GameClient client);
}
