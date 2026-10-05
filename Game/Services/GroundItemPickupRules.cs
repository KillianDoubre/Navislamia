namespace Navislamia.Game.Services;

/// <summary>
/// Who may take a ground object, and from when. The object's <c>TM_SC_ENTER</c> (3) carries a
/// <c>pick_up_order</c> of three slots, each naming one entitled player or one entitled party, and the
/// official server re-reads those same three slots when <c>TM_CS_TAKE_ITEM</c> (204) arrives
/// (<c>onTakeItem</c> 0x140134080, order loop at 0x140134848-0x140134890 decoded in
/// <c>docs/packet-specs/socle-partage-objets-sol.md</c> §5.3).
/// </summary>
/// <remarks>
/// <para>
/// The loop walks the slots in order: an empty slot or one that names the asker accepts at once, a slot
/// that names somebody else refuses the asker until that slot's deadline — 3000 ticks for the first,
/// <c>+1000</c> per slot already skipped — and once the three slots are exhausted the asker is accepted.
/// </para>
/// <para>
/// One <c>ar_time</c> tick is 10 ms (<see cref="ServerClock"/>), so the deadlines are <b>30 / 40 / 50
/// seconds</b> and an unentitled player may take the object at 30 s when a single slot is filled, at 50 s
/// when the three are. <c>203-drop-item.md</c> announced a 3/4/5 second lock: that was the tick count read
/// as milliseconds, and this rule is the correction.
/// </para>
/// <para>
/// The only criterion is the time since the fall. Neither the distance nor the composition of the party at
/// the moment of the click takes part: the official server tests a fixed 20-unit range for everybody, the
/// client tests none at all.
/// </para>
/// </remarks>
public static class GroundItemPickupRules
{
    /// <summary>Slots in a <c>TS_ITEM_PICK_UP_ORDER</c> (<c>rzu TS_SC_ENTER.h:29-33</c>).</summary>
    public const int Slots = 3;

    /// <summary>Deadline of the first slot, in <c>ar_time</c> ticks: 3000 ticks = 30 s.</summary>
    public const uint FirstDeadlineTicks = 3000;

    /// <summary>Added to the deadline per filled, non-corresponding slot already skipped: 1000 = 10 s.</summary>
    public const uint SlotStepTicks = 1000;

    /// <summary>
    /// The take decision of <c>onTakeItem</c> for one asker.
    /// </summary>
    /// <param name="elapsedTicks">
    /// <c>GetArTime() - drop_time</c> in ticks — the time since the object fell, never the time since the
    /// last time it was sent to anybody.
    /// </param>
    /// <param name="occupiedSlots">
    /// How many slots of the order are filled. Zero is the empty order (common loot, as a raid leaves it),
    /// which everybody takes at once.
    /// </param>
    /// <param name="firstSlotNamesMe">
    /// Whether the first filled slot designates the asker: its <c>hPlayer[0]</c> handle, or the party in its
    /// <c>nPartyID[0]</c>. Such an asker is accepted as soon as the object falls, without waiting.
    /// </param>
    /// <remarks>
    /// This repository fills slot 0 and leaves slots 1 and 2 at zero (the official server fills them with
    /// the two other contributing parties, which needs a per-party contribution table the repository does
    /// not have — <c>docs/packet-specs/socle-partage-objets-sol.md</c> §7.2), so production only ever passes
    /// <c>occupiedSlots</c> 0 or 1. The pacing below is the official one for every count, so the dedicated
    /// multi-slot card will extend the caller, not rewrite the rule — but note what this signature assumes:
    /// the slot that may name the asker is the first filled one, every further filled slot naming somebody
    /// else. A card that fills slots 1 and 2 asks per slot and generalises this loop.
    /// </remarks>
    public static bool CanPickUp(uint elapsedTicks, int occupiedSlots, bool firstSlotNamesMe)
    {
        if (occupiedSlots <= 0 || firstSlotNamesMe) return true;

        var deadline = FirstDeadlineTicks;
        for (var slot = 0; slot < occupiedSlots; slot++)
        {
            if (elapsedTicks < deadline) return false;
            deadline += SlotStepTicks;
        }

        return true;
    }
}
