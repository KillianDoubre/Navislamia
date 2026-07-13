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
                    var spawned = info.SpawnedMonsters.ContainsKey(monster.InstanceId);

                    if (dead.Contains(monster.InstanceId))
                    {
                        if (spawned)
                        {
                            inRangeIds.Add(monster.InstanceId);
                        }

                        continue;
                    }

                    inRangeIds.Add(monster.InstanceId);

                    if (spawned)
                    {
                        continue;
                    }

                    var handle = WorldObjectHandle.Next();
                    var (x, y) = _worldState.GetPosition(monster.InstanceId);
                    client.Connection.Send(GameSpawnPackets.BuildEnterMonster(handle, x, y, monster.Z,
                        info.Layer, _worldState.GetHp(monster.InstanceId), monster.Level, monster.Race,
                        monster.MonsterId, monster.FaceDirection));
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
