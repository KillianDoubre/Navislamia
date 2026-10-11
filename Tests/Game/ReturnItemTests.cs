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
/// The Return Scroll and the return feathers (StructCreature::onItemUseEffect, StructPlayer::UseItem,
/// StructSkill::TOWN_PORTAL / RETURN_FEATHER / RETURN_BACK_FEATHER): their spell is cast for real, the scroll is spent
/// at the use, a feather when its spell fires.
/// </summary>
[TestFixture]
public class ReturnItemTests
{
    private const int Scroll = 603001;

    // ---- the catalog ----

    [TestCase(BuffCatalog.TownPortalSkill)]
    [TestCase(BuffCatalog.ReturnFeatherSkill)]
    [TestCase(BuffCatalog.ReturnBackFeatherSkill)]
    public void The_return_spells_are_their_own_kind(int skillId)
    {
        var row = new CastableSkillRow(skillId, 604, false, 1, null, 0, new decimal[20], 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0);
        BuffCatalog.TryClassify(row, out var fields).Should().BeTrue();
        fields.Kind.Should().Be(SkillCastKind.Return);
    }

    // ---- the cast ----

    private static CastableBuffFields TownPortal() =>
        new(BuffCatalog.TownPortalSkill, SkillCastKind.Return, 0, 0, new decimal[20], 0m, 0m, 0, 0m, 0, 0, 8m, 0m, 0m,
            0m, 0m, 0, CastRange: 0, EffectType: 604, Cancelable: true);

    private static (SkillCastService Service, GameClient Client, ConnectionInfo Info,
        StorageTestHarness.FrameConnection Connection, IWarpService Warp) Caster()
    {
        var catalog = A.Fake<IBuffCatalog>();
        CastableBuffFields ignored;
        A.CallTo(() => catalog.TryGet(BuffCatalog.TownPortalSkill, out ignored)).Returns(true)
            .AssignsOutAndRefParameters(TownPortal());
        var stats = A.Fake<IStatService>();
        A.CallTo(() => stats.Compute(A<ConnectionInfo>._)).Returns(new CharacterStatResult(
            new StatBlock { MaxHp = 1000f, CastingSpeed = 100f }, new StatBlock()));
        var world = new MonsterWorldState(A.Fake<IMonsterResourceRepository>(), Options.Create(new MonsterSpawnOptions()));
        var warp = A.Fake<IWarpService>();
        var service = new SkillCastService(catalog, stats, A.Fake<IStateCatalog>(), world, A.Fake<ICombatService>(),
            A.Fake<IFieldPropCatalog>(), warp, runTicks: false);
        var connection = new StorageTestHarness.FrameConnection(Array.Empty<byte>());
        var client = StorageTestHarness.NewGameClient(connection);
        var info = StorageTestHarness.Session(client);
        info.CharacterHandle = 1;
        info.CharacterHp = 500;
        info.CharacterMp = 100;
        info.RespawnX = 6650;
        info.RespawnY = 6650;
        service.Register(client);
        return (service, client, info, connection, warp);
    }

    private static List<SkillPacketType> Steps(StorageTestHarness.FrameConnection connection) => connection.Sent
        .Where(p => BinaryPrimitives.ReadUInt16LittleEndian(p.AsSpan(4, 2)) == (ushort)GamePackets.TM_SC_SKILL)
        .Select(p => (SkillPacketType)p[31]).ToList();

    [Test]
    public void An_item_casts_its_return_spell_for_eight_seconds_then_it_fires()
    {
        var h = Caster();
        var now = ServerClock.Now;
        var fired = 0;

        h.Service.CastItemSkill(h.Client, BuffCatalog.TownPortalSkill, 1, 0, 77, _ => fired++)
            .Should().Be(ResultCode.Success);

        Steps(h.Connection).Should().Equal(SkillPacketType.Casting);
        h.Service.ProcessCasts(unchecked(now + 790));
        fired.Should().Be(0, "delay_cast 8 s is 800 ticks");

        h.Service.ProcessCasts(unchecked(now + 810));
        fired.Should().Be(1);
        Steps(h.Connection).Should().Equal(SkillPacketType.Casting, SkillPacketType.Fire, SkillPacketType.Complete);
    }

