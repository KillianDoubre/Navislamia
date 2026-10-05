using System;
using System.Buffers.Binary;
using System.Linq;
using FluentAssertions;
using Navislamia.Game.Network.Packets.Enums;
using Navislamia.Game.Services.Combat;
using NUnit.Framework;

namespace Tests.Game;

/// <summary>EF_ADD_ENERGY_ON_ATTACK / ON_BEING_ATTACKED (docs/packet-specs/socle-etats-periodiques-energie.md §5).</summary>
[TestFixture]
public class EnergyProcsTests
{
    // The Epic 7 rows: 41119 (on attack) and 41218 (on being attacked).
    private static readonly decimal[] OnAttack = { 0, 1, 0, 0, 0, 0, 0, 2, 99, 0, 0, 0, 23, 0, 0, 0, 0, 0, 99, 0 };
    private static readonly decimal[] OnBeingAttacked = { 1, 0, 0, 0, 0, 0, 0, 1, 99, 0, 0, 0, 23, 0, 0, 0, 0, 0, 99, 0 };

    private sealed class FixedRandom : Random
    {
        private readonly int _value;
        public FixedRandom(int value) => _value = value;
        public override int Next(int maxValue) => _value;
        public override int Next(int minValue, int maxValue) => _value;
    }

    [TestCase(EnergyProcs.NormalAttack, true)]
    [TestCase(EnergyProcs.Harmful | EnergyProcs.PhysicalSkill, true)]
    [TestCase(EnergyProcs.Harmful | EnergyProcs.MagicalSkill, true)]
    [TestCase(EnergyProcs.Helpful | EnergyProcs.MagicalSkill, false)]
    public void TheAttackTypeMask23_TakesSwingsAndHarmfulSkills(uint type, bool expected) =>
        EnergyProcs.Applies(OnAttack, 5, 101, type, 0, 100, -1, roll: 0).Should().Be(expected);

    [Test]
    public void TheRatioIsVar6PlusVar7TimesTheLevel()
    {
        EnergyProcs.Applies(OnAttack, 5, 101, EnergyProcs.NormalAttack, 0, 100, -1, roll: 9).Should().BeTrue("2 × 5 = 10 %");
        EnergyProcs.Applies(OnAttack, 5, 101, EnergyProcs.NormalAttack, 0, 100, -1, roll: 10).Should().BeFalse("XRandom() % 100 >= ratio");
    }

    [Test]
    public void AWeaponClassList_GatesTheProc()
    {
        var gated = (decimal[])OnAttack.Clone();
        gated[8] = 101; gated[9] = 102;
        EnergyProcs.Applies(gated, 50, 102, EnergyProcs.NormalAttack, 0, 100, -1, 0).Should().BeTrue();
        EnergyProcs.Applies(gated, 50, 201, EnergyProcs.NormalAttack, 0, 100, -1, 0).Should().BeFalse();
    }

    [Test]
    public void TheLearnedPassive_AddsEnergyWithinTheCapacity_AndPublishesIt()
    {
        var procs = new EnergyProcs(new[] { (41119, true, OnAttack), (41218, false, OnBeingAttacked) }, new FixedRandom(0));
        var connection = new StorageTestHarness.FrameConnection(Array.Empty<byte>());
        var client = StorageTestHarness.NewGameClient(connection);
        var info = StorageTestHarness.Session(client);
        info.CharacterHandle = 7;
        info.CharacterName = "Ana";
        info.LearnedSkills[1082] = 3; // SKILL_INCREASE_ENERGY: three energies at most
        info.LearnedSkills[41119] = 2;

        procs.OnAttack(client, EnergyProcs.NormalAttack, 0, -1);
        info.Energy.Count.Should().Be(2, "var0 + var1 × 2");
        procs.OnAttack(client, EnergyProcs.NormalAttack, 0, -1);
        info.Energy.Count.Should().Be(3, "capped by the capacity");
        procs.OnBeingAttacked(client, EnergyProcs.NormalAttack, 0, -1);
        info.Energy.Count.Should().Be(3, "41218 is not learned");

        var energy = connection.Sent.Where(p => BinaryPrimitives.ReadUInt16LittleEndian(p.AsSpan(4, 2))
            == (ushort)GamePackets.TM_SC_ENERGY).ToList();
        energy.Should().HaveCount(2, "a proc that added nothing publishes nothing");
        BinaryPrimitives.ReadInt16LittleEndian(energy[^1].AsSpan(11, 2)).Should().Be(3);
    }
}
