using System.Collections.Frozen;
using System.Collections.Generic;
using System.Linq;

namespace Navislamia.Game.Services.GmCommands;

public enum GmCommand
{
    Help,
    Hold,
    Titles,
    Title,
    Position,
    Sitdown,
    Standup,
    Battle,
    Walk,
    KillAll,
    Notice,
    Warp,
    Item,
    Gold,
    Level,
    Heal,
    Die,
    Exp,
    Jp,
    JobLevel,
    Learn,
    Buff,
    Immortal,
    Pk,
    Home,
    Target,
    Save,
    Chaos,
    Rate,
    Rates,
    GameTime,
    Ride,
    Unride,
    Speed,
    SubTitle, ChangeName, BlockChat, CheckAutoUser, ForceWarp, Invisible, Kick, Rebirth, Lv,
    Job, Race, MaxSkills
}

/// <summary>
/// One command: its chat name, whether it needs <see cref="GmCommandRules.GmPermission"/>, and the usage
/// line <c>/help</c> prints. <see cref="Origin"/> says where the command comes from, so nobody mistakes an
/// addition of this repository for a port.
/// </summary>
public sealed record GmCommandDefinition(GmCommand Command, string Name, bool Privileged, string Usage,
    string Origin);

/// <summary>
/// The commands the server answers. NGemity's table (<c>AllowedCommandInfo.cpp:34-39</c>) is the model for
/// the unprivileged half and for the permission rule; the privileged toolbox reuses the effect of the
/// reference's Lua functions (<c>warp</c>, <c>insert_item</c>, <c>insert_gold</c>...) as plain commands,
/// because this repository resolves scripts as catalogue lookups and never executes Lua.
/// </summary>
public static class GmCommandCatalog
{
    public const string FromNgemity = "NGemity";
    public const string FromLua = "NGemity Lua";
    public const string FromRepository = "Navislamia";
    public const string FromOfficial = "Official server (GameMessage.cpp command table)";

