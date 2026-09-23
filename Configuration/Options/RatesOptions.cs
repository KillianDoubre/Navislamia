namespace Navislamia.Configuration.Options;

/// <summary>
/// The <c>Rates</c> section of the game server's settings, read through <c>IOptionsMonitor</c> so an edit
/// of the file applies without a restart. Every multiplier defaults to 1, the authentic rate; a key is only
/// declared here once something in the server actually reads it (docs/gm-commands.md, <c>/rate</c>).
/// </summary>
public class RatesOptions
{
    /// <summary>Experience per kill. NGemity <c>Game.EXPRate</c>.</summary>
    public double Exp { get; set; } = 1;

    /// <summary>
    /// JP per kill. NGemity's <c>EXPRate</c> multiplies both; here the two are separate and an unset
    /// <c>Jp</c> follows <see cref="Exp"/>, which is NGemity's behaviour.
    /// </summary>
    public double? Jp { get; set; }

    /// <summary>Gold per kill. NGemity <c>Game.GoldDropRate</c>.</summary>
    public double Gold { get; set; } = 1;

    /// <summary>Chance of every drop slot, capped at 100 %. NGemity <c>Game.ItemDropRate</c>.</summary>
    public double ItemDrop { get; set; } = 1;

    /// <summary>
    /// Extra factor on a slot whose direct item is a summon card (group 13), on top of
    /// <see cref="ItemDrop"/>. NGemity <c>Game.CreatureCardDropRate</c>.
    /// </summary>
    public double CreatureCardDrop { get; set; } = 1;

    /// <summary>Delay before a killed monster respawns.</summary>
    public int MonsterRespawnSeconds { get; set; } = 10;

    /// <summary>How long an item stays on the ground before it disappears.</summary>
    public int GroundItemLifetimeSeconds { get; set; } = 120;

    /// <summary>Factor on the JP cost of a skill level.</summary>
    public double SkillJpCost { get; set; } = 1;

    /// <summary>Factor on the JP cost of a job level.</summary>
    public double JobLevelJpCost { get; set; } = 1;

    /// <summary>The largest multiplier <c>/rate</c> accepts for an event.</summary>
    public double MaxEventMultiplier { get; set; } = 100;

    /// <summary>Minutes before the end of an event at which a reminder is announced; 0 disables it.</summary>
    public int EventReminderMinutes { get; set; } = 5;

    /// <summary>
    /// Where the running <c>/rate</c> events are kept, so a restart resumes them with their remaining time.
    /// A relative path is resolved against the server's content root.
    /// </summary>
    public string EventStatePath { get; set; } = "rate-events.json";
}
