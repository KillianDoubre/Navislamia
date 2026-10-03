using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Navislamia.Configuration.Options;
using Navislamia.Game.Network.Clients;
using Navislamia.Game.Network.Packets.Game;
using Navislamia.Game.Services.Jobs;

namespace Navislamia.Game.Services.Creatures;

/// <summary>What a creature keeper's choice shows: the page, or nothing when the dialog closes.</summary>
public sealed record CreatureDialogStep(NpcDialogDefinition Page);

public interface ICreatureDialogService
{
    CreatureDialogStep Select(GameClient client, int npcId, string function, string trigger);
}

/// <summary>
/// The creature keepers' Lua (<c>NPC_CreatureSetup.lua</c>), ported like the job change: the care pages
/// (<c>Creature_Management_*</c>, <c>Creature_Care_*</c>, the paid recoveries, the revival of a dead summon) and the
/// evolution pages (<c>NPC_Creature_Evolution_Menu</c>, <c>Creature_Evolution_sub</c>, <c>Creature_Evolution_exe</c>).
/// A summon is named by its card's handle where the Lua names it by the summon's own handle: the card is what the
/// session always knows. Everything is judged again when a choice comes back.
/// </summary>
public sealed class CreatureDialogService : ICreatureDialogService
{
    public const string EvolutionMenu = "NPC_Creature_Evolution_Menu";
    public const string EvolutionSub = "Creature_Evolution_sub";
    public const string EvolutionExe = "Creature_Evolution_exe";

    /// <summary><c>CHAT_NPC</c>, what the Lua's <c>message()</c> sends, from <c>@SCRIPT</c>.</summary>
    private const byte ChatNpc = 40;

    private static readonly Dictionary<string, int> RaceNpc = new()
    {
        ["Deva"] = 1001,
        ["Asura"] = 2001,
        ["Gaia"] = 4001
    };

    private readonly ICreatureService _creatures;

    public CreatureDialogService(ICreatureService creatures)
    {
        _creatures = creatures;
    }

    /// <summary>Whether a dialog function is one of the creature keeper's.</summary>
    public static bool Handles(string function) =>
        function is EvolutionMenu or EvolutionSub or EvolutionExe
        || (function is not null && function.StartsWith("Creature_", StringComparison.Ordinal)
                                 && ParseRace(function, out _, out _));

    public CreatureDialogStep Select(GameClient client, int npcId, string function, string trigger)
    {
        var args = Arguments(trigger);
        switch (function)
        {
            case EvolutionMenu:
                return new CreatureDialogStep(EvolutionMenuPage(client, npcId));
            case EvolutionSub:
                return new CreatureDialogStep(EvolutionSubPage(client, npcId, Card(client, args)));
            case EvolutionExe:
                return new CreatureDialogStep(EvolutionExecute(client, npcId, Card(client, args)));
        }

        if (!ParseRace(function, out var kind, out var race))
        {
            return new CreatureDialogStep(null);
        }

        var keeper = RaceNpc[race];
        return new CreatureDialogStep(kind switch
        {
            "Management" => ManagementPage(client, keeper, race),
            "Care" => CarePage(client, keeper, race, Card(client, args)),
            _ => Recover(client, keeper, race, kind, Card(client, args), args)
        });
    }

    // ---- care --------------------------------------------------------------------------------------------

    private NpcDialogDefinition ManagementPage(GameClient client, int keeper, string race)
    {
        var page = new NpcDialogDefinition { Title = JobChangeRules.NpcString(keeper, 1), Text = JobChangeRules.NpcString(keeper, 8) };
        foreach (var card in _creatures.FormedCards(client.ConnectionInfo))
        {
            page.Menu.Add(new NpcDialogMenuEntry { Label = CreatureLabel(card), Trigger = Call($"Creature_Care_{race}", card.Handle) });
        }

        page.Menu.Add(new NpcDialogMenuEntry { Label = "@90010002", Trigger = " " });
        return page;
    }

