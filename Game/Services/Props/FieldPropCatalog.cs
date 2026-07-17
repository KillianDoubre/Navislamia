using System;
using System.Collections.Frozen;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Extensions.Options;
using Navislamia.Configuration.Options;
using Serilog;

namespace Navislamia.Game.Services.Props;

public readonly record struct PropActivation(int Condition, int Value1, int Value2);

public readonly record struct FieldPropTemplate(
    int Id,
    int ActivateSkillId,
    int CastingTime,
    int MinLevel,
    int MaxLevel,
    int Limit,
    int LimitJobId,
    PropAction Action,
    PropActivation[] Activations);

public readonly record struct FieldPropInstance(
    long InstanceId,
    int PropId,
    float X,
    float Y,
    float ZOffset,
    float RotateX,
    float RotateY,
    float RotateZ,
    float ScaleX,
    float ScaleY,
    float ScaleZ);

public interface IFieldPropCatalog
{
    /// <summary>
    /// Every prop in the world. A prop's <c>InstanceId</c> is its index here, which is what lets
    /// <c>FieldPropService</c> resolve one in O(1) without a second dictionary.
    /// </summary>
    IReadOnlyList<FieldPropInstance> Instances { get; }
    bool TryGetInstance(long instanceId, out FieldPropInstance instance);
    bool TryGetTemplate(int propId, out FieldPropTemplate template);
    bool TryGetDungeonStart(int dungeonId, out int x, out int y);
}

/// <summary>
/// The field props of the world, frozen at startup like the monster drop catalog. Positions come
/// from the Epic 7.3 client's own map files through tools/Export-FieldProps; the database has none.
/// </summary>
public class FieldPropCatalog : IFieldPropCatalog
{
    private static readonly PropActivation[] NoActivations = Array.Empty<PropActivation>();

    private readonly ILogger _logger = Log.ForContext<FieldPropCatalog>();
    private readonly FrozenDictionary<int, FieldPropTemplate> _templates;
    private readonly FrozenDictionary<int, (int X, int Y)> _dungeons;

    public IReadOnlyList<FieldPropInstance> Instances { get; }

    public FieldPropCatalog(IOptions<FieldPropOptions> options) : this(options.Value)
    {
    }

    public FieldPropCatalog(FieldPropOptions options)
    {
        _templates = options.Templates.ToFrozenDictionary(
            template => template.Id,
            template => new FieldPropTemplate(
                template.Id,
                template.ActivateSkillId,
                template.CastingTime,
                template.MinLevel,
                template.MaxLevel,
                template.Limit,
                template.LimitJobId,
                PropScript.Parse(template.Script),
                template.Activations.Count == 0
                    ? NoActivations
                    : template.Activations
                        .Select(activation => new PropActivation(
                            activation.Condition, activation.Value1, activation.Value2))
                        .ToArray()));

        _dungeons = options.Dungeons.ToFrozenDictionary(
            dungeon => dungeon.Id,
            dungeon => (dungeon.X, dungeon.Y));

        Instances = options.Spawns
            .Where(spawn => _templates.ContainsKey(spawn.PropId))
            .Select((spawn, index) => new FieldPropInstance(
                index,
                spawn.PropId,
                spawn.X,
                spawn.Y,
                spawn.ZOffset,
                spawn.RotateX,
                spawn.RotateY,
                spawn.RotateZ,
                spawn.ScaleX,
                spawn.ScaleY,
                spawn.ScaleZ))
            .ToArray();

        var usable = Instances.Count(instance =>
            _templates[instance.PropId].Action.Kind != PropActionKind.None);

        _logger.Debug("Loaded {props} field props over {templates} templates, {usable} teleporting",
            Instances.Count, _templates.Count, usable);
    }

    public bool TryGetInstance(long instanceId, out FieldPropInstance instance)
    {
        if (instanceId >= 0 && instanceId < Instances.Count)
        {
            instance = Instances[(int)instanceId];
            return true;
        }

        instance = default;
        return false;
    }

    public bool TryGetTemplate(int propId, out FieldPropTemplate template) =>
        _templates.TryGetValue(propId, out template);

    public bool TryGetDungeonStart(int dungeonId, out int x, out int y)
    {
        if (_dungeons.TryGetValue(dungeonId, out var position))
        {
            (x, y) = position;
            return true;
        }

        x = 0;
        y = 0;
        return false;
    }
}
