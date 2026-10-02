using System;
using Navislamia.Game.Network.Clients;
using Navislamia.Game.Network.Packets.Game;

namespace Navislamia.Game.Services;

public readonly record struct PvpProgress(decimal ImmoralPoint, int PkCount, int DkCount);

public static class MoralityRules
{
    public const decimal BloodyLimit = 100m;
    public const decimal DemoniacLimit = 1000m;
    public const int NemesisState = 5999;
    private const decimal Maximum = long.MaxValue / 10000m;
    private const decimal Minimum = long.MinValue / 10000m;

    public static decimal Normalize(decimal value) => decimal.Truncate(Math.Clamp(value, Minimum, Maximum) * 10000m) / 10000m;
    public static long WireValue(decimal value) => decimal.ToInt64(Normalize(value) * 10000m);

    public static decimal KillIncrease(int killerLevel, int victimLevel, bool victimPkOn, decimal victimImmoral,
        decimal killerImmoral, int dkcAfterKill, int levelGap, bool pkServer, int partyCount)
    {
        if (victimPkOn || victimImmoral >= BloodyLimit) return 0m;
        var amount = killerLevel - victimLevel >= levelGap
            ? killerImmoral <= -1000m ? 1000m : killerImmoral < 0m ? 200m : 100m
            : 50m;
        var multiplier = killerImmoral > 1000m ? 2m : 1m + Math.Max(0, decimal.Truncate(killerImmoral / 100m)) * .1m;
        multiplier += dkcAfterKill >= 20 ? 1m : dkcAfterKill >= 5 ? .5m : 0m;
        // The official PK server expression uses integer division for its party multiplier.
        if (pkServer && partyCount > 0) multiplier *= 2 - 2 / (partyCount + 1);
        return amount * multiplier;
    }

    public static decimal AfterMonsterKill(decimal immoral, int monsterLevel, int playerLevel, int partyCount) =>
        immoral > 0m && monsterLevel >= playerLevel
            ? Normalize(immoral - (immoral >= DemoniacLimit ? .5m : 1m) * (partyCount > 0 ? 2m / (partyCount + 1) : 1m))
            : immoral;

    public static decimal AfterDeath(decimal immoral, int pkCount) => immoral > 0m
        ? Math.Max(0m, immoral - (pkCount < 100 ? 10m - Math.Max(0, pkCount) / 10 : 1m)) : immoral;

    public static long RewardExperience(long exp, decimal immoral)
    {
        var reduction = immoral > 1000m ? .9m : immoral > 100m ? decimal.Truncate(immoral / 100m) * .09m : 0m;
        return (long)Math.Max(0m, exp * (1m - reduction));
    }

    public static long DeathExperience(long loss, decimal immoral) =>
        (long)Math.Min(long.MaxValue, loss * (immoral > 1000m ? 3m : immoral > 100m ? 1m + decimal.Truncate(immoral / 100m) * .2m : 1m));

    public static void Set(GameClient client, decimal value)
    {
        var info = client.ConnectionInfo;
        uint oldStatus;
        lock (info.ProgressLock)
        {
            oldStatus = ActorStatus.ForPlayer(info);
            info.ImmoralPoint = Normalize(value);
            client.Connection.Send(GameStatPackets.BuildProperty(info.CharacterHandle, "immoral", WireValue(info.ImmoralPoint)));
        }
        if (ActorStatus.ForPlayer(info) != oldStatus) client.SendActorStatus();
    }

    public static void Add(GameClient client, decimal amount)
    {
        lock (client.ConnectionInfo.ProgressLock) Set(client, client.ConnectionInfo.ImmoralPoint + amount);
    }
}
