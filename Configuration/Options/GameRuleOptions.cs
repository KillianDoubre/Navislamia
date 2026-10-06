using System.Collections.Generic;

namespace Navislamia.Configuration.Options;

/// <summary>
/// The server's game rules (section <c>GameRules</c>), the official server's <c>GameRule</c> globals.
/// </summary>
public class GameRuleOptions
{
    /// <summary>
    /// <c>GameRule::bIsPKServer</c>: a death costs twice the experience and may drop an item
    /// (docs/packet-specs/socle-mort-joueur.md §4). Off by default, like a regular server.
    /// </summary>
    public bool PkServer { get; set; }

    /// <summary>
    /// Whether every place is a PK field (<c>StructPlayer::IsInPKField</c>), where a player in PK mode may attack
    /// another player. Debug override: normally the .nfl polygons and WorldLocation types decide.
    /// </summary>
    public bool PkFieldsEverywhere { get; set; }

    /// <summary>GameRule::fPVPDamageRateForPlayer (Epic 7 default: 0.05).</summary>
    public decimal PvpDamageRate { get; set; } = 0.05m;

    public int PkPenaltyLevel { get; set; } = 10;
    /// <summary>Retail fStaminaBonusRate: an additive share of monster EXP and JP.</summary>
    public decimal StaminaBonusRate { get; set; } = 1m;
    /// <summary>Additional monster EXP/JP in imported dungeon cells; configured per server.</summary>
    public decimal DungeonRewardBonusRate { get; set; }

    /// <summary>
    /// The PC bang mode given to every player whose login carries none (0 none, 1 ally, 2 premium): the auth server
    /// of this repository sends no mode, so this is how a server grants the bonus. The higher of the two applies.
    /// </summary>
    public byte DefaultPcBangMode { get; set; }

    /// <summary>GameRule::fAllyPCBangBonusRate: the EXP/JP an ally PC bang adds (0.1).</summary>
    public decimal AllyPcBangBonusRate { get; set; } = 0.1m;

    /// <summary>GameRule::fPremiumPCBangBonusRate: the EXP/JP a premium PC bang adds (1.2).</summary>
    public decimal PremiumPcBangBonusRate { get; set; } = 1.2m;

    /// <summary>GameRule::fAllyPCBangChaosBonusRate / fPremiumPCBangChaosBonusRate: the chaos both add (0.1).</summary>
    public decimal AllyPcBangChaosBonusRate { get; set; } = 0.1m;
    public decimal PremiumPcBangChaosBonusRate { get; set; } = 0.1m;

    /// <summary>
    /// <c>g_nCurrentLocalFlag</c> (<c>game.local_flag</c>): the server's country bit, 1 = Korea, the official default
    /// and the value the crafting, dungeon and HuntaHolic catalogues already use. An NPC whose <c>local_flag</c> has
    /// this bit is not shown (docs/packet-specs/socle-pnj-pays-periodes.md).
    /// </summary>
    public int LocalFlag { get; set; } = 1;

    /// <summary>
    /// <c>game.ServiceServer</c>: a live server hides the NPCs flagged "not on a service server" (bit 30: cash shop
    /// merchants, test helpers) and shows those flagged "not on a test server" (bit 29).
    /// </summary>
    public bool ServiceServer { get; set; } = true;

    /// <summary>
    /// The seasonal events this server runs, each with its yearly window. The official Lua knows no date: an event's
    /// NPCs and menu entries were added to the scripts for the event and taken out after it. Here they stay hidden
    /// until an entry opens their event (<c>NpcEvents</c>, docs/packet-specs/socle-pnj-evenements.md); none by default.
    /// </summary>
    public List<GameEventWindow> Events { get; set; } = new();
}

/// <summary>
/// One seasonal event and its yearly window: <c>From</c> and <c>To</c> are <c>MM-dd</c>, both included, in the server's
/// local date; a window whose end comes before its start spans the new year (<c>12-20</c> to <c>01-05</c>).
/// </summary>
public class GameEventWindow
{
    /// <summary>The event: Halloween, Christmas, Valentine, Easter, NewYear, or a script function's own name.</summary>
    public string Name { get; set; }

    public string From { get; set; }

    public string To { get; set; }
}
