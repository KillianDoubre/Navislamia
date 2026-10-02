using System;
using System.Buffers.Binary;
using System.Linq;
using FluentAssertions;
using Navislamia.Game.Network.Packets.Game;
using Navislamia.Game.Services;

namespace Tests.Game;

[TestFixture]
public class MoralityTests
{
    [TestCase(10, 10, 0, 0, 50)] [TestCase(20, 10, 0, 0, 100)]
    [TestCase(20, 10, -1, 0, 200)] [TestCase(20, 10, -1000, 0, 1000)]
    [TestCase(20, 10, 500, 0, 150)] [TestCase(20, 10, 1001, 0, 200)]
    [TestCase(20, 10, 0, 5, 150)] [TestCase(20, 10, 0, 20, 200)]
    public void Innocent_kill_penalty_accounts_for_level_morality_and_dk_count(int killerLevel, int victimLevel,
        decimal immoral, int dkc, decimal expected) =>
        MoralityRules.KillIncrease(killerLevel, victimLevel, false, 0, immoral, dkc, 10, false, 0).Should().Be(expected);

    [Test]
    public void Pk_server_party_penalty_reproduces_the_official_integer_multiplier()
    {
        MoralityRules.KillIncrease(10, 10, false, 0, 0, 0, 10, true, 2).Should().Be(100);
        MoralityRules.KillIncrease(10, 10, false, 0, 0, 0, 10, true, 1).Should().Be(50);
    }

    [TestCase(100, 99)] [TestCase(1000, 999.5)] [TestCase(0.1, -0.9)] [TestCase(-100, -100)]
    public void Killing_a_sufficient_level_monster_reduces_immorality(decimal before, decimal after) =>
        MoralityRules.AfterMonsterKill(before, 10, 10, 0).Should().Be(after);

    [Test]
    public void Weak_monsters_do_not_reduce_immorality_and_group_reduction_retains_decimals()
    {
        MoralityRules.AfterMonsterKill(100, 9, 10, 0).Should().Be(100);
        MoralityRules.AfterMonsterKill(100, 10, 10, 2).Should().Be(99.3333m);
    }

    [TestCase(0, 90)] [TestCase(10, 91)] [TestCase(99, 99)] [TestCase(100, 99)]
    public void Death_reduces_positive_immorality_according_to_pk_count(int pkc, decimal expected) =>
        MoralityRules.AfterDeath(100, pkc).Should().Be(expected);

    [TestCase(100, 1000, 1000)] [TestCase(101, 910, 1200)] [TestCase(500, 550, 2000)]
    [TestCase(1000, 100, 3000)] [TestCase(1001, 100, 3000)]
    public void Immorality_changes_monster_experience_and_death_loss_at_official_thresholds(decimal immoral,
        long reward, long loss)
    {
        MoralityRules.RewardExperience(1000, immoral).Should().Be(reward);
        MoralityRules.DeathExperience(1000, immoral).Should().Be(loss);
    }

    [Test]
    public void Fractional_points_travel_as_fixed_point_and_threshold_changes_keep_other_status_flags()
    {
        var frames = new StorageTestHarness.FrameConnection(Array.Empty<byte>());
        var client = StorageTestHarness.NewGameClient(frames); var info = StorageTestHarness.Session(client);
        info.IsWalking = true; info.PkMode = true;
        MoralityRules.Set(client, 999.9999m);
        MoralityRules.Set(client, 1000m);
        var property = frames.Sent.Last(f => f.Length == 37);
        BinaryPrimitives.ReadInt64LittleEndian(property.AsSpan(28)).Should().Be(10000000);
        var mask = ActorStatus.ForPlayer(info);
        (mask & (1u << 11)).Should().NotBe(0); (mask & (1u << 12)).Should().NotBe(0);
        (mask & (1u << 13)).Should().NotBe(0); (mask & (1u << 16)).Should().NotBe(0);
        MoralityRules.Set(client, 99.5m);
        (ActorStatus.ForPlayer(info) & ((1u << 12) | (1u << 13))).Should().Be(0);
        MoralityRules.WireValue(-0.9m).Should().Be(-9000);
    }
}
