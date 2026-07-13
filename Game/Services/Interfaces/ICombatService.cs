using Navislamia.Game.Network.Clients;

namespace Navislamia.Game.Services;

public interface ICombatService
{
    void StartAttack(GameClient client, uint targetHandle);
    void StopAttack(GameClient client);
}
