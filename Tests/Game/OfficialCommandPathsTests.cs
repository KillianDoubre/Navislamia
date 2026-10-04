using System.Buffers.Binary;
using FakeItEasy;
using FluentAssertions;
using Navislamia.Game.Network.Clients;
using Navislamia.Game.Network.Packets;
using Navislamia.Game.Network.Packets.Game;
using Navislamia.Game.Services;
using Navislamia.Game.Services.Buffs;
using Navislamia.Game.Services.Interfaces;
using Navislamia.Game.Services.Party;
using Navislamia.Game.Services.Rates;
using Navislamia.Game.Services.Stats;

namespace Tests.Game;

public partial class PartyServiceTests
{
    [Test]
    public void Assist_accepts_only_a_real_party_member_and_translates_monster_handles_per_viewer()
    {
        var ana = Player(1, "Ana"); var bo = Player(2, "Bo"); var outsider = Player(3, "Cy");
        _parties.TryHandleCommand(ana, "/pcreate Wolves"); Join(ana, bo); Clear(ana, bo, outsider);
        Info(ana).SpawnedMonsters[7] = 0x40000001;
        Info(bo).SpawnedMonsters[7] = 0x40000099;
        Info(bo).TargetHandle = 0x40000099;
        _parties.TryHandleCommand(ana, "/passist 2").Should().BeTrue();
        Lines(ana).Should().Equal("ASSIST|1073741825|"); Lines(bo).Should().BeEmpty();
        Clear(ana); Info(outsider).PartyId = Info(ana).PartyId;
        _parties.TryHandleCommand(ana, "/passist 3");
        Lines(ana).Should().BeEmpty("a forged session party id is not membership");
        Info(bo).TargetHandle = 123;
        _parties.TryHandleCommand(ana, "/passist 2"); Lines(ana).Should().Equal("ASSIST|123|");
    }
}

public partial class DeathResurrectionTests
{
    [Test]
    public void Official_lv_can_lower_level_without_filling_vitals_and_never_revives_a_dead_player()
    {
        var (levels, client, info, _) = Leveling(); info.CharacterHp = 100; info.CharacterMp = 20;
        levels.SetLevel(client, 3).Should().BeTrue(); info.CharacterLevel.Should().Be(3);
        levels.TryGetExperienceFor(3, out var threshold).Should().BeTrue(); info.CharacterExp.Should().Be(threshold);
        info.CharacterHp.Should().Be(100); info.CharacterMp.Should().Be(20);
        levels.SetLevel(client, 6).Should().BeTrue(); info.CharacterHp.Should().Be(500); info.CharacterMp.Should().Be(50);
        info.CharacterHp = 0; info.CharacterMp = 7;
        levels.SetLevel(client, 7).Should().BeTrue(); info.CharacterHp.Should().Be(0); info.CharacterMp.Should().Be(7);
        levels.SetLevel(client, 999).Should().BeFalse(); info.CharacterLevel.Should().Be(7);
    }

    [Test]
    public void Official_rebirth_is_in_place_adds_max_hp_to_mp_and_keeps_energy_and_experience_loss()
    {
        var (_, client, info, _) = Leveling(); var warp = A.Fake<IWarpService>();
        var stats = A.Fake<IStatService>();
        A.CallTo(() => stats.Compute(info)).Returns(new CharacterStatResult(new StatBlock { MaxHp = 500, MaxMp = 2000 }, new StatBlock()));
        var service = new ResurrectionService(warp, stats, A.Fake<IStateCatalog>(), A.Fake<ISkillCastService>(),
            A.Fake<ICharacterService>(), A.Fake<IResurrectionItemCatalog>());
        info.CharacterHp = 0; info.CharacterMp = 100; info.DeathExpLoss = 15;
        info.Energy.Add(3, 5, ServerClock.Now);
        service.Rebirth(client);
        info.CharacterHp.Should().Be(500); info.CharacterMp.Should().Be(600);
        info.DeathExpLoss.Should().Be(15); info.Energy.Count.Should().Be(3);
        A.CallTo(() => warp.Warp(A<GameClient>._, A<float>._, A<float>._)).MustNotHaveHappened();
    }
}