    [Test]
    public void A_return_spell_cannot_be_asked_by_a_400_since_it_is_never_learned()
    {
        var h = Caster();
        h.Service.Cast(h.Client, new GameActionPackets.SkillRequest(BuffCatalog.TownPortalSkill, 1, 1, 0, 0, 0, 0, 1));
        BinaryPrimitives.ReadUInt16LittleEndian(h.Connection.Sent.Single().AsSpan(52, 2))
            .Should().Be((ushort)ResultCode.AccessDenied);
        h.Info.PendingCast.Should().BeNull();
    }

    [Test]
    public void A_cancelled_return_never_fires()
    {
        var h = Caster();
        var fired = 0;
        h.Service.CastItemSkill(h.Client, BuffCatalog.TownPortalSkill, 1, 0, 77, _ => fired++);

        h.Service.CancelCast(h.Client).Should().BeTrue();
        h.Service.ProcessCasts(unchecked(ServerClock.Now + 1000));

        fired.Should().Be(0);
    }

    [Test]
    public void Town_portal_warps_to_the_return_point()
    {
        var h = Caster();
        h.Service.TownPortal(h.Client);
        A.CallTo(() => h.Warp.Warp(h.Client, 6650f, 6650f, 0)).MustHaveHappenedOnceExactly();
    }

    // ---- the item use ----

    private sealed record Use(ItemUseService Service, GameClient Client, ConnectionInfo Info, ICharacterService Characters,
        ISkillCastService States, StorageTestHarness.FrameConnection Connection)
    {
        public Action<GameClient> Fire { get; set; }
    }

    private static Use Using(int itemCode, int skillId, ResultCode castResult = ResultCode.Success)
    {
        var characters = A.Fake<ICharacterService>();
        var states = A.Fake<ISkillCastService>();
        var pets = A.Fake<IPetSummonService>();
        var repository = A.Fake<IItemResourceRepository>();
        A.CallTo(() => repository.GetUseFields()).Returns(new[]
        {
            new ItemUseFields(itemCode, 0, 0, ItemBaseType.Supply, OptTypes: new short[] { 5 },
                OptVar1: new decimal[] { skillId }, OptVar2: new decimal[] { 1 })
        });
        var connection = new StorageTestHarness.FrameConnection(Array.Empty<byte>());
        var client = StorageTestHarness.NewGameClient(connection);
        var info = StorageTestHarness.Session(client);
        info.CharacterHandle = 1;
        info.CharacterName = "Tester";
        info.CharacterLevel = 10;
        info.CharacterHp = 100;
        info.X = 1000;
        info.Y = 2000;
        info.DestinationX = 1000;
        info.DestinationY = 2000;
        A.CallTo(() => characters.GetItemByHandleAsync("Tester", 77)).Returns(Task.FromResult(new ItemEntity
        {
            Id = 77, ItemResourceId = itemCode, Amount = 3, SocketItemIds = new long[] { 164000, 52000, 0, 0 }
        }));
        A.CallTo(() => characters.ConsumeItemAsync("Tester", 77, 1)).Returns(Task.FromResult<long?>(2));
        A.CallTo(() => pets.TryUseCageAsync(A<GameClient>._, A<long>._, A<uint>._)).Returns(Task.FromResult(false));
        A.CallTo(() => states.IsCastItemSkill(skillId)).Returns(true);
        var use = new Use(new ItemUseService(characters, new ItemUseCatalog(repository), pets, states,
            A.Fake<IStatService>()), client, info, characters, states, connection);
        A.CallTo(() => states.CastItemSkill(client, skillId, 1, A<uint>._, 77u, A<Action<GameClient>>._))
            .ReturnsLazily((GameClient _, int _, int _, uint _, uint _, Action<GameClient> fire) =>
            {
                use.Fire = fire;
                return castResult;
            });
        return use;
    }

