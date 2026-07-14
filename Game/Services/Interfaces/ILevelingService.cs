using Navislamia.Game.Network.Clients;

namespace Navislamia.Game.Services;

public interface ILevelingService
{
    void ApplyExperience(GameClient client);

    void ApplyJobLevelUp(GameClient client, uint targetHandle);
}
