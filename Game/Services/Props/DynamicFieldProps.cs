using System;
using System.Collections.Generic;
using System.Linq;
using Navislamia.Game.Network;

namespace Navislamia.Game.Services.Props;

/// <summary>
/// A field prop created at run time on one layer (<c>StructFieldProp::Create(…, layer)</c>): the HuntaHolic healing
/// props, posed in each room's dungeon (docs/packet-specs/socle-huntaholic.md §8). Unlike the world's props, it exists
/// on its layer only and goes away when used.
/// </summary>
public sealed record DynamicFieldProp(FieldPropInstance Instance, byte Layer, FieldPropTemplate Template,
    Action<DynamicFieldProp> Used)
{
    public long InstanceId => Instance.InstanceId;
}

public interface IDynamicFieldProps
{
    /// <summary>Creates a prop; its instance id never collides with a world prop's (their index in the catalogue).</summary>
    DynamicFieldProp Add(int propId, float x, float y, byte layer, FieldPropTemplate template,
        Action<DynamicFieldProp> used = null, float zOffset = 0, float rotateX = 0, float rotateY = 0,
        float rotateZ = 0, float scaleX = 1, float scaleY = 1, float scaleZ = 1);

    /// <summary>Takes the prop out of the world without using it. False when it was already gone.</summary>
    bool Remove(long instanceId);

    bool TryGet(long instanceId, out DynamicFieldProp prop);

    /// <summary>
    /// <c>StructFieldProp::UseProp</c> for a single-use prop (<c>use_count</c> 1): the prop leaves the world and its
    /// owner is told (the HuntaHolic room pends its respawn). False when someone used it first.
    /// </summary>
    bool TryUse(long instanceId, out DynamicFieldProp prop);

    /// <summary>The props of <paramref name="layer"/> within <paramref name="range"/> of a point.</summary>
    IReadOnlyList<FieldPropInstance> Within(float x, float y, byte layer, float range);
}

public sealed class DynamicFieldProps : IDynamicFieldProps
{
    /// <summary>Far above the world props' indexes (3 189 today).</summary>
    public const long FirstInstanceId = 1L << 40;

    private readonly object _gate = new();
    private readonly Dictionary<long, DynamicFieldProp> _props = new();
    private long _nextInstanceId = FirstInstanceId;

    public DynamicFieldProp Add(int propId, float x, float y, byte layer, FieldPropTemplate template,
        Action<DynamicFieldProp> used = null, float zOffset = 0, float rotateX = 0, float rotateY = 0,
        float rotateZ = 0, float scaleX = 1, float scaleY = 1, float scaleZ = 1)
    {
        lock (_gate)
        {
            var instance = new FieldPropInstance(_nextInstanceId++, propId, x, y, zOffset, rotateX, rotateY, rotateZ,
                scaleX, scaleY, scaleZ);
            var prop = new DynamicFieldProp(instance, layer, template, used);
            _props[instance.InstanceId] = prop;
            return prop;
        }
    }

    public bool Remove(long instanceId)
    {
        lock (_gate)
        {
            return _props.Remove(instanceId);
        }
    }

    public bool TryGet(long instanceId, out DynamicFieldProp prop)
    {
        lock (_gate)
        {
            return _props.TryGetValue(instanceId, out prop);
        }
    }

    public bool TryUse(long instanceId, out DynamicFieldProp prop)
    {
        lock (_gate)
        {
            if (!_props.Remove(instanceId, out prop))
            {
                return false;
            }
        }

        prop.Used?.Invoke(prop);
        return true;
    }

    public IReadOnlyList<FieldPropInstance> Within(float x, float y, byte layer, float range)
    {
        lock (_gate)
        {
            if (_props.Count == 0) return Array.Empty<FieldPropInstance>();
            return _props.Values
                .Where(prop => prop.Layer == layer && CombatRange.Distance(x, y, prop.Instance.X, prop.Instance.Y) <= range)
                .Select(prop => prop.Instance)
                .ToArray();
        }
    }
}