    private static ResultCode ResultOf(Use use) => (ResultCode)BinaryPrimitives.ReadUInt16LittleEndian(use.Connection.Sent
        .First(p => BinaryPrimitives.ReadUInt16LittleEndian(p.AsSpan(4, 2)) == 0).AsSpan(9, 2));

    [Test]
    public async Task The_Return_Scroll_starts_its_cast_is_spent_at_the_use_and_portals_when_it_fires()
    {
        var u = Using(Scroll, BuffCatalog.TownPortalSkill);

        await u.Service.UseAsync(u.Client, new GameActionPackets.UseItemRequest(77, 1));

        ResultOf(u).Should().Be(ResultCode.Success);
        A.CallTo(() => u.Characters.ConsumeItemAsync("Tester", 77, 1)).MustHaveHappenedOnceExactly();
        A.CallTo(() => u.States.ApplyItemSkill(A<GameClient>._, A<int>._, A<int>._, A<uint>._)).MustNotHaveHappened();
        A.CallTo(() => u.States.TownPortal(A<GameClient>._)).MustNotHaveHappened();

        u.Fire(u.Client);
        A.CallTo(() => u.States.TownPortal(u.Client)).MustHaveHappenedOnceExactly();
    }

    [Test]
    public async Task A_refused_cast_spends_nothing_and_answers_its_code()
    {
        var u = Using(Scroll, BuffCatalog.TownPortalSkill, ResultCode.NotActable);

        await u.Service.UseAsync(u.Client, new GameActionPackets.UseItemRequest(77, 1));

        ResultOf(u).Should().Be(ResultCode.NotActable);
        A.CallTo(() => u.Characters.ConsumeItemAsync(A<string>._, A<uint>._, A<long>._)).MustNotHaveHappened();
    }

    [Test]
    public async Task A_return_feather_is_spent_when_it_fires_and_leaves_its_back_feather()
    {
        var u = Using(ReturnItems.ReturnFeather, BuffCatalog.ReturnFeatherSkill);
        A.CallTo(() => u.Characters.AddItemAsync("Tester", ReturnItems.ReturnBackFeather, 1, A<long[]>._))
            .Returns(Task.FromResult(new ItemEntity { Id = 78, ItemResourceId = ReturnItems.ReturnBackFeather, Amount = 1 }));

        await u.Service.UseAsync(u.Client, new GameActionPackets.UseItemRequest(77, 1));
        A.CallTo(() => u.Characters.ConsumeItemAsync(A<string>._, A<uint>._, A<long>._)).MustNotHaveHappened();

        u.Fire(u.Client);
        await Task.Delay(50);

        A.CallTo(() => u.Characters.ConsumeItemAsync("Tester", 77, 1)).MustHaveHappenedOnceExactly();
        A.CallTo(() => u.Characters.AddItemAsync("Tester", ReturnItems.ReturnBackFeather, 1,
            A<long[]>.That.Matches(s => s[0] == 1000 && s[1] == 2000 && s[2] == 0))).MustHaveHappenedOnceExactly();
        A.CallTo(() => u.States.TownPortal(u.Client)).MustHaveHappenedOnceExactly();
    }

    [Test]
    public async Task A_return_back_feather_brings_back_to_its_position_then_is_spent()
    {
        var u = Using(ReturnItems.ReturnBackFeather, BuffCatalog.ReturnBackFeatherSkill);

        await u.Service.UseAsync(u.Client, new GameActionPackets.UseItemRequest(77, 1));
        A.CallTo(() => u.Characters.ConsumeItemAsync(A<string>._, A<uint>._, A<long>._)).MustNotHaveHappened();

        u.Fire(u.Client);
        await Task.Delay(50);

        A.CallTo(() => u.States.ReturnTo(u.Client, 164000f, 52000f, 0)).MustHaveHappenedOnceExactly();
        A.CallTo(() => u.Characters.ConsumeItemAsync("Tester", 77, 1)).MustHaveHappenedOnceExactly();
    }
}
