namespace Navislamia.Game.Network.Packets.Enums;

/// <summary>
/// The <c>type</c> byte of <c>TM_CS_RESURRECTION</c> (513), the reference's <c>TS_RESURRECTION_TYPE</c>.
/// Epic 7.3 replaced the pre-6.1 <c>use_state</c>/<c>use_potion</c> boolean pair with this single
/// field, so the frame carries one byte here instead of two.
/// </summary>
/// <remarks>
/// The mapping between the client's three death-window buttons and these values is deduced from the
/// control names (<c>button_resurrect_buff</c>, <c>button_resurrect_item</c>,
/// <c>button_resurrect_town</c>) and the client texts, not read from the code that builds the frame:
/// see docs/packet-specs/socle-mort-respawn.md §5.2 and its open point §15.1.
/// </remarks>
public enum ResurrectionType : sbyte
{
    /// <summary>Resurrect at the return point, without a state or an item: the town button.</summary>
    UseNone = 0,

    /// <summary>Resurrect through a resurrection buff.</summary>
    UseState = 1,

    /// <summary>Resurrect through a resurrection item (scroll, potion, bottle).</summary>
    UsePotion = 2,

    /// <summary>Competition context.</summary>
    Compete = 3,

    /// <summary>Deathmatch context.</summary>
    Deathmatch = 4
}
