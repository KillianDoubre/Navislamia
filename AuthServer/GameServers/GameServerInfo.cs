namespace Navislamia.AuthServer.GameServers;

public sealed class GameServerInfo
{
    public ushort Index { get; init; }
    public string Name { get; init; } = string.Empty;
    public bool IsAdultServer { get; init; }
    public string ScreenshotUrl { get; init; } = string.Empty;
    public string Ip { get; init; } = string.Empty;
    public int Port { get; init; }
    public ushort UserRatio { get; init; }
}
