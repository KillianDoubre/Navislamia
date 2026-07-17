using Navislamia.Game.Network.Clients;

namespace Navislamia.Game.Services.Props;

/// <summary>
/// The reference server's FieldProp::IsUsable, with one deliberate divergence: see
/// <see cref="RaceAllows"/>.
/// </summary>
public static class FieldPropUsage
{
    private const int LimitDeva = 0x4;
    private const int LimitAsura = 0x8;
    private const int LimitGaia = 0x10;
    private const int LimitAnyRace = LimitDeva | LimitAsura | LimitGaia;

    private const int RaceGaia = 3;
    private const int RaceDeva = 4;
    private const int RaceAsura = 5;

    private const int ConditionItem = 1;
    private const int ConditionQuest = 2;
    private const int ConditionSkill = 3;
    private const int ConditionWorn = 4;

    public static bool IsUsable(FieldPropTemplate template, ConnectionInfo info)
    {
        if (template.MinLevel > 0 && info.CharacterLevel < template.MinLevel)
        {
            return false;
        }

        if (template.MaxLevel > 0 && info.CharacterLevel > template.MaxLevel)
        {
            return false;
        }

        if (!RaceAllows(template.Limit, info.CharacterRace))
        {
            return false;
        }

        if (template.LimitJobId != 0 && template.LimitJobId != info.CharacterJob)
        {
            return false;
        }

        foreach (var activation in template.Activations)
        {
            // Quests, item counts, learned skill levels and worn items gate the props that need
            // them. Only the skill condition is checkable today, so anything else refuses rather
            // than silently letting a gated prop through.
            if (activation.Condition == ConditionSkill)
            {
                if (!info.LearnedSkills.TryGetValue(activation.Value1, out var level)
                    || level != activation.Value2)
                {
                    return false;
                }

                continue;
            }

            if (activation.Condition is ConditionItem or ConditionQuest or ConditionWorn)
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Whether the prop's script resolves to something this server can carry out. A prop with an
    /// unsupported script still streams and is visible; it just refuses activation.
    /// </summary>
    public static bool CanAct(FieldPropTemplate template, IFieldPropCatalog catalog)
    {
        return template.Action.Kind switch
        {
            PropActionKind.CommonWarpGate or PropActionKind.RunTeleport => true,
            PropActionKind.EnterDungeon or PropActionKind.ExitDungeon =>
                catalog.TryGetDungeonStart(template.Action.DungeonId, out _, out _),
            _ => false
        };
    }

    /// <summary>
    /// A set race bit means that race MAY use the prop, so the player's race has to be among them.
    /// </summary>
    /// <remarks>
    /// This deliberately does not reproduce the reference server, which reads the bits as one
    /// exclusion per race (<c>race != GAIA &amp;&amp; (limit &amp; LIMIT_GAIA)</c> refuses). Against this
    /// data that logic refuses everyone: all 454 spawned props carry <c>limit = 15388</c>, every race
    /// and every class bit set, so at least two of its three tests fail whatever the race. A field
    /// that makes every prop in the world unusable is not describing an exclusion; the only reading
    /// the data supports is an allow-list, under which all-bits-set means everyone. The field
    /// therefore discriminates nothing here — it is kept because a future data set may use it.
    /// </remarks>
    private static bool RaceAllows(int limit, int race)
    {
        var allowed = limit & LimitAnyRace;
        if (allowed == 0)
        {
            return true;
        }

        var bit = race switch
        {
            RaceGaia => LimitGaia,
            RaceDeva => LimitDeva,
            RaceAsura => LimitAsura,
            _ => 0
        };

        return bit != 0 && (allowed & bit) != 0;
    }
}
