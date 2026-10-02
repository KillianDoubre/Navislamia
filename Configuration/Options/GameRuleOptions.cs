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
}
