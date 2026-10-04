using System;
using System.Collections.Generic;
using System.Linq;
using Navislamia.Configuration.Options;
using Navislamia.Game.Network.Clients;
using Navislamia.Game.Services.Interfaces;
using Navislamia.Game.Services.Props;

namespace Navislamia.Game.Services.Dungeons;

/// <summary>
/// The secret dungeons' portals (docs/packet-specs/socle-donjons-instances-secrets.md §Portails secrets). A key monster
/// (the Carbuncle of each area, <c>random_respawn.lua</c> entries 1-8) is kept alive in one of its area's boxes and
/// brought back <c>interval</c> after its death; its death runs its <c>script_on_dead</c>,
/// <c>add_field_prop(portal, 600, …)</c>, which opens the portal where it fell for ten minutes
/// (<c>SCRIPT_AddFieldProp</c>, <c>RespawnedFieldPropManager</c>).
/// </summary>
public sealed class SecretPortals
{
    private readonly DungeonCatalog _catalog;
    private readonly MonsterWorldState _monsters;
    private readonly IDynamicFieldProps _props;
    private readonly IFieldPropService _fieldProps;
    private readonly IMonsterSpawnService _monsterSpawn;
    private readonly IPlayerVisibilityService _players;
    private readonly Random _random;
    private readonly object _gate = new();

    /// <summary>Per respawn entry: the living key monsters and the respawns due.</summary>
    private readonly Dictionary<KeyMonsterRespawn, (List<long> Alive, List<(uint Due, long Dead)> Due)> _respawns = new();
    private readonly Dictionary<long, KeyMonsterRespawn> _entryOf = new();

    public SecretPortals(DungeonCatalog catalog, MonsterWorldState monsters, IDynamicFieldProps props,
        IFieldPropService fieldProps, IMonsterSpawnService monsterSpawn, IPlayerVisibilityService players,
        Random random = null)
    {
        _catalog = catalog;
        _monsters = monsters;
        _props = props;
        _fieldProps = fieldProps;
        _monsterSpawn = monsterSpawn;
        _players = players;
        _random = random ?? Random.Shared;
        foreach (var entry in catalog.KeyMonsterRespawns)
            _respawns[entry] = (new List<long>(), new List<(uint, long)>());
    }

    /// <summary>A monster died: a key monster opens its portal and its respawn is pended.</summary>
    public void OnMonsterKilled(long instanceId, uint now)
    {
        if (_monsters is null || !_monsters.TryGetInstance(instanceId, out var monster)) return;

        lock (_gate)
        {
            if (_entryOf.Remove(instanceId, out var entry))
            {
                var state = _respawns[entry];
                state.Alive.Remove(instanceId);
                // The corpse stays until the respawn: combat still reads the instance after this hook.
                state.Due.Add((unchecked(now + entry.IntervalTicks), instanceId));
            }
        }

        if (_props is null || !_catalog.DeathProps.TryGetValue(monster.MonsterId, out var death)
            || !_catalog.PropTemplates.TryGetValue(death.PropId, out var definition)) return;

        var (x, y) = _monsters.GetPosition(instanceId);
        var template = new FieldPropTemplate(definition.Id, definition.ActivateSkillId, 0, definition.MinLevel,
            definition.MaxLevel, 0, 0, PropScript.Parse(definition.Script), Array.Empty<PropActivation>());
        _props.Add(death.PropId, x, y, monster.Layer, template, null, death.ZOffset, death.RotateX, death.RotateY,
            death.RotateZ, death.ScaleX, death.ScaleY, death.ScaleZ,
            expiresAt: unchecked(now + (uint)death.Seconds * 100));
        Refresh(x, y, monster.Layer, props: true);
    }

    /// <summary>The respawns that are due, the missing key monsters, and the portals that closed.</summary>
    public void Tick(uint now)
    {
        List<(float X, float Y)> spawned = new();
        lock (_gate)
        {
            foreach (var (entry, state) in _respawns)
            {
                foreach (var due in state.Due.Where(d => unchecked((int)(now - d.Due)) >= 0).ToArray())
                {
                    state.Due.Remove(due);
                    _monsters?.RemoveDungeonMonsters(new[] { due.Dead });
                }

                var missing = entry.Count - state.Alive.Count - state.Due.Count;
                for (var i = 0; i < missing; i++)
                {
                    if (Spawn(entry) is not { } spawn) break;
                    state.Alive.Add(spawn.Id);
                    _entryOf[spawn.Id] = entry;
                    spawned.Add((spawn.X, spawn.Y));
                }
            }
        }

        foreach (var (x, y) in spawned) Refresh(x, y, 0, props: false);
        foreach (var closed in _props?.RemoveExpired(now) ?? Array.Empty<DynamicFieldProp>())
            Refresh(closed.Instance.X, closed.Instance.Y, closed.Layer, props: true);
    }

    private (long Id, float X, float Y)? Spawn(KeyMonsterRespawn entry)
    {
        if (_monsters is null || entry.Boxes.Length == 0) return null;
        var box = entry.Boxes[_random.Next(entry.Boxes.Length)];
        var x = _random.Next(box.Left, box.Right + 1);
        var y = _random.Next(box.Top, box.Bottom + 1);
        var ids = _monsters.SpawnDungeonMonsters(new[]
        {
            new MonsterSpawnPoint { MonsterId = entry.MonsterId, Count = 1, X = x, Y = y, Radius = 0, Layer = 0, RespawnSeconds = 0 }
        });
        return ids.Count == 0 ? null : (ids[0], x, y);
    }

    /// <summary>The players of the layer who see the spot get what changed there.</summary>
    private void Refresh(float x, float y, byte layer, bool props)
    {
        foreach (var client in _players?.Registry.Clients ?? Array.Empty<GameClient>())
        {
            var info = client.ConnectionInfo;
            if (info.CharacterHandle == 0 || info.Layer != layer
                || CombatRange.Distance(x, y, info.X, info.Y) > WorldVisibility.ViewRange) continue;
            if (props) _fieldProps?.Sync(client);
            else _monsterSpawn?.Sync(client);
        }
    }
}
