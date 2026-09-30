namespace Navislamia.Configuration.Options;

/// <summary>
/// The <c>Crafting</c> section of the game server's settings (docs/packet-specs/socle-artisanat-ressources.md
/// §14).
/// </summary>
public class CraftingOptions
{
    /// <summary>
    /// The server's country bits, NGemity <c>Game.LocalFlag</c>: an <c>EnhanceResource</c> row applies when
    /// <c>(LocalFlag &amp; local_flag) != 0</c> (<c>ObjectMgr.cpp:1099</c>). <c>1</c> is the value that gives
    /// each of the 111 <c>enhance_id</c> of the 9.4 data exactly one row; NGemity's own defaults (4 in code,
    /// 8 in <c>chihiro.conf.dist</c>) leave 6 or 18 of them without one.
    /// </summary>
    public int LocalFlag { get; set; } = 1;
}
