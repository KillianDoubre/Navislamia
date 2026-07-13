using System.Collections.Generic;
using Navislamia.Game.Network;
using Navislamia.Game.Network.Packets.Game;

namespace Navislamia.Game.Services;

public static class SpawnedObjectSet
{
    public static void DespawnMissing(Connection connection, Dictionary<long, uint> spawned,
        HashSet<long> visible, Dictionary<uint, long> idsByHandle = null)
    {
        List<KeyValuePair<long, uint>> leaving = null;

        foreach (var pair in spawned)
        {
            if (visible.Contains(pair.Key))
            {
                continue;
            }

            connection.Send(GameSpawnPackets.BuildLeave(pair.Value));
            leaving ??= new List<KeyValuePair<long, uint>>();
            leaving.Add(pair);
        }

        if (leaving == null)
        {
            return;
        }

        foreach (var pair in leaving)
        {
            spawned.Remove(pair.Key);
            idsByHandle?.Remove(pair.Value);
        }
    }
}
