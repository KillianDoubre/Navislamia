using System;
using System.Collections.Generic;
using Navislamia.Configuration.Options;
using Navislamia.Game.DataAccess.Entities.Arcadia;

namespace Navislamia.Game.Services;

public static class MonsterInstanceFactory
{
    public static IReadOnlyList<MonsterInstance> Build(MonsterSpawnOptions options,
        IReadOnlyList<MonsterResourceEntity> resources)
    {
        var instances = new List<MonsterInstance>(GetInstanceCount(options));
        var resourcesById = IndexResources(resources);
        long instanceId = 0;

        foreach (var spawn in options.Spawns)
        {
            AddInstances(instances, resourcesById, ref instanceId,
                spawn.MonsterId, spawn.ResourceId ?? spawn.MonsterId, spawn.Count,
                spawn.X - spawn.Radius, spawn.Y - spawn.Radius,
                spawn.X + spawn.Radius, spawn.Y + spawn.Radius);
        }

        foreach (var area in options.Areas)
        {
            foreach (var population in area.Monsters)
            {
                AddInstances(instances, resourcesById, ref instanceId,
                    population.ResourceId, population.ResourceId, population.Count,
                    area.Left, area.Top, area.Right, area.Bottom);
            }
        }

        return instances;
    }

    public static IReadOnlyList<MonsterInstance> Build(IEnumerable<MonsterSpawnPoint> spawns,
        IReadOnlyList<MonsterResourceEntity> resources)
    {
        var instances = new List<MonsterInstance>();
        var resourcesById = IndexResources(resources);
        long instanceId = 0;

        foreach (var spawn in spawns)
        {
            AddInstances(instances, resourcesById, ref instanceId,
                spawn.MonsterId, spawn.ResourceId ?? spawn.MonsterId, spawn.Count,
                spawn.X - spawn.Radius, spawn.Y - spawn.Radius,
                spawn.X + spawn.Radius, spawn.Y + spawn.Radius);
        }

        return instances;
    }

    public static IReadOnlyCollection<int> GetRequiredResourceIds(MonsterSpawnOptions options)
    {
        var ids = new HashSet<int>();

        foreach (var spawn in options.Spawns)
        {
            ids.Add(spawn.ResourceId ?? spawn.MonsterId);
        }

        foreach (var area in options.Areas)
        {
            foreach (var population in area.Monsters)
            {
                ids.Add(population.ResourceId);
            }
        }

        return ids;
    }

    private static Dictionary<int, MonsterResourceEntity> IndexResources(
        IReadOnlyList<MonsterResourceEntity> resources)
    {
        var resourcesById = new Dictionary<int, MonsterResourceEntity>(resources.Count);

        foreach (var resource in resources)
        {
            resourcesById[(int)resource.Id] = resource;
        }

        return resourcesById;
    }

    private static void AddInstances(List<MonsterInstance> instances,
        IReadOnlyDictionary<int, MonsterResourceEntity> resourcesById, ref long instanceId,
        int monsterId, int resourceId, int count, int x1, int y1, int x2, int y2)
    {
        if (count <= 0 || !resourcesById.TryGetValue(resourceId, out var resource))
        {
            return;
        }

        var race = resource.Race is >= 0 and <= byte.MaxValue ? (byte)resource.Race : (byte)0;
        var left = Math.Min(x1, x2);
        var right = Math.Max(x1, x2);
        var top = Math.Min(y1, y2);
        var bottom = Math.Max(y1, y2);
        var random = new Random(HashCode.Combine(monsterId, resourceId, left, top, right, bottom));

        for (var i = 0; i < count; i++)
        {
            var faceDirection = (float)(random.NextDouble() * Math.PI * 2);
            instances.Add(new MonsterInstance(
                instanceId++, monsterId,
                random.Next(left, right + 1), random.Next(top, bottom + 1), 0f,
                resource.Level, resource.Hp, race, faceDirection,
                resource.FirstAttack != 0, resource.VisibleRange, resource.ChaseRange,
                (float)resource.AttackRange, (float)resource.Size, (float)resource.Scale));
        }
    }

    private static int GetInstanceCount(MonsterSpawnOptions options)
    {
        long count = 0;

        foreach (var spawn in options.Spawns)
        {
            count += Math.Max(0, spawn.Count);
        }

        foreach (var area in options.Areas)
        {
            foreach (var population in area.Monsters)
            {
                count += Math.Max(0, population.Count);
            }
        }

        return checked((int)count);
    }
}