    /// <summary><c>Creature_Care_*</c>: the summon's vitals and the recoveries it may buy, with the Lua's prices.</summary>
    private NpcDialogDefinition CarePage(GameClient client, int keeper, string race, CreatureCard card)
    {
        if (card is null)
        {
            return null;
        }

        var (hp, maxHp, mp, maxMp) = _creatures.VitalsOf(client, card);
        var level = card.Level;
        var percentHp = maxHp > 0 ? (int)Math.Floor(hp * 100.0 / maxHp) : 0;
        var percentMp = maxMp > 0 ? (int)Math.Floor(mp * 100.0 / maxMp) : 0;
        var page = new NpcDialogDefinition
        {
            Title = JobChangeRules.NpcString(keeper, 1),
            Text = hp == 0
                ? JobChangeRules.Sconv("@90010019", "#@creature_name@#", card.SummonName, "#@creature_level@#", Text(level),
                    "#@creature_HP@#", Text(hp), "#@creature_MaxHP@#", Text(maxHp), "#@creature_MP@#", Text(mp),
                    "#@creature_MaxMP@#", Text(maxMp), "#@percent_MP@#", Text(percentMp))
                : JobChangeRules.Sconv("@90010018", "#@creature_name@#", card.SummonName, "#@creature_level@#", Text(level),
                    "#@creature_HP@#", Text(hp), "#@creature_MaxHP@#", Text(maxHp), "#@percent_HP@#", Text(percentHp),
                    "#@creature_MP@#", Text(mp), "#@creature_MaxMP@#", Text(maxMp), "#@percent_MP@#", Text(percentMp))
        };

        var prices = CarePrices(level, percentHp, percentMp);
        if (hp == 0)
        {
            page.Menu.Add(Entry(JobChangeRules.Sconv("@90010023", "#@cost@#", Text(prices.Revive)),
                Call($"Creature_Care_RecoverEx_HP_{race}", card.Handle, prices.Revive, 30)));
            page.Menu.Add(Entry(JobChangeRules.Sconv("@90010033", "#@cost@#", Text(prices.Restore + prices.All)),
                Call($"Creature_Care_Recover_ALL_{race}", card.Handle, prices.Restore + prices.All)));
        }
        else
        {
            if (hp < maxHp || mp < maxMp)
            {
                page.Menu.Add(Entry(JobChangeRules.Sconv("@90010024", "#@cost@#", Text(prices.All)),
                    Call($"Creature_Care_Recover_ALL_{race}", card.Handle, prices.All)));
            }

            if (hp > 0 && hp < maxHp)
            {
                page.Menu.Add(Entry(JobChangeRules.Sconv("@90010025", "#@cost@#", Text(prices.Hp)),
                    Call($"Creature_Care_Recover_HP_{race}", card.Handle, prices.Hp)));
            }

            if (mp >= 0 && mp < maxMp)
            {
                page.Menu.Add(Entry(JobChangeRules.Sconv("@90010026", "#@cost@#", Text(prices.Mp)),
                    Call($"Creature_Care_Recover_MP_{race}", card.Handle, prices.Mp)));
            }
        }

        page.Menu.Add(Entry("@90010003", Call($"Creature_Management_{race}")));
        page.Menu.Add(Entry("@90010001", " "));
        return page;
    }

    /// <summary>
    /// The prices of <c>Creature_Care_*</c>: HP <c>(100 − HP %) × (lv + 4) / 10</c>, MP three times that rate, the
    /// revival <c>100 × (lv + 4) / 10 × 3 + 30 × (lv + 4) / 10</c>, each floored.
    /// </summary>
    public static (long Hp, long Mp, long All, long Restore, long Revive) CarePrices(int level, int percentHp, int percentMp)
    {
        var hp = (long)Math.Floor((100 - percentHp) * (level + 4) / 10.0);
        var mp = (long)Math.Floor((100 - percentMp) * (level + 4) / 10.0 * 3);
        var restore = (long)Math.Floor(100 * (level + 4) / 10.0 * 3);
        var revive = restore + (long)Math.Floor(30 * (level + 4) / 10.0);
        return (hp, mp, hp + mp, restore, revive);
    }

    /// <summary>
    /// The paid recoveries: refused (<c>@90010005</c>) without the gold, otherwise the gold is taken and the summon
    /// gets its HP (a revival at <c>ratio</c> % of it), its MP, or both, then <c>@90010031</c>.
    /// </summary>
    private NpcDialogDefinition Recover(GameClient client, int keeper, string race, string kind, CreatureCard card,
        IReadOnlyList<long> args)
    {
        if (card is null || args.Count < 2)
        {
            return null;
        }

        var cost = Math.Max(0, args[1]);
        var page = new NpcDialogDefinition { Title = JobChangeRules.NpcString(keeper, 1) };
        page.Menu.Add(Entry("@90010003", Call($"Creature_Management_{race}")));
        page.Menu.Add(Entry("@90010002", " "));
        var info = client.ConnectionInfo;
        if (!info.TryDebitGold(cost))
        {
            page.Text = "@90010005";
            return page;
        }

        client.Connection.Send(GameCharacterPackets.BuildGoldUpdate(info.CharacterGold, info.CharacterChaos));
        var (hp, maxHp, mp, maxMp) = _creatures.VitalsOf(client, card);
        switch (kind)
        {
            case "RecoverEx_HP":
                var ratio = args.Count >= 3 ? args[2] : 100;
                hp = (int)Math.Floor(maxHp * ratio / 100.0);
                break;
            case "Recover_HP":
                hp = maxHp;
                break;
            case "Recover_MP":
                mp = maxMp;
                break;
            case "Recover_ALL":
                hp = maxHp;
                mp = maxMp;
                break;
            case "Recover_SP":
                break;
        }

        _creatures.SetSummonVitals(client, card, hp, mp);
        page.Text = "@90010031";
        client.Connection.Send(GameChatPackets.BuildChat("@SCRIPT", ChatNpc, "@90010031"));
        return page;
    }

