using Navislamia.Game.Network.Clients;

namespace Navislamia.Game.Services;

public interface INpcSpawnService
{
    void Sync(GameClient client);
}
