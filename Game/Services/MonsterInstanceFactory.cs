using System;
using System.Collections.Generic;
using Navislamia.Configuration.Options;
using Navislamia.Game.DataAccess.Entities.Arcadia;
using Navislamia.Game.Services.Stats;

namespace Navislamia.Game.Services;

public static class MonsterInstanceFactory
{
    public static IReadOnlyList<MonsterInstance> Build(MonsterSpawnOptions options,
        IReadOnlyList<MonsterResourceEntity> resources, Func<int, StatBaseStats?> baseStats = null,
        Func<float, float, bool> isBlocked = null)
    {
        var instances = new List<MonsterInstance>(GetInstanceCount(options));
        var resourcesById = IndexResources(resources, baseStats, options.UseSecondaryRewards);
        long instanceId = 0;

        foreach (var spawn in options.Spawns)
        {
            AddInstances(instances, resourcesById, ref instanceId,
                spawn.MonsterId, spawn.ResourceId ?? spawn.MonsterId, spawn.Count,
                spawn.X - spawn.Radius, spawn.Y - spawn.Radius,
                spawn.X + spawn.Radius, spawn.Y + spawn.Radius, isBlocked, spawn.Layer, spawn.IsDungeonRaidMonster);
        }

        foreach (var area in options.Areas)
        {
            foreach (var population in area.Monsters)
            {
                if (!Stands(options, population))
                {
                    continue;
                }

                AddInstances(instances, resourcesById, ref instanceId,
                    population.ResourceId, population.ResourceId, population.Count,
                    area.Left, area.Top, area.Right, area.Bottom, isBlocked, area.Layer, area.IsDungeonRaidMonster);
            }
        }

        return instances;
    }

    public static IReadOnlyList<MonsterInstance> Build(IEnumerable<MonsterSpawnPoint> spawns,
        IReadOnlyList<MonsterResourceEntity> resources, Func<int, StatBaseStats?> baseStats = null,
        Func<float, float, bool> isBlocked = null, bool useSecondaryRewards = false)
    {
        var instances = new List<MonsterInstance>();
        var resourcesById = IndexResources(resources, baseStats, useSecondaryRewards);
        long instanceId = 0;

        foreach (var spawn in spawns)
        {
            AddInstances(instances, resourcesById, ref instanceId,
                spawn.MonsterId, spawn.ResourceId ?? spawn.MonsterId, spawn.Count,
                spawn.X - spawn.Radius, spawn.Y - spawn.Radius,
                spawn.X + spawn.Radius, spawn.Y + spawn.Radius, isBlocked, spawn.Layer, spawn.IsDungeonRaidMonster);
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
                if (Stands(options, population))
                {
                    ids.Add(population.ResourceId);
                }
            }
        }

        return ids;
    }

    /// <summary>
    /// Indexes the resources with their combat stats, built once per resource. Without a base-stat source
    /// (the tests) a resource gets no <c>StatResource</c> row, so only its level and columns count.
    /// </summary>
    private static Dictionary<int, (MonsterResourceEntity Resource, MonsterCombatStats Combat, MonsterRewardProfile Rewards)> IndexResources(
        IReadOnlyList<MonsterResourceEntity> resources, Func<int, StatBaseStats?> baseStats, bool useSecondaryRewards)
    {
        var resourcesById = new Dictionary<int, (MonsterResourceEntity, MonsterCombatStats, MonsterRewardProfile)>(resources.Count);

        foreach (var resource in resources)
        {
            var combat = MonsterCombatStats.From(resource, baseStats?.Invoke(resource.StatId));
            resourcesById[(int)resource.Id] = (resource, combat, MonsterRewardProfile.From(resource, useSecondaryRewards));
        }

        return resourcesById;
    }

    private static void AddInstances(List<MonsterInstance> instances,
        IReadOnlyDictionary<int, (MonsterResourceEntity Resource, MonsterCombatStats Combat, MonsterRewardProfile Rewards)> resourcesById,
        ref long instanceId, int monsterId, int resourceId, int count, int x1, int y1, int x2, int y2,
        Func<float, float, bool> isBlocked, byte layer = 0, bool raid = false)
    {
        if (count <= 0 || !resourcesById.TryGetValue(resourceId, out var entry))
        {
            return;
        }

        var (resource, combat, rewards) = entry;
        if (raid) combat = combat.AsRaid();
        var race = resource.Race is >= 0 and <= byte.MaxValue ? (byte)resource.Race : (byte)0;
        var left = Math.Min(x1, x2);
        var right = Math.Max(x1, x2);
        var top = Math.Min(y1, y2);
        var bottom = Math.Max(y1, y2);
        var random = new Random(HashCode.Combine(monsterId, resourceId, left, top, right, bottom));

        for (var i = 0; i < count; i++)
        {
            var faceDirection = (float)(random.NextDouble() * Math.PI * 2);
            var (x, y) = SpawnPoint(random, left, top, right, bottom, isBlocked);
            instances.Add(new MonsterInstance(
                instanceId++, monsterId,
                x, y, 0f,
                resource.Level, combat.MaxHp, race, faceDirection,
                resource.FirstAttack != 0, resource.VisibleRange, resource.ChaseRange,
                (float)resource.AttackRange, (float)resource.Size, (float)resource.Scale,
                resource.TamingId, resource.TamingPercentage, combat, resource.MonsterSkillLinkId,
                resource.MonsterGroup, resource.GroupFirstAttack != 0, Layer: layer, Rewards: rewards,
                IsDungeonRaidMonster: raid, MonsterType: resource.MonsterType, CreatureGroup: resource.Grp));
        }
    }

    /// <summary>Tries a spawn point this many times before keeping a blocked one.</summary>
    public const int SpawnPointAttempts = 16;

    /// <summary>
    /// A random point of the area outside the obstacles: the spawn areas come from the 9.4 data and half
    /// their centres lie in an obstacle of the 7.3 maps, where a monster could neither wander nor be
    /// reached. After <see cref="SpawnPointAttempts"/> blocked draws the last one is kept (an area entirely
    /// inside an obstacle still spawns its monsters).
    /// </summary>
    private static (int X, int Y) SpawnPoint(Random random, int left, int top, int right, int bottom,
        Func<float, float, bool> isBlocked)
    {
        var (x, y) = (random.Next(left, right + 1), random.Next(top, bottom + 1));
        for (var attempt = 1; isBlocked is not null && attempt < SpawnPointAttempts && isBlocked(x, y); attempt++)
        {
            (x, y) = (random.Next(left, right + 1), random.Next(top, bottom + 1));
        }

        return (x, y);
    }

    /// <summary>
    /// <c>monster_respawn.lua</c>: a rare monster below 310000 whose id ends in 41, 43, 44, 46 or 49 is an anti-bot
    /// "Auto Trap" (attacking it brings the 5997 punishment and <c>set_auto_user</c>, <c>ETC_run_monster_skill.lua</c>),
    /// respawned only when <c>game.use_auto_trap</c> is 1. These ids are only ever rare monsters in the Lua.
    /// </summary>
    public static bool IsAutoTrap(int monsterId) =>
        monsterId is > 0 and < 310000 && (monsterId % 100) is 41 or 43 or 44 or 46 or 49;

    private static bool Stands(MonsterSpawnOptions options, MonsterSpawnPopulation population) =>
        options.UseAutoTrap || !IsAutoTrap(population.ResourceId);

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
                if (Stands(options, population))
                {
                    count += Math.Max(0, population.Count);
                }
            }
        }

        return checked((int)count);
    }
}
