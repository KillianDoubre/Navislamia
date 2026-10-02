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
    /// another player. The locations that are PK fields are not known here, so it is off by default: then only a
    /// duel opens player-versus-player combat (docs/packet-specs/socle-competition-joueurs.md §10).
    /// </summary>
    public bool PkFieldsEverywhere { get; set; }
}
