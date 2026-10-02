using System.Buffers.Binary;
using FakeItEasy;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Navislamia.Configuration.Options;
using Navislamia.Game.DataAccess.Entities.Arcadia;
using Navislamia.Game.DataAccess.Repositories.Interfaces;
using Navislamia.Game.Network.Clients;
using Navislamia.Game.Network.Packets.Enums;
using Navislamia.Game.Scripting;
using Navislamia.Game.Services;
using Navislamia.Game.Services.Progression;

namespace Tests.Game;

[TestFixture]
public class MonsterProgressionTests
{
    private static GameClient Player(uint handle, long? party = null)
    {
        var client = StorageTestHarness.NewGameClient(new StorageTestHarness.FrameConnection(Array.Empty<byte>()));
        StorageTestHarness.Session(client).CharacterHandle = handle;
        StorageTestHarness.Session(client).PartyId = party;
        return client;
    }

    internal static MonsterWorldState World(bool raid = false, byte layer = 0)
    {
        var repo = A.Fake<IMonsterResourceRepository>();
        A.CallTo(() => repo.GetByIds(A<IReadOnlyCollection<int>>._)).Returns(new[]
        { new MonsterResourceEntity { Id = 2101, Level = 1, Hp = 80, RunSpeed = 120 } });
        return new MonsterWorldState(repo, Options.Create(new MonsterSpawnOptions
        { Spawns = { new MonsterSpawnPoint { MonsterId = 2101, Count = 1, X = 1000, Y = 1000,
            IsDungeonRaidMonster = raid, Layer = layer } } }));
    }

    [Test]
    public void Rewards_follow_actual_damage_first_largest_and_last_group()
    {
        var first = Player(1); var last = Player(2);
        var ledger = new MonsterDamageLedger(new[] { (first, 20L), (last, 80L) }, first, 100);
        var shares = MonsterContribution.Resolve(ledger, last, 101);
        shares.Single(s => s.Representative == first).Factor.Should().BeApproximately(.4, 1e-9);
        shares.Single(s => s.Representative == last).Factor.Should().BeApproximately(.6, 1e-9);
        MonsterContribution.Scale(1000, shares[0].Factor).Should().Be(600);
    }

    [Test]
    public void First_hit_bonus_moves_after_one_minute_including_clock_wrap()
    {
        var first = Player(1); var last = Player(2); var tick = uint.MaxValue - 100;
        var ledger = new MonsterDamageLedger(new[] { (first, 20L), (last, 80L) }, first, tick);
        MonsterContribution.Resolve(ledger, last, unchecked(tick + 6000)).Last().Factor.Should().BeApproximately(.4, 1e-9);
        MonsterContribution.Resolve(ledger, last, unchecked(tick + 6001)).Last().Factor.Should().BeApproximately(.1, 1e-9);
    }

    [Test]
    public void Party_damage_is_combined_and_a_disconnected_contributor_keeps_the_denominator()
    {
        var first = Player(1, 10); var ally = Player(2, 10); var other = Player(3);
        var ledger = new MonsterDamageLedger(new[] { (first, 10L), (ally, 50L), (other, 40L) }, first, 100);
        var shares = MonsterContribution.Resolve(ledger, other, 101);
        shares.Should().HaveCount(2);
        shares[0].Factor.Should().BeApproximately(.7, 1e-9);
        shares[1].Factor.Should().BeApproximately(.3, 1e-9);
        StorageTestHarness.Session(other).CharacterHandle = 0;
        MonsterContribution.Resolve(ledger, first, 101).Single().Factor.Should().BeApproximately(.8, 1e-9);
    }

    [Test]
    public void Ledger_counts_no_overkill_negative_damage_or_hits_on_a_corpse_and_is_consumed_once()
    {
        var world = World(); var first = Player(1); var last = Player(2);
        world.ApplyDamage(0, -4, first, 99).Should().Be(100);
        world.ApplyDamage(0, 20, first, 100).Should().Be(80);
        world.ApplyDamage(0, 1000, last, 101).Should().Be(0);
        world.TryKill(0, DateTime.UtcNow).Should().BeTrue();
        world.ApplyDamage(0, 500, first, 102).Should().Be(0);
        var ledger = world.TakeDamageContributions(0)!;
        ledger.FirstAttacker.Should().Be(first); ledger.FirstTick.Should().Be(100);
        ledger.Damage.Should().BeEquivalentTo(new[] { (first, 20L), (last, 80L) });
        world.TakeDamageContributions(0).Should().BeNull();
        world.CollectRespawns(DateTime.UtcNow.AddSeconds(1));
        world.TakeDamageContributions(0).Should().BeNull();
    }

    [Test]
    public void Raid_speed_and_layer_are_separate_from_the_shared_resource()
    {
        var resource = new MonsterResourceEntity { Id = 2101, Level = 1, Hp = 80, RunSpeed = 120 };
        var instances = MonsterInstanceFactory.Build(new[]
        {
            new MonsterSpawnPoint { MonsterId = 2101, Count = 1 },
            new MonsterSpawnPoint { MonsterId = 2101, Count = 1, IsDungeonRaidMonster = true, Layer = 7 }
        }, new[] { resource });
        instances[0].Combat.Plain.MoveSpeed.Should().Be(120);
        instances[1].Combat.Plain.MoveSpeed.Should().Be(180);
        instances[0].Hp.Should().Be(instances[1].Hp);
        instances[1].Layer.Should().Be(7); instances[1].IsDungeonRaidMonster.Should().BeTrue();
    }

