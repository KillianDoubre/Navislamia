using System;
using System.Collections.Generic;
using Navislamia.Game.Network;
using Navislamia.Game.Network.Clients;
using Navislamia.Game.Network.Packets.Game;
using Serilog;

namespace Navislamia.Game.Services;

public class MonsterSpawnService : IMonsterSpawnService
{
    private readonly ILogger _logger = Log.ForContext<MonsterSpawnService>();
    private readonly MonsterWorldState _worldState;

    public MonsterSpawnService(MonsterWorldState worldState)
    {
        _worldState = worldState;
    }

    public void Sync(GameClient client)
    {
        try
        {
            var info = client.ConnectionInfo;
            var inRange = _worldState.WithinRange(info.X, info.Y, WorldVisibility.ViewRange);
            var inRangeIds = new HashSet<long>(inRange.Count);

            lock (info.MonsterVisibilityLock)
            {
                var dead = _worldState.GetDeadInstances();

                foreach (var monster in inRange)
                {
                    if (dead.Contains(monster.InstanceId))
                    {
                        continue;
                    }

                    inRangeIds.Add(monster.InstanceId);

                    if (info.SpawnedMonsters.ContainsKey(monster.InstanceId))
                    {
                        continue;
                    }

                    var handle = WorldObjectHandle.Next();
                    client.Connection.Send(GameSpawnPackets.BuildEnterMonster(handle, monster.X, monster.Y, monster.Z,
                        info.Layer, _worldState.GetHp(monster.InstanceId), monster.Level, monster.Race, monster.MonsterId));
                    info.SpawnedMonsters[monster.InstanceId] = handle;
                }

                SpawnedObjectSet.DespawnMissing(client.Connection, info.SpawnedMonsters, inRangeIds);
            }
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "{clientTag} monster sync failed", client.ClientTag);
        }
    }
}
