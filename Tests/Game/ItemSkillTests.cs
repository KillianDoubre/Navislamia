using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using FakeItEasy;
using FluentAssertions;
using Microsoft.Extensions.Options;
using Navislamia.Configuration.Options;
using Navislamia.Game.DataAccess.Entities.Arcadia;
using Navislamia.Game.DataAccess.Entities.Enums;
using Navislamia.Game.DataAccess.Entities.Telecaster;
using Navislamia.Game.DataAccess.Repositories.Interfaces;
using Navislamia.Game.Network.Clients;
using Navislamia.Game.Network.Packets;
using Navislamia.Game.Network.Packets.Enums;
using Navislamia.Game.Network.Packets.Game;
using Navislamia.Game.Services;
using Navislamia.Game.Services.Buffs;
using Navislamia.Game.Services.Interfaces;
using Navislamia.Game.Services.Pets;
using Navislamia.Game.Services.Props;
using Navislamia.Game.Services.Stats;

namespace Tests.Game;

/// <summary>
/// Item spells cast for real (StructCreature::onItemUseEffect: CastSkill with the item's handle): casting time, target,
/// no learned gate and no cool-down (StructSkill::Cast, !m_hCastItem), and the four effects the items use that nothing
/// handled: 401 EF_REMOVE_BAD_STATE, 304 EF_CASTING_CANCEL_WITH_ADD_STATE, 6 EF_RESPAWN_MONSTER_WITH_DIFF_CODE and
/// 314 EF_ADD_STATE_BY_USING_ITEM.
/// </summary>
[TestFixture]
public class ItemSkillTests
{
    private static CastableBuffFields Classify(int skillId, int effect, bool harmful = false, int? state = null,
        params decimal[] vars)
    {
        var all = new decimal[20];
        vars.CopyTo(all, 0);
        var row = new CastableSkillRow(skillId, effect, harmful, 1, state, 0, all, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0);
        BuffCatalog.TryClassify(row, out var fields).Should().BeTrue();
        return fields;
    }

    [TestCase(6005, 401, SkillCastKind.RemoveBadState)]
    [TestCase(9627, 304, SkillCastKind.CastingCancel)]
    [TestCase(65061, 6, SkillCastKind.RespawnMonster)]
    public void The_item_only_effects_have_their_kind(int skillId, int effect, SkillCastKind kind) =>
        Classify(skillId, effect).Kind.Should().Be(kind);

    [Test]
    public void A_luna_chip_spell_is_a_state_paid_with_items() =>
        Classify(6010, 314, true, 9903, 690303m, 1m, 2m, 4m).Kind.Should().Be(SkillCastKind.StateByItem);

    // ---- the cast service ----

    private sealed record Harness(SkillCastService Service, GameClient Client, ConnectionInfo Info,
        StorageTestHarness.FrameConnection Connection, IEventAreaWorldEffects World, MonsterWorldState Monsters);

    private const uint MonsterHandle = 0x40000001;

