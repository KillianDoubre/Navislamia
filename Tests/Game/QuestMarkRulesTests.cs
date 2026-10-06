using FluentAssertions;
using Navislamia.Game.Network.Packets.Enums;
using Navislamia.Game.Services;
using NUnit.Framework;
using Link = Navislamia.Game.Services.QuestMarkRules.Link;

namespace Tests.Game;

/// <summary>GetStatusCode and StructNPC::Has*Quest (docs/packet-specs/socle-marqueurs-quete.md).</summary>
[TestFixture]
public class QuestMarkRulesTests
{
    private static Link Quest(int type = 101, bool startable = false, bool inProgress = false, bool finishable = false,
        bool start = true, bool progress = true, bool end = true) =>
        new(type, start, progress, end, startable, inProgress, finishable);

    [Test]
    public void The_first_mark_that_holds_wins_hand_in_then_take_then_in_progress()
    {
        QuestMarkRules.Mark(new[] { Quest(startable: true), Quest(finishable: true) })
            .Should().Be(CreatureStatus.NpcHasFinishableQuest);
        QuestMarkRules.Mark(new[] { Quest(inProgress: true), Quest(startable: true) })
            .Should().Be(CreatureStatus.NpcHasStartableQuest);
        QuestMarkRules.Mark(new[] { Quest(inProgress: true) }).Should().Be(CreatureStatus.NpcHasInProgressQuest);
        QuestMarkRules.Mark(new[] { Quest() }).Should().Be(0u);
    }

    [Test]
    public void A_mark_needs_the_link_flag_of_its_kind()
    {
        QuestMarkRules.Mark(new[] { Quest(finishable: true, end: false) }).Should().Be(0u, "another NPC takes it back");
        QuestMarkRules.Mark(new[] { Quest(startable: true, start: false) }).Should().Be(0u);
        QuestMarkRules.Mark(new[] { Quest(inProgress: true, progress: false) }).Should().Be(0u);
    }

    [Test]
    public void A_contract_giver_shows_no_take_mark_while_one_of_its_contracts_runs()
    {
        QuestMarkRules.Mark(new[] { Quest(901, startable: true), Quest(902, startable: true) })
            .Should().Be(CreatureStatus.NpcHasStartableQuest);
        QuestMarkRules.Mark(new[] { Quest(901, startable: true), Quest(901, inProgress: true, progress: false) })
            .Should().Be(0u, "HasStartableQuest: bHasStartableRandom && !bHasProgressRandom");
        QuestMarkRules.Mark(new[] { Quest(901, inProgress: true, progress: false), Quest(101, startable: true) })
            .Should().Be(CreatureStatus.NpcHasStartableQuest, "an ordinary quest is not held back");
    }

    [Test]
    public void The_npc_mask_carries_the_mark()
    {
        Navislamia.Game.Network.Packets.Game.ActorStatus.ForNpc(CreatureStatus.NpcHasFinishableQuest).Should().Be(1u << 10);
        Navislamia.Game.Network.Packets.Game.ActorStatus.ForNpc().Should().Be(0u);
    }
}
