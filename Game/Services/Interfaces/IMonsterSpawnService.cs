using Navislamia.Game.Network.Clients;

namespace Navislamia.Game.Services;

public interface IMonsterSpawnService
{
    void Sync(GameClient client);
}
