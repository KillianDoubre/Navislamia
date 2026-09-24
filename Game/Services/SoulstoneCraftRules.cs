using System.Collections.Generic;

namespace Navislamia.Game.Services;

/// <summary>
/// The two game rules <c>TM_CS_SOULSTONE_CRAFT</c> (260) applies, kept out of the handler so they can be
/// judged on their own: what a socketing costs and how many identical stones an item accepts.
///
/// Both are ported from NGemity (<c>WorldSession.cpp:1515</c> and <c>:1553</c>), which is the only source
/// that states them — the client proves a cost exists (<c>db_string.rdb</c> <c>smsg_soket02</c>) and that
/// two stones of the same stat are refused (<c>smsg_soket05</c>), never the amounts. They are flagged for
/// Killian: the price divisor is a game-design value, and the duplication threshold is only the
/// reference's reading. See docs/packet-specs/260-soulstone-craft.md §5.5, §7.4 and §7.5.
/// </summary>
public static class SoulstoneCraftRules
{
    /// <summary>
    /// Gold divisor applied to the price of every provided stone, one integer division per slot
    /// (NGemity <c>WorldSession.cpp:1553</c> <c>nCraftCost += price / 10</c>).
    /// </summary>
    public const int PriceDivisor = 10;

    /// <summary>NGemity <c>WorldSession.cpp:1515</c>: <c>nSocketCount == 4 ? 2 : 1</c>.</summary>
    public const int FourSocketDuplicationLimit = 2;

    public const int DefaultDuplicationLimit = 1;

    /// <summary>How many stones of the same profile an item with <paramref name="socketCount"/> chassis accepts.</summary>
    public static int DuplicationLimit(int socketCount)
    {
        return socketCount == 4 ? FourSocketDuplicationLimit : DefaultDuplicationLimit;
    }

    /// <summary>
    /// The gold a request costs: every provided stone's price divided by ten, the division done per stone
    /// as the reference accumulates it, then added up.
    /// </summary>
    public static long CraftCost(IReadOnlyList<int> prices)
    {
        var cost = 0L;
        if (prices is null)
        {
            return cost;
        }

        foreach (var price in prices)
        {
            cost += price / PriceDivisor;
        }

        return cost;
    }

    /// <summary>
    /// The slots of the frame that carry a stone, in slot order, limited to the chassis the crafted item
    /// actually has. NGemity only walks <c>0..socketCount-1</c> (<c>WorldSession.cpp:1520</c>) while the
    /// frame always carries four handles (rzu <c>TS_CS_SOULSTONE_CRAFT.h:7</c>): the slots past the item's
    /// chassis are ignored, never refused.
    /// </summary>
    public static List<int> FilledSlots(uint[] handles, int socketCount)
    {
        var slots = new List<int>(socketCount);
        if (handles is null)
        {
            return slots;
        }

        var bounded = socketCount < handles.Length ? socketCount : handles.Length;
        for (var slot = 0; slot < bounded; slot++)
        {
            if (handles[slot] != 0)
            {
                slots.Add(slot);
            }
        }

        return slots;
    }

    /// <summary>
    /// How many chassis other than <paramref name="slot"/> already hold a stone of the same profile.
    /// The stone being socketed never counts against itself (<c>k != i</c>, <c>WorldSession.cpp:1535</c>),
    /// and a chassis the catalog does not know is left out of the count rather than compared.
    /// </summary>
    public static int CountIdenticalSockets(in SoulstoneProfile stone,
        IReadOnlyList<(int Slot, SoulstoneProfile Profile)> socketed, int slot)
    {
        var identical = 0;
        if (socketed is null)
        {
            return identical;
        }

        foreach (var entry in socketed)
        {
            if (entry.Slot != slot && entry.Profile == stone)
            {
                identical++;
            }
        }

        return identical;
    }
}