    // ---- evolution ----------------------------------------------------------------------------------------

    private NpcDialogDefinition EvolutionMenuPage(GameClient client, int npcId)
    {
        var page = new NpcDialogDefinition { Title = JobChangeRules.NpcString(npcId, 0), Text = JobChangeRules.NpcString(npcId, 13) };
        var any = false;
        foreach (var card in _creatures.FormedCards(client.ConnectionInfo))
        {
            if (!Evolvable(card))
            {
                continue;
            }

            page.Menu.Add(Entry(CreatureLabel(card), Call(EvolutionSub, card.Handle)));
            any = true;
        }

        if (!any)
        {
            page.Text = JobChangeRules.NpcString(npcId, 14);
        }

        page.Menu.Add(Entry("@90010002", " "));
        return page;
    }

    private bool Evolvable(CreatureCard card) => _creatures.FormOf(card) switch
    {
        1 => card.Level >= SummonProgression.NormalEvolveLevel,
        2 => card.Level >= SummonProgression.GrowthEvolveLevel,
        _ => false
    };

    private NpcDialogDefinition EvolutionSubPage(GameClient client, int npcId, CreatureCard card)
    {
        if (card is null || !Evolvable(card))
        {
            return null;
        }

        var page = new NpcDialogDefinition { Title = JobChangeRules.NpcString(npcId, 0) };
        if (!_creatures.IsOut(client.ConnectionInfo, card))
        {
            page.Text = JobChangeRules.Sconv(JobChangeRules.NpcString(npcId, 16), "#@creature_name@#", CreatureLabel(card));
            page.Menu.Add(Entry("@90010064", Call(EvolutionExe, card.Handle)));
        }
        else
        {
            // A summon out in the world must be sent back first.
            page.Text = JobChangeRules.NpcString(npcId, 15);
        }

        page.Menu.Add(Entry("@90010003", Call(EvolutionMenu)));
        page.Menu.Add(Entry("@90010002", " "));
        return page;
    }

    private NpcDialogDefinition EvolutionExecute(GameClient client, int npcId, CreatureCard card)
    {
        if (card is null || !_creatures.Evolve(client, card))
        {
            return null;
        }

        var page = new NpcDialogDefinition { Title = JobChangeRules.NpcString(npcId, 0), Text = JobChangeRules.NpcString(npcId, 17) };
        page.Menu.Add(Entry("@90010002", " "));
        return page;
    }

    // ---- helpers ------------------------------------------------------------------------------------------

    private static string CreatureLabel(CreatureCard card) =>
        JobChangeRules.Sconv("@90010009", "#@creature_name@#", card.SummonName, "#@creature_level@#", Text(card.Level));

    private CreatureCard Card(GameClient client, IReadOnlyList<long> args) =>
        args.Count > 0 && args[0] is > 0 and <= uint.MaxValue
            ? _creatures.FindCard(client.ConnectionInfo, (uint)args[0])
            : null;

    private static NpcDialogMenuEntry Entry(string label, string trigger) => new() { Label = label, Trigger = trigger };

    private static string Text(long value) => value.ToString(CultureInfo.InvariantCulture);

    /// <summary>A trigger written like the Lua's: <c>Name( a, b )</c>.</summary>
    public static string Call(string function, params long[] args) => args.Length == 0
        ? function + "()"
        : function + "( " + string.Join(", ", args.Select(Text)) + " )";

    /// <summary>The integer arguments of a trigger, in order.</summary>
    public static IReadOnlyList<long> Arguments(string trigger)
    {
        var open = trigger?.IndexOf('(') ?? -1;
        var close = trigger?.LastIndexOf(')') ?? -1;
        if (open < 0 || close <= open)
        {
            return Array.Empty<long>();
        }

        var values = new List<long>();
        foreach (var part in trigger[(open + 1)..close].Split(',', StringSplitOptions.RemoveEmptyEntries))
        {
            if (long.TryParse(part.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var value))
            {
                values.Add(value);
            }
        }

        return values;
    }

    /// <summary><c>Creature_&lt;kind&gt;_&lt;race&gt;</c>: the kind of page and the keeper's race.</summary>
    private static bool ParseRace(string function, out string kind, out string race)
    {
        kind = race = null;
        if (function is null || !function.StartsWith("Creature_", StringComparison.Ordinal))
        {
            return false;
        }

        var cut = function.LastIndexOf('_');
        race = function[(cut + 1)..];
        if (!RaceNpc.ContainsKey(race))
        {
            return false;
        }

        kind = function["Creature_".Length..cut];
        if (kind.StartsWith("Care_", StringComparison.Ordinal))
        {
            kind = kind["Care_".Length..];
        }

        return kind is "Management" or "Care" or "RecoverEx_HP" or "Recover_HP" or "Recover_MP" or "Recover_SP"
            or "Recover_ALL";
    }
}
