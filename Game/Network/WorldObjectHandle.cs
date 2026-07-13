using System.Threading;

namespace Navislamia.Game.Network;

public static class WorldObjectHandle
{
    private const uint Seed = 0x40000000;
    private static uint _current = Seed;

    public static uint Next() => Interlocked.Increment(ref _current);
}
