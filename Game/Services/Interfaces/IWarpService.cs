using Navislamia.Game.Network.Clients;

namespace Navislamia.Game.Services.Interfaces;

public interface IWarpService
{
    void Warp(GameClient client, float x, float y);
    void Warp(GameClient client, float x, float y, byte layer)
    {
        client.ConnectionInfo.Layer = layer;
        Warp(client, x, y);
    }
}
