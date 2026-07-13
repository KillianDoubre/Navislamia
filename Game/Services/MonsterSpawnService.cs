using System;
using System.Collections.Generic;
using Microsoft.Extensions.Options;
using Navislamia.Configuration.Options;
using Navislamia.Game.DataAccess.Repositories.Interfaces;
using Navislamia.Game.Network;
using Navislamia.Game.Network.Clients;
using Navislamia.Game.Network.Packets.Game;
using Serilog;

namespace Navislamia.Game.Services;

public class MonsterSpawnService : IMonsterSpawnService
{
    private readonly ILogger _logger = Log.ForContext<MonsterSpawnService>();
    private readonly IMonsterResourceRepository _repository;
    private readonly MonsterSpawnOptions _options;
    private readonly object _lock = new();
    private SpatialIndex<MonsterInstance> _index;

    public MonsterSpawnService(IMonsterResourceRepository repository, IOptions<MonsterSpawnOptions> options)
    {
        _repository = repository;
        _options = options.Value;
        Load();
    }

    private void Load()
    {
        try
        {
            var resourceIds = MonsterInstanceFactory.GetRequiredResourceIds(_options);
            var resources = _repository.GetByIds(resourceIds);
            var instances = MonsterInstanceFactory.Build(_options, resources);
            _index = new SpatialIndex<MonsterInstance>(instances, monster => monster.X, monster => monster.Y,
                WorldVisibility.ViewRange);
            _logger.Information("Loaded {instances} monster instances from {points} spawn points and {areas} official areas ({resources} monster resources)",
                _index.Count, _options.Spawns.Count, _options.Areas.Count, resources.Count);
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to load monsters at startup; will retry on first sync");
        }
    }

    public void Sync(GameClient client)
    {
        try
        {
            var info = client.ConnectionInfo;
            var inRange = GetInRange(info.X, info.Y);

            var inRangeIds = new HashSet<long>(inRange.Count);

            foreach (var monster in inRange)
            {
                inRangeIds.Add(monster.InstanceId);

                if (info.SpawnedMonsters.ContainsKey(monster.InstanceId))
                {
                    continue;
                }

                var handle = WorldObjectHandle.Next();
                client.Connection.Send(GameSpawnPackets.BuildEnterMonster(handle, monster.X, monster.Y, monster.Z,
                    info.Layer, monster.Hp, monster.Level, monster.Race, monster.MonsterId));
                info.SpawnedMonsters[monster.InstanceId] = handle;
            }

            SpawnedObjectSet.DespawnMissing(client.Connection, info.SpawnedMonsters, inRangeIds);
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "{clientTag} monster sync failed", client.ClientTag);
        }
    }

    private IReadOnlyList<MonsterInstance> GetInRange(float x, float y)
    {
        return GetIndex()?.WithinRange(x, y, WorldVisibility.ViewRange)
            ?? Array.Empty<MonsterInstance>();
    }

    private SpatialIndex<MonsterInstance> GetIndex()
    {
        if (_index != null)
        {
            return _index;
        }

        lock (_lock)
        {
            if (_index == null)
            {
                Load();
            }
        }

        return _index;
    }
}
