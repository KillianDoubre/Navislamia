using System.Runtime.InteropServices;

namespace Navislamia.Game.Network.Packets.Game;

[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct TS_SC_GAME_TIME
{
    public uint T;
    public ulong GameTime;
}
