using Navislamia.Game.DataAccess.Entities.Enums;

namespace Navislamia.Game.Services.Pets;

/// <summary>
/// The pet's pickup filter, the value of <c>TM_CS_SET_PET_FILTER</c> (355): which ground items the pet collects,
/// by the item's <c>type</c> column (<see cref="ItemBaseType"/>). Ported bit for bit from the 7.3 client's own
/// gather (<c>SFrame.exe</c> @<c>0x473ea3</c>, the <c>GetItemObjects_For_PetAutoGather</c> that
/// <c>SGameLocalPet</c> calls with the filter, @<c>0x6cd89e</c>), whose window labels are the client's strings
/// "Pickup Filter — All, Consumable, Soulstone, Cube, Card, Gear, Etc". No official server reads 355: the client
/// filters its own pet. This server also gathers for the pet, so it follows the same rule (socle-familier-pet.md §19).
/// </summary>
public static class PetPickupFilter
{
    public const uint Consumable = 0x01;
    public const uint Soulstone = 0x02;
    public const uint Cube = 0x04;
    public const uint Card = 0x08;
    public const uint Gear = 0x10;
    public const uint Etc = 0x20;

    /// <summary>"All": compared for equality (<c>cmp $0x3f</c>), not as a mask; only it lets a charm through.</summary>
    public const uint All = 0x3f;

    /// <summary>The client's default when its option is absent (@<c>0x65048e</c>): everything but Etc.</summary>
    public const uint Default = 0x1f;

    /// <summary>
    /// Whether the pet collects an item of <paramref name="type"/> under <paramref name="filter"/>. A reusable item
    /// (<see cref="ItemBaseType.Use"/>, the cages among them) is always collected. <c>null</c> is an item with no
    /// client record — gold, code 0 — whose category the client reads from a fallback record that nothing
    /// establishes: it is collected (NON ÉTABLI, §19).
    /// </summary>
    public static bool Collects(uint filter, ItemBaseType? type)
    {
        if (type is not { } kind || filter == All || kind == ItemBaseType.Use)
        {
            return true;
        }

        var bit = kind switch
        {
            ItemBaseType.Supply => Consumable,
            ItemBaseType.Soulstone => Soulstone,
            ItemBaseType.Cube => Cube,
            ItemBaseType.Card => Card,
            ItemBaseType.Armor => Gear,
            ItemBaseType.Etc => Etc,
            _ => 0u
        };
        return (filter & bit) != 0;
    }
}