    [TestCase(2, 0, 0, false)]
    [TestCase(3, 1, 1, false)]
    [TestCase(930, 0, 0, true)]
    [TestCase(31, 0, -1, false)]
    public void Reconstructed_Lua_uses_only_imported_slots_and_the_expected_target(int code, int trigger, int expected, bool self)
    {
        var calls = new List<(int Slot, uint Target)>();
        var scripts = new ScriptService(NullLogger<ScriptService>.Instance);
        scripts.RunMonsterTrigger("trigger", new MonsterScriptContext
        {
            MonsterHandle = 50, TargetHandle = 60, MonsterId = code, TriggerIndex = trigger,
            CastSkill = (slot, target, _, _) => { calls.Add((slot, target)); return true; }
        }).Should().BeTrue();
        if (expected < 0) calls.Should().BeEmpty();
        else calls.Should().Equal((expected, self ? 50u : 60u));
        scripts.RunString("assert(get_monster_id(50)==0)").Should().Be(1);
    }

    [Test]
    public void Raid_flag_is_passed_to_Lua_and_context_is_restored()
    {
        var scripts = new ScriptService(NullLogger<ScriptService>.Instance);
        scripts.RunString("function raid_check(a,b,c,x,y,l,raid) assert(l==7 and raid==1) end");
        scripts.RunMonsterTrigger("raid_check", new MonsterScriptContext { Layer = 7, IsDungeonRaidMonster = true }).Should().BeTrue();
    }

    [Test]
    public void Enter_precedes_unexpired_states_uses_observer_handle_and_replays_on_reentry()
    {
        var world = World(); var now = ServerClock.Now;
        var timed = world.AddState(0, 500, 1, 3, now, now + 100000);
        // A zero or past deadline is expired for the 500 ms tick (MonsterWorldState.RemoveExpiredStates), so it is
        // not announced: the client would otherwise keep an icon the server is about to drop.
        world.AddState(0, 501, 1, 1, now, 0);
        world.AddState(0, 502, 1, 1, now, now == 0 ? uint.MaxValue : now - 1);
        var wire = new StorageTestHarness.FrameConnection(Array.Empty<byte>());
        var client = StorageTestHarness.NewGameClient(wire);
        StorageTestHarness.Session(client).CharacterHandle = 1; StorageTestHarness.Session(client).X = 1000; StorageTestHarness.Session(client).Y = 1000;
        StorageTestHarness.Session(client).ClientClockOffset = 100;
        var service = new MonsterSpawnService(world); service.Sync(client);
        wire.Sent.Should().HaveCount(2);
        BinaryPrimitives.ReadUInt16LittleEndian(wire.Sent[0].AsSpan(4)).Should().Be((ushort)GamePackets.TM_SC_ENTER);
        BinaryPrimitives.ReadUInt16LittleEndian(wire.Sent[1].AsSpan(4)).Should().Be(505);
        BinaryPrimitives.ReadUInt32LittleEndian(wire.Sent[1].AsSpan(7)).Should().Be(StorageTestHarness.Session(client).GetMonsterHandle(0));
        // Server ticks, like every other 505 (the buffs' countdown is validated in game on that base).
        BinaryPrimitives.ReadUInt32LittleEndian(wire.Sent[1].AsSpan(19)).Should().Be(timed.EndTick);
        service.Sync(client); wire.Sent.Should().HaveCount(2);
        StorageTestHarness.Session(client).X = 100000; service.Sync(client);
        StorageTestHarness.Session(client).X = 1000; service.Sync(client);
        wire.Sent.Should().HaveCount(5, "a leave, then the enter and its state again");
    }

    [TestCase(false, 0, 0, 100, 10, 0)]
    [TestCase(false, 100000, 0, 200, 20, 93007)]
    [TestCase(true, 100000, .5, 250, 25, 93007)]
    [TestCase(true, 0, .5, 150, 15, 0)]
    public void Dungeon_and_stamina_bonuses_apply_after_base_share(bool dungeon, int stamina, decimal dungeonRate,
        long expectedExp, long expectedJp, int remaining)
    {
        var bonus = MonsterRewardBonuses.Apply(100, 10, stamina, 5, 1, dungeon, dungeonRate);
        bonus.Exp.Should().Be(expectedExp); bonus.Jp.Should().Be(expectedJp); bonus.Stamina.Should().Be(remaining);
    }

    [Test]
    public void Stamina_saver_qualifies_at_zero_stamina_and_spends_nothing()
    {
        MonsterRewardBonuses.Apply(100, 10, 0, 5, 1, false, 0, true).Should().Be(new BonusReward(200, 20, 0));
        var cell = ProgressionResources.Official.DungeonCells.First();
        MonsterRewardBonuses.InDungeon(cell.X * 16384f + 1, cell.Y * 16384f + 1).Should().BeTrue();
        MonsterRewardBonuses.InDungeon(float.NaN, 0).Should().BeFalse();
    }
}