    public static IReadOnlyList<GmCommandDefinition> All { get; } = new[]
    {
        new GmCommandDefinition(GmCommand.ChangeName, "change_name", false, "/change_name <name>", FromOfficial),
        new GmCommandDefinition(GmCommand.BlockChat, "block_chat", true, "/block_chat <name> [minutes 0-144000]", FromOfficial),
        new GmCommandDefinition(GmCommand.CheckAutoUser, "check_auto_user", true, "/check_auto_user <name>", FromOfficial),
        new GmCommandDefinition(GmCommand.ForceWarp, "force_warp", true, "/force_warp <name> | <x> <y> [name]", FromOfficial),
        new GmCommandDefinition(GmCommand.Invisible, "invisible", true, "/invisible [1|2]", FromOfficial),
        new GmCommandDefinition(GmCommand.Kick, "kick", true, "/kick <name>", FromOfficial),
        new GmCommandDefinition(GmCommand.Rebirth, "rebirth", true, "/rebirth", FromOfficial),
        new GmCommandDefinition(GmCommand.Lv, "lv", true, "/lv <level>", FromOfficial),
        new GmCommandDefinition(GmCommand.Help, "help", false, "/help", FromRepository),
        new GmCommandDefinition(GmCommand.Hold, "hold", false, "/hold [summon handle] [on|off]", FromRepository),
        new GmCommandDefinition(GmCommand.Titles, "titles", false, "/titles", FromRepository),
        new GmCommandDefinition(GmCommand.Title, "title", false, "/title <id|0>", FromRepository),
        new GmCommandDefinition(GmCommand.SubTitle, "subtitle", false, "/subtitle <slot 1-5> <id|0>", FromOfficial),
        new GmCommandDefinition(GmCommand.Position, "position", false, "/position", FromNgemity),
        new GmCommandDefinition(GmCommand.Sitdown, "sitdown", false, "/sitdown", FromNgemity),
        new GmCommandDefinition(GmCommand.Ride, "ride", false, "/ride <summon handle>", FromOfficial),
        new GmCommandDefinition(GmCommand.Unride, "unride", false, "/unride", FromOfficial),
        new GmCommandDefinition(GmCommand.Standup, "standup", false, "/standup", FromNgemity),
        new GmCommandDefinition(GmCommand.Battle, "battle", false, "/battle [on|off]", FromNgemity),
        new GmCommandDefinition(GmCommand.Walk, "walk", false, "/walk [on|off]", FromNgemity),
        new GmCommandDefinition(GmCommand.KillAll, "doit", true, "/doit", FromNgemity),
        new GmCommandDefinition(GmCommand.Notice, "notice", true, "/notice <text>", FromRepository),
        new GmCommandDefinition(GmCommand.Warp, "warp", true, "/warp <x> <y>", FromLua),
        new GmCommandDefinition(GmCommand.Item, "item", true, "/item <code> [count]", FromLua),
        new GmCommandDefinition(GmCommand.Gold, "gold", true, "/gold <amount>", FromLua),
        new GmCommandDefinition(GmCommand.Level, "level", true, "/level <level>", FromRepository),
        new GmCommandDefinition(GmCommand.Heal, "heal", true, "/heal", FromRepository),
        new GmCommandDefinition(GmCommand.Die, "die", true, "/die", FromRepository),
        new GmCommandDefinition(GmCommand.Exp, "exp", true, "/exp <amount>", FromRepository),
        new GmCommandDefinition(GmCommand.Jp, "jp", true, "/jp <amount>", FromRepository),
        new GmCommandDefinition(GmCommand.JobLevel, "joblevel", true, "/joblevel <level>", FromRepository),
        new GmCommandDefinition(GmCommand.Job, "job", true, "/job <job id>", FromRepository),
        new GmCommandDefinition(GmCommand.Race, "race", true, "/race <deva|asura|gaia>", FromRepository),
        new GmCommandDefinition(GmCommand.MaxSkills, "maxskills", true, "/maxskills", FromRepository),
        new GmCommandDefinition(GmCommand.Learn, "learn", true, "/learn <skill> [level]", FromLua),
        new GmCommandDefinition(GmCommand.Buff, "buff", true, "/buff <state> [level] [seconds]", FromLua),
        new GmCommandDefinition(GmCommand.Immortal, "immortal", true, "/immortal [on|off]", FromRepository),
        new GmCommandDefinition(GmCommand.Speed, "speed", true, "/speed [move speed, 120 base | nothing to reset]",
            FromRepository),
        new GmCommandDefinition(GmCommand.Pk, "pk", true, "/pk [on|off]", FromRepository),
        new GmCommandDefinition(GmCommand.Home, "home", true, "/home", FromRepository),
        new GmCommandDefinition(GmCommand.Target, "target", true, "/target", FromRepository),
        new GmCommandDefinition(GmCommand.Save, "save", true, "/save", FromLua),
        new GmCommandDefinition(GmCommand.Chaos, "chaos", true, "/chaos <amount>", FromRepository),
        new GmCommandDefinition(GmCommand.Rate, "rate", true,
            "/rate [<exp|jp|gold|drop|card|all> <multiplier> <duration> | reset [type]]", FromRepository),
        new GmCommandDefinition(GmCommand.Rates, "rates", false, "/rates", FromRepository),
        new GmCommandDefinition(GmCommand.GameTime, "gametime", true, "/gametime [hours offset|0]", FromRepository)
    };

    private static readonly FrozenDictionary<string, GmCommandDefinition> ByName =
        All.ToFrozenDictionary(definition => definition.Name);

    /// <summary>
    /// Resolves a command name for a caller. An unknown name and a privileged command without the
    /// permission both answer false: like NGemity, the server does not reveal that a command exists to
    /// someone who may not use it.
    /// </summary>
    public static bool TryResolve(string name, int permission, out GmCommandDefinition definition)
    {
        if (name != null && ByName.TryGetValue(name, out definition) &&
            GmCommandRules.CanUse(definition, permission))
        {
            return true;
        }

        definition = null;
        return false;
    }

    /// <summary>Whether <paramref name="name"/> is a command at all, whatever the caller's permission.</summary>
    public static bool Exists(string name) => name != null && ByName.ContainsKey(name);

    /// <summary>The commands a caller may use, in table order, for <c>/help</c>.</summary>
    public static IEnumerable<GmCommandDefinition> AvailableTo(int permission) =>
        All.Where(definition => GmCommandRules.CanUse(definition, permission));
}
