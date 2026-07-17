using Navislamia.Game.Network.Clients;

namespace Navislamia.Game.Services.Interfaces;

public interface IWarpService
{
    void Warp(GameClient client, float x, float y);
}
