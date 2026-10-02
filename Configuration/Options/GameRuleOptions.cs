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
}
