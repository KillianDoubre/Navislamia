using System.Collections.Generic;
using Navislamia.Game.Network;
using Navislamia.Game.Network.Packets.Game;

namespace Navislamia.Game.Services;

public static class SpawnedObjectSet
{
    public static void DespawnMissing(Connection connection, Dictionary<long, uint> spawned,
        HashSet<long> visible)
    {
        List<long> leaving = null;

        foreach (var pair in spawned)
        {
            if (visible.Contains(pair.Key))
            {
                continue;
            }

            connection.Send(GameSpawnPackets.BuildLeave(pair.Value));
            leaving ??= new List<long>();
            leaving.Add(pair.Key);
        }

        if (leaving == null)
        {
            return;
        }

        foreach (var id in leaving)
        {
            spawned.Remove(id);
        }

    }
}
