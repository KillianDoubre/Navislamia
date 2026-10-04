using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Linq;
using FakeItEasy;
using FluentAssertions;
using Microsoft.Extensions.Options;
using Navislamia.Configuration.Options;
using Navislamia.Game.Network.Clients;
using Navislamia.Game.Network.Packets;
using Navislamia.Game.Network.Packets.Game;
using Navislamia.Game.Network.Packets.Enums;
using Navislamia.Game.Services;
using Navislamia.Game.Services.Interfaces;
using Navislamia.Game.Services.Stats;
using Navislamia.Game.DataAccess.Repositories.Interfaces;
using Navislamia.Game.Services.Buffs;
using Navislamia.Game.Services.Props;

namespace Tests.Game;

/// <summary>The HuntaHolic healing props' skills and the layer props (docs/packet-specs/socle-huntaholic.md §8).</summary>
[TestFixture]
public class PropHealTests
{
    /// <summary>The Epic 7 rows: 64807 (9502) and 64806 (9503), var1..var20 as GetVar(0..19).</summary>
    private static CastableBuffFields Skill(int id, int effect, params decimal[] vars)
    {
        var all = new decimal[20];
        vars.CopyTo(all, 0);
        var row = new CastableSkillRow(id, effect, false, 1, null, 0, all, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0);
        BuffCatalog.TryClassify(row, out var fields).Should().BeTrue();
        return fields;
    }

    private static CastableBuffFields RegionHeal() => Skill(64807, 9502, 0m, 0m, 0.3m, 0m, 30m, 2m);
    private static CastableBuffFields AreaHeal() => Skill(64806, 9503, 0.05m, 0m, 0m, 0m, 30m, 3m, 2m, 30m);

    [Test]
    public void BothHealingPropSkillsAreCastAtTheProp()
    {
        RegionHeal().Kind.Should().Be(SkillCastKind.PropHeal);
        AreaHeal().Kind.Should().Be(SkillCastKind.PropHeal);
    }

    [Test]
    public void TheRegionHealIsThirtyPercentWithinThirtyMetresOfAllies()
    {
        PropHealRules.Region(RegionHeal(), 1).Should()
            .Be(new RegionPropHeal(0, 0.3f, 360f, PropHealTargets.OnlyAlly));
    }

    [Test]
    public void TheAreaHealsFivePercentEveryThreeSecondsForThirtySeconds()
    {
        var area = PropHealRules.Area(AreaHeal(), 1);

        area.Should().Be(new AreaPropHeal(0.05f, 0f, 360f, 300, PropHealTargets.OnlyAlly, 3000));
        PropHealRules.AreaFireCount(area).Should().Be(11, "it fires at once, then until current_time > end");
    }

    [TestCase(0, 0.3f, 100, 1000, 300)]
    [TestCase(0, 0.3f, 900, 1000, 100)]
    [TestCase(0, 0.3f, 1000, 1000, 0)]
    [TestCase(50, 0f, 0, 1000, 50)]
    public void AHealIsBoundedByTheMissingHp(int flat, float ratio, int current, int max, int healed)
    {
        PropHealRules.Healed(flat, ratio, current, max).Should().Be(healed);
    }

    [TestCase(PropHealTargets.OnlyAlly, true, 0, 0, true)]
    [TestCase(PropHealTargets.OnlyAlly, false, 7, 7, true)]
    [TestCase(PropHealTargets.OnlyAlly, false, 7, 8, false)]
    [TestCase(PropHealTargets.OnlyAlly, false, 0, 0, false)]
    [TestCase(PropHealTargets.AnyBody, false, 0, 0, true)]
    [TestCase(PropHealTargets.OnlyEnemy, true, 0, 0, false)]
    public void TheTargetLimitIsThePartyForAllies(PropHealTargets targets, bool isCaster, long casterParty, long party,
        bool reached)
    {
        PropHealRules.Reaches(targets, isCaster, casterParty, party).Should().Be(reached);
    }

    private sealed class Harness
    {
        public readonly DynamicFieldProps Props = new();
        public readonly PlayerRegistry Registry = new();
        public readonly SkillCastService Service;
        public readonly Dictionary<GameClient, StorageTestHarness.FrameConnection> Wires = new();
        private readonly int _skillId;

        public Harness(CastableBuffFields fields)
        {
            _skillId = fields.SkillId;
            var catalog = A.Fake<IBuffCatalog>();
            A.CallTo(() => catalog.TryGet(fields.SkillId, out fields)).Returns(true).AssignsOutAndRefParameters(fields);
            A.CallTo(() => catalog.Count).Returns(1);
            var stats = A.Fake<IStatService>();
            A.CallTo(() => stats.Compute(A<ConnectionInfo>._)).Returns(
                new CharacterStatResult(new StatBlock { MaxHp = 1000, MaxMp = 500 }, new StatBlock()));
            var players = A.Fake<IPlayerVisibilityService>();
            A.CallTo(() => players.Registry).Returns(Registry);
            var world = new MonsterWorldState(A.Fake<IMonsterResourceRepository>(), Options.Create(new MonsterSpawnOptions()));
            Service = new SkillCastService(catalog, stats, A.Fake<IStateCatalog>(), world, A.Fake<ICombatService>(),
                A.Fake<IFieldPropCatalog>(), A.Fake<IWarpService>(), players, runTicks: false, dynamicProps: Props);
        }