    private static Harness Build(params CastableBuffFields[] skills)
    {
        var catalog = A.Fake<IBuffCatalog>();
        foreach (var skill in skills)
        {
            CastableBuffFields ignored;
            A.CallTo(() => catalog.TryGet(skill.SkillId, out ignored)).Returns(true).AssignsOutAndRefParameters(skill);
        }

        var stats = A.Fake<IStatService>();
        A.CallTo(() => stats.Compute(A<ConnectionInfo>._)).Returns(new CharacterStatResult(
            new StatBlock { MaxHp = 1000f, CastingSpeed = 100f, AttackRange = 50f }, new StatBlock()));
        var repository = A.Fake<IMonsterResourceRepository>();
        A.CallTo(() => repository.GetByIds(A<IReadOnlyCollection<int>>._)).Returns(new[]
        {
            new MonsterResourceEntity { Id = 2101, Level = 1, Hp = 80, Size = 1m, Scale = 1m }
        });
        var monsters = new MonsterWorldState(repository, Options.Create(new MonsterSpawnOptions
        {
            Spawns = { new MonsterSpawnPoint { MonsterId = 2101, X = 10, Y = 0, Count = 1, Radius = 0 } }
        }));
        var world = A.Fake<IEventAreaWorldEffects>();
        var service = new SkillCastService(catalog, stats, A.Fake<IStateCatalog>(), monsters, A.Fake<ICombatService>(),
            A.Fake<IFieldPropCatalog>(), A.Fake<IWarpService>(), runTicks: false, worldEffects: world);
        var connection = new StorageTestHarness.FrameConnection(Array.Empty<byte>());
        var client = StorageTestHarness.NewGameClient(connection);
        var info = StorageTestHarness.Session(client);
        info.CharacterHandle = 1;
        info.CharacterHp = 500;
        info.CharacterMp = 100;
        info.X = 0;
        info.Y = 0;
        info.DestinationX = 0;
        info.DestinationY = 0;
        lock (info.MonsterVisibilityLock) info.SpawnedMonsters[0] = MonsterHandle;
        service.Register(client);
        return new Harness(service, client, info, connection, world, monsters);
    }

    private static List<SkillPacketType> Steps(Harness h) => h.Connection.Sent
        .Where(p => BinaryPrimitives.ReadUInt16LittleEndian(p.AsSpan(4, 2)) == (ushort)GamePackets.TM_SC_SKILL)
        .Select(p => (SkillPacketType)p[31]).ToList();

    private static CastableBuffFields Fields(int skillId, SkillCastKind kind, int effect, decimal delayCast = 0m,
        decimal cooldown = 0m, int state = 0, params decimal[] vars)
    {
        var all = new decimal[20];
        vars.CopyTo(all, 0);
        return new CastableBuffFields(skillId, kind, state, 0, all, 10m, 0m, 1, 0m, 0, 0, delayCast, 0m, 0m, cooldown, 0m,
            0, EffectType: effect, CastRange: 20, Cancelable: true, ProbabilityOnHit: 100);
    }

    [Test]
    public void Item_spells_that_need_a_real_cast_are_told_apart()
    {
        var h = Build(Fields(10009, SkillCastKind.Debuff, 301, 0.5m, state: 7050),
            Fields(10015, SkillCastKind.Buff, 301, 0.5m, state: 4558),
            Fields(6047, SkillCastKind.Buff, 301, state: 4031),
            Fields(64809, SkillCastKind.PhysicalAttack, 30011));

        h.Service.IsCastItemSkill(10009).Should().BeTrue("a harmful state lands on a target");
        h.Service.IsCastItemSkill(10015).Should().BeTrue("a buff with a casting time is cast");
        h.Service.IsCastItemSkill(6047).Should().BeFalse("an instant buff stays on the instant path");
        h.Service.IsCastItemSkill(64809).Should().BeTrue("a bomb hits a target");
    }

    [Test]
    public void An_item_spell_needs_no_learning_and_arms_no_cool_down()
    {
        var h = Build(Fields(10015, SkillCastKind.Buff, 301, 0.5m, cooldown: 5m, state: 4558));
        var now = ServerClock.Now;

        h.Service.CastItemSkill(h.Client, 10015, 1, 0, 77, null).Should().Be(ResultCode.Success);
        Steps(h).Should().Equal(SkillPacketType.Casting);
        h.Info.SkillCooldowns.Should().BeEmpty();

        h.Service.ProcessCasts(unchecked(now + 60));
        Steps(h).Should().Equal(SkillPacketType.Casting, SkillPacketType.Fire, SkillPacketType.Complete);
        h.Connection.Sent.Select(p => BinaryPrimitives.ReadUInt16LittleEndian(p.AsSpan(4, 2))).Should()
            .NotContain((ushort)GamePackets.TM_SC_SKILL_LIST, "the spell's cool-down is not the item's");

        // A second use right away is not held by the spell's cool-down either.
        h.Service.CastItemSkill(h.Client, 10015, 1, 0, 77, null).Should().Be(ResultCode.Success);
    }

