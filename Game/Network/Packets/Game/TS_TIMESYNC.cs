using System.Runtime.InteropServices;

namespace Navislamia.Game.Network.Packets.Game;

[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct TS_TIMESYNC
{
    public uint Time;
}

[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct TS_SC_SET_TIME
{
    public int Gap;
}
