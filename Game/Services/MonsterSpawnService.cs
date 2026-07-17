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
            var dead = _worldState.GetDeadInstances();

            WorldObjectStreamer.Stream(client, info.MonsterVisibilityLock, inRange,
                monster => monster.InstanceId,
                (monster, handle) =>
                {
                    // Only read for a monster that is actually entering, so the state lock is not
                    // touched once per visible monster per sync.
                    var (x, y) = _worldState.GetPosition(monster.InstanceId);
                    return GameSpawnPackets.BuildEnterMonster(handle, x, y, monster.Z, info.Layer,
                        _worldState.GetHp(monster.InstanceId), monster.Level, monster.Race,
                        monster.MonsterId, monster.FaceDirection);
                },
                info.SpawnedMonsters,
                canEnter: monster => !dead.Contains(monster.InstanceId));
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "{clientTag} monster sync failed", client.ClientTag);
        }
    }
}