    [Test]
    public void A_cure_scroll_removes_the_bad_state_up_to_its_level()
    {
        var h = Build(Fields(6005, SkillCastKind.RemoveBadState, 401, vars: new[] { 6003m, 0m, 1m }));
        var now = ServerClock.Now;
        lock (h.Info.BuffLock)
        {
            h.Info.ActiveBuffs.Add(new ActiveBuff(1, 6003, 0, 2, now, unchecked(now + 10000)));
        }

        h.Service.CastItemSkill(h.Client, 6005, 1, 0, 77, null);
        lock (h.Info.BuffLock) h.Info.ActiveBuffs.Should().Contain(b => b.StateId == 6003, "level 2 is above level 1's cap");

        h.Service.CastItemSkill(h.Client, 6005, 2, 0, 77, null);
        lock (h.Info.BuffLock) h.Info.ActiveBuffs.Should().NotContain(b => b.StateId == 6003);
    }

    [Test]
    public void A_summon_scroll_brings_the_monster_of_its_level_at_the_user()
    {
        var h = Build(Fields(65061, SkillCastKind.RespawnMonster, 6, vars: new[] { 710m, 711m, 712m, 713m }));

        h.Service.CastItemSkill(h.Client, 65061, 3, 0, 77, null).Should().Be(ResultCode.Success);

        A.CallTo(() => h.World.SpawnMonsters(712, 1, 0f, 0f, 0, 0u)).MustHaveHappenedOnceExactly();
    }

    [Test]
    public void A_spell_breaker_stops_a_monster_cast_of_its_family()
    {
        var h = Build(Fields(9627, SkillCastKind.CastingCancel, 304, vars: new[] { 99m, 100m, 100m, 2m }));
        h.Monsters.BeginCast(0, magical: true);

        h.Service.CastItemSkill(h.Client, 9627, 1, MonsterHandle, 77, null).Should().Be(ResultCode.Success);

        h.Monsters.IsCastBroken(0).Should().BeTrue();
        h.Monsters.EndCast(0).Should().BeTrue("the monster skill service then sends its ST_Cancel");
    }

    [TestCase(MonsterWorldState.AnyCast, true)]
    [TestCase(MonsterWorldState.MagicalCast, true)]
    [TestCase(MonsterWorldState.PhysicalCast, false)]
    public void A_cast_breaker_matches_the_spell_family(int family, bool breaks)
    {
        var h = Build();
        h.Monsters.BeginCast(0, magical: true);
        h.Monsters.TryInterruptCast(0, family).Should().Be(breaks);
        h.Monsters.TryInterruptCast(5, MonsterWorldState.AnyCast).Should().BeFalse("nothing is being cast there");
    }

    [Test]
    public void The_luna_chip_price_is_var_level_units_of_item_var0()
    {
        var h = Build(Fields(6010, SkillCastKind.StateByItem, 314, state: 9903,
            vars: new[] { 690303m, 1m, 2m, 4m, 7m }));
        h.Service.TryGetItemCost(6010, 3, out var code, out var count).Should().BeTrue();
        code.Should().Be(690303);
        count.Should().Be(4);
        h.Service.TryGetItemCost(6010, 20, out _, out _).Should().BeFalse("levels run 1 to 19");
    }

    [Test]
    public void A_luna_chip_spell_cannot_be_asked_by_a_400()
    {
        var h = Build(Fields(6010, SkillCastKind.StateByItem, 314, state: 9903, vars: new[] { 690303m, 1m }));
        h.Info.LearnedSkills[6010] = 1;
        h.Service.Cast(h.Client, new GameActionPackets.SkillRequest(6010, 1, MonsterHandle, 0, 0, 0, 0, 1));
        BinaryPrimitives.ReadUInt16LittleEndian(h.Connection.Sent.Single().AsSpan(52, 2))
            .Should().Be((ushort)ResultCode.AccessDenied);
    }

    // ---- the item use: the price of a 314 ----

