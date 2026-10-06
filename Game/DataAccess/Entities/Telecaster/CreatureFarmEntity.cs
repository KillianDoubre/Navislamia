using System;

namespace Navislamia.Game.DataAccess.Entities.Telecaster;

/// <summary>
/// One invocation deposited in the creature farm: the official farm table (<c>DB_Farm.cpp:16-29</c>,
/// <c>DB_Login.cpp:1330-1360</c>) reduced to the columns the 7.3 server serves, as decided by
/// docs/packet-specs/socle-ferme-creatures-officielle.md §5.6 point 1.
/// <para>
/// The repositories already holding the items are Telecaster ones (<see cref="ItemEntity"/>): the row
/// lives beside them, so the card it names and its farm row are read together. The shape is the
/// official one — a row per deposited invocation — rather than <see cref="ItemStorageEntity"/>, which
/// names no slot, no <c>max_level</c>, no cracker and no nursing time.
/// </para>
/// </summary>
public class CreatureFarmEntity : Entity
{
    /// <summary>
    /// The farm slot, 0..2 (<c>FARM_MAX_COUNT</c>, measured 7.3 in §5.2). The official table stores it
    /// 1-based and the read wears it back (<c>slot - 1</c>, <c>DB_Login.cpp:1333-1334</c>); this row keeps
    /// the 0-based number the wire carries (<c>6001</c> <c>index</c>, §3.2).
    /// </summary>
    public int Slot { get; set; }

    /// <summary>
    /// The owner of the farm. The card stays bound to the character while it is farmed, as the official
    /// row does through <c>owner_id = SID of the player</c> (§5.6 point 1, §7.1).
    /// </summary>
    public long CharacterId { get; set; }
    public virtual CharacterEntity Character { get; set; }

    /// <summary>The UID of the deposited card (<c>item_id</c> in the official table).</summary>
    public long CardItemId { get; set; }

    /// <summary>
    /// Frozen at deposition: <see cref="Creatures.CreatureFarmRules.MaxLevel"/> (100 in 7.3) for a premium
    /// ticket, the depositing character's level otherwise (<c>StructPlayer.cpp:11319</c>).
    /// </summary>
    public int MaxLevel { get; set; }

    /// <summary>The cracker consumed at deposition (<c>is_using_cracker</c>); ×1.5 on the farm experience.</summary>
    public bool IsUsingCracker { get; set; }

    /// <summary>The ticket was premium (<c>is_cash</c>), read as <c>using_cash</c> by the <c>6001</c>.</summary>
    public bool IsCash { get; set; }

    /// <summary>Deposition time; the <c>6001</c>'s <c>elasped_time</c> is now − this, in seconds (§3.2).</summary>
    public DateTime RegistrationTime { get; set; }

    /// <summary>
    /// The ticket's duration in seconds (<c>duration</c> of the <c>6001</c>); it belongs to the ticket the
    /// deposit consumed and is therefore written by the deposit, never derived here.
    /// </summary>
    public int Duration { get; set; }

    /// <summary>
    /// The last nursing time (<c>nursing_time</c>), null when the entry was never nursed — the <c>6001</c>
    /// then reads <c>refresh_time = 0</c> like an entry nursed after the last 06:00 (§3.2, §5.4).
    /// </summary>
    public DateTime? NursingTime { get; set; }
}