public partial class CastMechanicsTests
{
    [Test]
    public void A_poison_kill_runs_the_real_combat_death_and_drop_path_only_once()
    {
        var drops = A.Fake<IGroundItemService>(); var levels = A.Fake<ILevelingService>();
        var h = Build(Attack(), states: PeriodicCatalog(2, 600), random: new Rolls(99),
            combatFactory: (world, stats, states) => new CombatService(world, A.Fake<IMonsterSpawnService>(),
                levels, drops, A.Fake<IRateService>(), stats, states, A.Fake<IPartyService>(), runTicks: false));
        h.World.TryAddState(InstanceId, 42, 7001, 1, 100, 1000, Rule(42), _ => Rule(42),
            out _, out _, h.Info.CharacterHandle, new StatePulse(100, 0)).Should().BeTrue();
        h.Service.ProcessPeriodicStates(201); h.Service.ProcessPeriodicStates(401);
        h.World.IsAlive(InstanceId).Should().BeFalse(); h.World.GetHp(InstanceId).Should().Be(0);
        h.World.GetStates(InstanceId).Should().BeEmpty();
        A.CallTo(() => drops.DropForMonster(h.Client, 2101, A<float>._, A<float>._, A<float>._, InstanceId, A<double>._))
            .MustHaveHappenedOnceExactly();
        h.Connection.Sent.Count(p => PacketId(p) == 406).Should().Be(1);
        h.Connection.Sent.Should().Contain(p => PacketId(p) == 500, "the ordinary monster death status is published");
    }

    [Test]
    public void A_player_poison_kill_runs_the_real_death_penalty_only_once()
    {
        var levels = A.Fake<ILevelingService>();
        var h = Build(Attack(), states: PeriodicCatalog(2, 600), random: new Rolls(99),
            combatFactory: (world, stats, states) => new CombatService(world, A.Fake<IMonsterSpawnService>(),
                levels, A.Fake<IGroundItemService>(), A.Fake<IRateService>(), stats, states, A.Fake<IPartyService>(), runTicks: false));
        h.Service.ApplyState(h.Client, 42, 1, 1000);
        var start = h.Info.ActiveBuffs.Single().StartTick;
        h.Service.ProcessPeriodicStates(start + 101); h.Service.ProcessPeriodicStates(start + 202);
        h.Info.CharacterHp.Should().Be(0);
        A.CallTo(() => levels.ApplyDeathPenalty(h.Client)).MustHaveHappenedOnceExactly();
        h.Connection.Sent.Count(p => PacketId(p) == 406).Should().Be(1);
    }

    [Test]
    public void Exact_end_time_keeps_the_last_fire_until_the_following_creature_update()
    {
        var h = Build(Attack(), states: PeriodicCatalog(21));
        h.Service.ApplyState(h.Client, 42, 1, 200);
        var start = h.Info.ActiveBuffs.Single().StartTick;
        h.Service.ProcessBuffs(start + 100); h.Service.ProcessBuffs(start + 101);
        h.Service.ProcessBuffs(start + 200); h.Info.ActiveBuffs.Should().ContainSingle();
        h.Service.ProcessBuffs(start + 202); h.Info.ActiveBuffs.Should().BeEmpty();
        h.Connection.Sent.Last(p => PacketId(p) == 406)[31].Should().Be(1);
    }
}

public partial class SupportSkillTests
{
    private static ushort PulseId(byte[] p) => BinaryPrimitives.ReadUInt16LittleEndian(p.AsSpan(4));
    [Test]
    public void State_results_and_energy_are_visible_to_observers_but_not_unrelated_players()
    {
        var h = new Harness(Fields()); var owner = h.Player(1); var viewer = h.Player(2); var stranger = h.Player(3);
        Info(stranger).SpawnedPlayers.Remove(1);
        A.CallTo(() => h.States.Periodic(42)).Returns(new PeriodicStateRule(42, 21, 1, 0, 0, 0, 10, 0, new decimal[20]));
        h.Service.ApplyState(owner, 42, 1, 1000); foreach (var wire in h.Wires.Values) wire.Sent.Clear();
        h.Service.ProcessPeriodicStates(Info(owner).ActiveBuffs.Single().StartTick + 101);

        h.Wires[owner].Sent.Should().Contain(p => PulseId(p) == 406);
        h.Wires[viewer].Sent.Should().Contain(p => PulseId(p) == 406);
        h.Wires[stranger].Sent.Should().NotContain(p => PulseId(p) == 406);
        Info(owner).Energy.Add(2, 5, ServerClock.Now); EnergyCounter.Publish(owner);
        h.Wires[owner].Sent.Should().Contain(p => PulseId(p) == 515);
        h.Wires[viewer].Sent.Should().Contain(p => PulseId(p) == 515);
        h.Wires[stranger].Sent.Should().NotContain(p => PulseId(p) == 515);
    }
}
