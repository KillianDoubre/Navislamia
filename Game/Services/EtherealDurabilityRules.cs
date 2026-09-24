using Navislamia.Game.DataAccess.Entities.Enums;
using Navislamia.Game.DataAccess.Repositories.Interfaces;
using Navislamia.Game.Network.Packets;

namespace Navislamia.Game.Services;

/// <summary>
/// Which clause of the refusal the client itself names (db_string <c>rzlabs_msg_ethereal_durability</c>,
/// "cannot be sacrificed or has no durability to sacrifice!") an offered object falls under. The two
/// clauses stand apart in the log; they share one answer code (§5.2 of the fiche).
/// </summary>
public enum EtherealSacrificeGate
{
    /// <summary>The object is an equipment that still carries ethereal durability: the gesture is sound.</summary>
    Accepted,

    /// <summary>The object is not the kind of object the gesture consumes.</summary>
    CannotBeSacrificed,

    /// <summary>The object's own ethereal durability is spent.</summary>
    NoDurabilityToSacrifice
}

/// <summary>
/// The sacrifice guard of <c>TM_CS_TRANSMIT_ETHEREAL_DURABILITY</c> (263): what the handle of a readable
/// frame must designate before the frame can be acted upon. It judges the object, never the charge: the
/// amount, the ceiling, the consumed object and the boost itself are not established
/// (docs/packet-specs/263-transmit-ethereal-durability.md §7.1, §7.2).
/// </summary>
public static class EtherealDurabilityRules
{
    /// <summary>
    /// The two clauses, in order. A resource the catalog does not know leaves the nature clause ungated —
    /// an object the server cannot judge is not refused for its nature — exactly as
    /// <see cref="IItemGroupCatalog"/> prescribes for its group gate.
    /// </summary>
    public static EtherealSacrificeGate Judge(ItemEtherealFields? resource, int itemEtherealDurability)
    {
        if (resource is { } fields && !CanBeSacrificed(fields))
        {
            return EtherealSacrificeGate.CannotBeSacrificed;
        }

        return itemEtherealDurability <= 0
            ? EtherealSacrificeGate.NoDurabilityToSacrifice
            : EtherealSacrificeGate.Accepted;
    }

    /// <summary>
    /// The nature clause: the object is wearable at all and its resource can carry ethereal durability.
    /// Both are needed — a non-wearable resource (the Ethereal Stone itself, a material) cannot be the
    /// equipment the window's material slot asks for, and a wearable resource that carries no ethereal
    /// durability has nothing to give.
    /// </summary>
    public static bool CanBeSacrificed(in ItemEtherealFields resource)
    {
        return IsEquipment(resource.WearType) && resource.EtherealDurability > 0;
    }

    /// <summary>
    /// Whether a resource's wear type is an equipment position. <c>None</c> and <c>CantWear</c> are the
    /// same value (-1) in <see cref="ItemWearType"/>: a resource declared non-wearable is no equipment.
    /// </summary>
    public static bool IsEquipment(ItemWearType wearType)
    {
        return wearType != ItemWearType.None;
    }

    /// <summary>
    /// The answer code of a refused object, and of an accepted one. <c>NotActable</c> (5) is the code the
    /// repository already answers a refused object with when it is the object itself that cannot play its
    /// part (<c>ItemUseService</c>, <c>EquipmentService</c>, <c>StorageService</c>); the code the 7.3
    /// client would map to each of the two clauses is not established (§7.6), so no new code is invented
    /// and an accepted gesture keeps the socle's <c>InvalidArgument</c>.
    /// </summary>
    public static ResultCode RefusalCode(EtherealSacrificeGate gate)
    {
        return gate == EtherealSacrificeGate.Accepted ? ResultCode.Success : ResultCode.NotActable;
    }

    /// <summary>The clause in words, for the server log.</summary>
    public static string Describe(EtherealSacrificeGate gate)
    {
        return gate switch
        {
            EtherealSacrificeGate.CannotBeSacrificed => "cannot be sacrificed",
            EtherealSacrificeGate.NoDurabilityToSacrifice => "has no durability to sacrifice",
            _ => "can be sacrificed"
        };
    }
}
