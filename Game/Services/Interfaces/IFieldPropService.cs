using Navislamia.Game.Network.Clients;

namespace Navislamia.Game.Services.Interfaces;

public interface IFieldPropService
{
    void Sync(GameClient client);

    /// <summary>Streams a spot again to the players who see it (a prop used up, expired or back).</summary>
    void Refresh(float x, float y) { }
}
