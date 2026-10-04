using System.Linq;
using Navislamia.Game.Network.Clients;

namespace Navislamia.Game.Services.Creatures;

public static class SummonFall
{
    public const int StateId = 9001;
    public const uint Duration = 300;

    public static bool IsActive(ConnectionInfo info, uint now)
    {
        lock (info.BuffLock)
            return info.ActiveBuffs.Any(b => b.StateId == StateId && unchecked((int)(b.EndTick - now)) > 0);
    }
}
