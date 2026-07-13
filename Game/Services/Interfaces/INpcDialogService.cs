using Navislamia.Game.Network.Clients;

namespace Navislamia.Game.Services;

public interface INpcDialogService
{
    void Contact(GameClient client, byte[] packet);
    void Select(GameClient client, byte[] packet);
}
