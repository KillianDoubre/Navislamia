using System.Collections.Concurrent;

namespace Navislamia.AuthServer.GameServers;

public sealed class GameServerRegistry : IGameServerRegistry
{
    private readonly ConcurrentDictionary<int, GameServerInfo> _servers = new();

    public void Register(GameServerInfo server) => _servers[server.Index] = server;

    public IReadOnlyList<GameServerInfo> GetAll() => _servers.Values.OrderBy(s => s.Index).ToList();

    public GameServerInfo? Get(int index) => _servers.TryGetValue(index, out var s) ? s : null;
}
