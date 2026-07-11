namespace Navislamia.AuthServer.GameServers;

public interface IGameServerRegistry
{
    void Register(GameServerInfo server);

    IReadOnlyList<GameServerInfo> GetAll();

    GameServerInfo? Get(int index);
}