    [Test]
    public async Task A_luna_chip_without_its_price_is_refused_and_spends_nothing()
    {
        var (service, client, characters, states, connection) = Using(carried: 3);

        await service.UseAsync(client, new GameActionPackets.UseItemRequest(77, 1));

        A.CallTo(() => states.CastItemSkill(A<GameClient>._, A<int>._, A<int>._, A<uint>._, A<uint>._,
            A<Action<GameClient>>._)).MustNotHaveHappened();
        A.CallTo(() => characters.ConsumeItemAsync(A<string>._, A<uint>._, A<long>._)).MustNotHaveHappened();
        var result = connection.Sent.First(p => BinaryPrimitives.ReadUInt16LittleEndian(p.AsSpan(4, 2)) == 0);
        BinaryPrimitives.ReadUInt16LittleEndian(result.AsSpan(9, 2)).Should().Be((ushort)ResultCode.NotEnoughItem);
    }

    [Test]
    public async Task A_luna_chip_with_its_price_spends_it_when_the_spell_fires()
    {
        var (service, client, characters, states, _) = Using(carried: 10);
        Action<GameClient> fire = null;
        A.CallTo(() => states.CastItemSkill(client, 6010, 3, A<uint>._, 77u, A<Action<GameClient>>._))
            .ReturnsLazily((GameClient _, int _, int _, uint _, uint _, Action<GameClient> f) =>
            {
                fire = f;
                return ResultCode.Success;
            });

        await service.UseAsync(client, new GameActionPackets.UseItemRequest(77, 1));
        A.CallTo(() => characters.ConsumeItemAsync("Tester", 88, A<long>._)).MustNotHaveHappened();

        fire(client);
        await Task.Delay(50);
        A.CallTo(() => characters.ConsumeItemAsync("Tester", 88, 4)).MustHaveHappenedOnceExactly();
    }

    private static (ItemUseService, GameClient, ICharacterService, ISkillCastService, StorageTestHarness.FrameConnection)
        Using(long carried)
    {
        var characters = A.Fake<ICharacterService>();
        var states = A.Fake<ISkillCastService>();
        var pets = A.Fake<IPetSummonService>();
        var repository = A.Fake<IItemResourceRepository>();
        A.CallTo(() => repository.GetUseFields()).Returns(new[]
        {
            new ItemUseFields(690203, 0, 0, ItemBaseType.Supply, OptTypes: new short[] { 5 },
                OptVar1: new decimal[] { 6010 }, OptVar2: new decimal[] { 3 })
        });
        var connection = new StorageTestHarness.FrameConnection(Array.Empty<byte>());
        var client = StorageTestHarness.NewGameClient(connection);
        var info = StorageTestHarness.Session(client);
        info.CharacterHandle = 1;
        info.CharacterName = "Tester";
        info.CharacterLevel = 10;
        info.CharacterHp = 100;
        A.CallTo(() => characters.GetItemByHandleAsync("Tester", 77)).Returns(Task.FromResult(
            new ItemEntity { Id = 77, ItemResourceId = 690203, Amount = 1 }));
        A.CallTo(() => characters.GetCarriedItemsAsync("Tester")).Returns(Task.FromResult(new[]
        {
            new ItemEntity { Id = 88, ItemResourceId = 690303, Amount = carried, WearInfo = ItemWearType.None }
        }));
        A.CallTo(() => characters.ConsumeItemAsync("Tester", A<uint>._, A<long>._)).Returns(Task.FromResult<long?>(1));
        A.CallTo(() => pets.TryUseCageAsync(A<GameClient>._, A<long>._, A<uint>._)).Returns(Task.FromResult(false));
        A.CallTo(() => states.IsCastItemSkill(6010)).Returns(true);
        int code;
        long count;
        A.CallTo(() => states.TryGetItemCost(6010, 3, out code, out count)).Returns(true)
            .AssignsOutAndRefParameters(690303, 4L);
        return (new ItemUseService(characters, new ItemUseCatalog(repository), pets, states, A.Fake<IStatService>()),
            client, characters, states, connection);
    }
}
