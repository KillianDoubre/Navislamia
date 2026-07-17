using Navislamia.Game.Network.Clients;

namespace Navislamia.Game.Services.Interfaces;

public interface IFieldPropService
{
    void Sync(GameClient client);
}