        public GameClient Player(uint handle, float x, long? party, byte layer = 1)
        {
            var wire = new StorageTestHarness.FrameConnection(Array.Empty<byte>());
            var client = StorageTestHarness.NewGameClient(wire);
            Wires[client] = wire;
            var info = StorageTestHarness.Session(client);
            info.CharacterHandle = handle;
            info.CharacterName = "P" + handle;
            info.CharacterLevel = 30;
            info.CharacterHp = 500;
            info.CharacterMaxHp = 1000;
            info.CharacterMp = 100;
            info.PartyId = party;
            info.Layer = layer;
            info.X = info.DestinationX = x;
            Registry.Register(handle, client);
            Service.Register(client);
            return client;
        }

        /// <summary>The prop as this client sees it: handle 0x40000001.</summary>
        public void Sees(GameClient client, DynamicFieldProp prop)
        {
            var info = StorageTestHarness.Session(client);
            info.SpawnedProps[prop.InstanceId] = 0x40000001;
            info.SpawnedPropInstancesByHandle[0x40000001] = prop.InstanceId;
        }

        public void Cast(GameClient caster) => Service.Cast(caster, new GameActionPackets.SkillRequest((ushort)_skillId,
            StorageTestHarness.Session(caster).CharacterHandle, 0x40000001, 0, 0, 0, 1, 1));
    }

    private static FieldPropTemplate Template(int skillId) =>
        new(300101, skillId, 0, 1, 300, 0, 0, PropAction.None, Array.Empty<PropActivation>());

    [Test]
    public void TheRegionHealUsesThePropAndHealsTheCasterAndItsPartyAroundIt()
    {
        var h = new Harness(RegionHeal());
        var caster = h.Player(1, 0, 9);
        var member = h.Player(2, 300, 9);
        var stranger = h.Player(3, 20, 10);
        var far = h.Player(4, 800, 9);
        var otherLayer = h.Player(5, 10, 9, layer: 0);
        var used = 0;
        var prop = h.Props.Add(300101, 10, 0, 1, Template(64807), _ => used++);
        h.Sees(caster, prop);

        h.Cast(caster);

        used.Should().Be(1);
        h.Props.TryGet(prop.InstanceId, out _).Should().BeFalse("the prop is used up");
        StorageTestHarness.Session(caster).CharacterHp.Should().Be(800, "30 % of 1 000 max HP");
        StorageTestHarness.Session(member).CharacterHp.Should().Be(800);
        StorageTestHarness.Session(stranger).CharacterHp.Should().Be(500, "only allies");
        StorageTestHarness.Session(far).CharacterHp.Should().Be(500, "beyond 30 m");
        StorageTestHarness.Session(otherLayer).CharacterHp.Should().Be(500, "another layer");

        var fire = h.Wires[caster].Sent.Last(frame =>
            BinaryPrimitives.ReadUInt16LittleEndian(frame.AsSpan(4)) == (ushort)GamePackets.TM_SC_SKILL && frame[31] == 0);
        BinaryPrimitives.ReadUInt16LittleEndian(fire.AsSpan(55)).Should().Be(2, "one ADD_HP hit per player healed");
    }

    [Test]
    public void ASecondCastAtAUsedPropFails()
    {
        var h = new Harness(RegionHeal());
        var caster = h.Player(1, 0, 9);
        var prop = h.Props.Add(300101, 10, 0, 1, Template(64807));
        h.Sees(caster, prop);
        h.Props.TryUse(prop.InstanceId, out _);

        h.Cast(caster);

        StorageTestHarness.Session(caster).CharacterHp.Should().Be(500);
    }

    [Test]
    public void TheHealingAreaFiresAtOnce()
    {
        var h = new Harness(AreaHeal());
        var caster = h.Player(1, 0, 9);
        var prop = h.Props.Add(300102, 10, 0, 1, Template(64806));
        h.Sees(caster, prop);

        h.Cast(caster);

        StorageTestHarness.Session(caster).CharacterHp.Should().Be(550, "5 % of 1 000 max HP on the first fire");
    }

    [Test]
    public void ALayerPropIsSeenOnItsLayerAndUsedOnce()
    {
        var props = new DynamicFieldProps();
        var used = 0;
        var prop = props.Add(300101, 100, 100, 3, default, _ => used++);

        prop.InstanceId.Should().BeGreaterThanOrEqualTo(DynamicFieldProps.FirstInstanceId);
        props.Within(100, 100, 3, 1).Should().ContainSingle();
        props.Within(100, 100, 2, 1).Should().BeEmpty();
        props.Within(1000, 1000, 3, 1).Should().BeEmpty();

        props.TryUse(prop.InstanceId, out _).Should().BeTrue();
        props.TryUse(prop.InstanceId, out _).Should().BeFalse();
        used.Should().Be(1);
        props.Within(100, 100, 3, 1).Should().BeEmpty();
    }
}
