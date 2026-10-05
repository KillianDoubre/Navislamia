using FluentAssertions;
using Navislamia.Game.Services;
using NUnit.Framework;

namespace Tests.Game;

/// <summary>
/// The order loop of the official <c>onTakeItem</c> (0x140134848-0x140134890) on its own: an empty slot or
/// one naming the asker accepts at once, a slot naming somebody else refuses until that slot's deadline, and
/// the asker is accepted once the three slots are exhausted. The pacing is the correction this branch
/// brings: <c>203-drop-item.md</c> announced a 3/4/5 second lock, which was the tick count read as
/// milliseconds — an <c>ar_time</c> tick is 10 ms, so the deadlines are 30/40/50 seconds
/// (<c>docs/packet-specs/socle-partage-objets-sol.md</c> §5.1-5.3).
/// </summary>
[TestFixture]
public class GroundItemPickupRulesTests
{
    private const uint First = GroundItemPickupRules.FirstDeadlineTicks;

    /// <summary>The figure itself, in seconds: 3000 ticks, +1000 per further filled slot, 100 ticks per second.</summary>
    [Test]
    public void Deadlines_AreThirtyFortyFiftySeconds()
    {
        (First / ServerClock.TicksPerSecond).Should().Be(30, "the first slot opens at 30 s");
        ((First + GroundItemPickupRules.SlotStepTicks) / ServerClock.TicksPerSecond).Should().Be(40,
            "the second slot opens at 40 s");
        ((First + 2 * GroundItemPickupRules.SlotStepTicks) / ServerClock.TicksPerSecond).Should().Be(50,
            "the order exhausted, everybody takes at 50 s");
        GroundItemPickupRules.Slots.Should().Be(3, "a TS_ITEM_PICK_UP_ORDER has three slots");
    }

    /// <summary>The player or party the order names takes the object the moment it falls, without waiting.</summary>
    [Test]
    public void CanPickUp_AcceptsTheNamedSlotAtOnce()
    {
        GroundItemPickupRules.CanPickUp(0, occupiedSlots: 1, firstSlotNamesMe: true)
            .Should().BeTrue("the entitled player takes as soon as the object falls");
        GroundItemPickupRules.CanPickUp(0, occupiedSlots: 3, firstSlotNamesMe: true)
            .Should().BeTrue("slot 0 is judged first, whatever follows it");
    }

    /// <summary>An empty order is the common loot a raid leaves: it is nobody's and everybody's at once.</summary>
    [Test]
    public void CanPickUp_AcceptsAnEmptyOrderAtOnce()
    {
        GroundItemPickupRules.CanPickUp(0, occupiedSlots: 0, firstSlotNamesMe: false).Should().BeTrue();
    }

    /// <summary>With one slot filled, a stranger waits 30 seconds to the tick, not 3.</summary>
    [Test]
    public void CanPickUp_RefusesAStrangerForTheFirstThreeThousandTicks()
    {
        GroundItemPickupRules.CanPickUp(0, 1, false).Should().BeFalse("t = 0");
        GroundItemPickupRules.CanPickUp(First - 1, 1, false).Should().BeFalse("29.99 s");
        GroundItemPickupRules.CanPickUp(First, 1, false).Should().BeTrue("30.00 s");
        GroundItemPickupRules.CanPickUp(uint.MaxValue, 1, false).Should().BeTrue("a wrapped elapsed count is huge");
    }

    /// <summary>
    /// Each further filled slot that does not name the asker adds ten seconds: the official loop raises its
    /// deadline by 1000 ticks per slot already skipped and only accepts once the three are exhausted.
    /// </summary>
    [Test]
    public void CanPickUp_AddsTenSecondsPerFurtherSlot()
    {
        GroundItemPickupRules.CanPickUp(First + GroundItemPickupRules.SlotStepTicks - 1, 2, false)
            .Should().BeFalse("39.99 s is one tick short of the second slot");
        GroundItemPickupRules.CanPickUp(First + GroundItemPickupRules.SlotStepTicks, 2, false)
            .Should().BeTrue("40.00 s");

        GroundItemPickupRules.CanPickUp(First + 2 * GroundItemPickupRules.SlotStepTicks - 1, 3, false)
            .Should().BeFalse("49.99 s is one tick short of the last slot");
        GroundItemPickupRules.CanPickUp(First + 2 * GroundItemPickupRules.SlotStepTicks, 3, false)
            .Should().BeTrue("50.00 s, the order exhausted, everybody takes");
    }
}
