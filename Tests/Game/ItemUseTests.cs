using System;
using System.IO;
using System.Threading.Tasks;
using FakeItEasy;
using FluentAssertions;
using Navislamia.Game.DataAccess.Entities.Enums;
using Navislamia.Game.DataAccess.Entities.Telecaster;
using Navislamia.Game.DataAccess.Repositories.Interfaces;
using Navislamia.Game.Network.Packets.Game;
using Navislamia.Game.Network.Packets;
using Navislamia.Game.Services;
using Navislamia.Game.Services.Pets;
using Navislamia.Game.Services.Stats;

namespace Tests.Game;

/// <summary>
/// The level gate of an item use: the catalog reads <c>use_min_level</c> / <c>use_max_level</c> of
/// the item resource, the rules apply them the way NGemity's <c>Player::IsUseableItem</c> does.
/// </summary>
[TestFixture]
public class ItemUseTests
{
    private static ItemUseCatalog Catalog(params ItemUseFields[] fields)
    {
        var repository = A.Fake<IItemResourceRepository>();
        A.CallTo(() => repository.GetUseFields()).Returns(fields);
        return new ItemUseCatalog(repository);
    }

    [Test]
    public void Catalog_ExposesTheLevelsOfAKnownResource()
    {
        var catalog = Catalog(new ItemUseFields(240100, 10, 0, ItemBaseType.Supply),
            new ItemUseFields(240101, 0, 60, ItemBaseType.Supply));

        catalog.TryGetLevels(240100, out var unbound).Should().BeTrue();
        unbound.MinLevel.Should().Be(10);
        unbound.MaxLevel.Should().Be(0);

        catalog.TryGetLevels(240101, out var capped).Should().BeTrue();
        capped.MinLevel.Should().Be(0);
        capped.MaxLevel.Should().Be(60);
    }

    [Test]
    public void Catalog_LeavesAnUnknownResourceUngated()
    {
        var catalog = Catalog(new ItemUseFields(240100, 10, 0, ItemBaseType.Supply));

        catalog.TryGetLevels(999999, out _).Should().BeFalse();
    }

    [Test]
    public void Catalog_SparesOnlyTheReusableType()
    {
        var catalog = Catalog(new ItemUseFields(240100, 0, 0, ItemBaseType.Supply),
            new ItemUseFields(540017, 0, 0, ItemBaseType.Use));

        catalog.IsConsumedOnUse(240100).Should().BeTrue();
        catalog.IsConsumedOnUse(540017).Should().BeFalse();
    }

    [Test]
    public void Catalog_ConsumesAnUnknownResource()
    {
        Catalog().IsConsumedOnUse(999999).Should().BeTrue();
    }

    [Test]
    public void CheckUseLevel_AcceptsACharacterAtBothBounds()
    {
        ItemUseRules.CheckUseLevel(10, new ItemUseLevels(10, 60)).Should().Be(ResultCode.Success);
        ItemUseRules.CheckUseLevel(60, new ItemUseLevels(10, 60)).Should().Be(ResultCode.Success);
    }

    [Test]
    public void CheckUseLevel_RefusesACharacterBelowTheMinimum()
    {
        ItemUseRules.CheckUseLevel(9, new ItemUseLevels(10, 0)).Should().Be(ResultCode.LimitMin);
    }

    [Test]
    public void CheckUseLevel_RefusesACharacterAboveACeiling()
    {
        ItemUseRules.CheckUseLevel(61, new ItemUseLevels(0, 60)).Should().Be(ResultCode.LimitMax);
    }

    [Test]
    public void CheckUseLevel_TreatsAZeroCeilingOrMinimumAsUnbounded()
    {
        ItemUseRules.CheckUseLevel(1, new ItemUseLevels(0, 0)).Should().Be(ResultCode.Success);
        ItemUseRules.CheckUseLevel(200, new ItemUseLevels(0, 0)).Should().Be(ResultCode.Success);
    }

    [Test]
    public void CheckUseLevel_ReportsTheCeilingFirstWhenBothBoundsRefuse()
    {
        // NGemity tests the ceiling before the floor, so LimitMax wins on a contradictory template.
        ItemUseRules.CheckUseLevel(61, new ItemUseLevels(70, 60)).Should().Be(ResultCode.LimitMax);
    }

    [Test]
    public void RetailCatalogOverridesOlderPotionAndScrollFields()
    {
        var repository = A.Fake<IItemResourceRepository>();
        A.CallTo(() => repository.GetUseFields()).Returns(new[]
        {
            new ItemUseFields(602001, 0, 0, ItemBaseType.Supply,
                OptTypes: new short[] { 1 }, OptVar1: new decimal[] { 150 })
        });
        var path = Path.Combine(TestContext.CurrentContext.TestDirectory, "item-use.73.json");

        var catalog = new ItemUseCatalog(repository, path);

        catalog.TryGetUseFields(602001, out var potion).Should().BeTrue();
        potion.OptVar1![0].Should().Be(80);
        catalog.TryGetUseFields(691101, out var scroll).Should().BeTrue();
        scroll.StateId.Should().Be(4031);
        scroll.StateTime.Should().Be(3600);
    }

    [Test]
    public async Task PotionHealsOnceAndSharedCooldownStopsASecondUse()
    {
        var character = A.Fake<ICharacterService>();
        var states = A.Fake<ISkillCastService>();
        var stats = A.Fake<IStatService>();
        var pets = A.Fake<IPetSummonService>();
        var catalog = Catalog(new ItemUseFields(9000001, 0, 0, ItemBaseType.Supply,
            CoolTime: 60, CoolTimeGroup: 1, OptTypes: new short[] { 1 }, OptVar1: new decimal[] { 30 }));
        var connection = new StorageTestHarness.FrameConnection(Array.Empty<byte>());
        var client = StorageTestHarness.NewGameClient(connection);
        var info = StorageTestHarness.Session(client);
        info.CharacterHandle = 1;
        info.CharacterName = "Tester";
        info.CharacterLevel = 1;
        info.CharacterHp = 50;
        A.CallTo(() => character.GetItemByHandleAsync("Tester", 123))
            .Returns(Task.FromResult(new ItemEntity { Id = 123, ItemResourceId = 9000001, Amount = 2 }));
        A.CallTo(() => character.ConsumeItemAsync("Tester", 123, 1))
            .Returns(Task.FromResult<long?>(1));
        A.CallTo(() => stats.Compute(info)).Returns(new CharacterStatResult(
            new StatBlock { MaxHp = 100, MaxMp = 100 }, new StatBlock()));
        A.CallTo(() => pets.TryUseCageAsync(client, 9000001, 123)).Returns(Task.FromResult(false));
        var service = new ItemUseService(character, catalog, pets, states, stats);

        await service.UseAsync(client, new GameActionPackets.UseItemRequest(123, 1));
        await service.UseAsync(client, new GameActionPackets.UseItemRequest(123, 1));

        info.CharacterHp.Should().Be(80);
        A.CallTo(() => character.ConsumeItemAsync("Tester", 123, 1)).MustHaveHappenedOnceExactly();
        info.ItemCooldowns.Should().ContainKey(-1);
    }

    [Test]
    public async Task ACoolTimeWithoutAGroupDoesNotBlockTheNextUse()
    {
        // NGemity only arms cool_time for a cool_time_group in 1..40: the 217 frame the client counts down
        // has no slot for group 0, so a server-side delay there would refuse a use the client shows ready.
        var character = A.Fake<ICharacterService>();
        var stats = A.Fake<IStatService>();
        var pets = A.Fake<IPetSummonService>();
        var catalog = Catalog(new ItemUseFields(9000003, 0, 0, ItemBaseType.Supply,
            CoolTime: 60, CoolTimeGroup: 0, OptTypes: new short[] { 1 }, OptVar1: new decimal[] { 10 }));
        var client = StorageTestHarness.NewGameClient(
            new StorageTestHarness.FrameConnection(Array.Empty<byte>()));
        var info = StorageTestHarness.Session(client);
        info.CharacterHandle = 1;
        info.CharacterName = "Tester";
        info.CharacterLevel = 1;
        info.CharacterHp = 50;
        A.CallTo(() => character.GetItemByHandleAsync("Tester", 126))
            .Returns(Task.FromResult(new ItemEntity { Id = 126, ItemResourceId = 9000003, Amount = 5 }));
        A.CallTo(() => character.ConsumeItemAsync("Tester", 126, 1)).Returns(Task.FromResult<long?>(4));
        A.CallTo(() => stats.Compute(info)).Returns(new CharacterStatResult(
            new StatBlock { MaxHp = 100, MaxMp = 100 }, new StatBlock()));
        A.CallTo(() => pets.TryUseCageAsync(client, 9000003, 126)).Returns(Task.FromResult(false));
        var service = new ItemUseService(character, catalog, pets, A.Fake<ISkillCastService>(), stats);

        await service.UseAsync(client, new GameActionPackets.UseItemRequest(126, 1));
        await service.UseAsync(client, new GameActionPackets.UseItemRequest(126, 1));

        info.CharacterHp.Should().Be(70);
        info.ItemCooldowns.Should().BeEmpty();
    }

    [Test]
    public async Task AFallFromAMountRefusesItemsBeforeAnythingIsRead()
    {
        // FALL_FROM_SUMMON takes STATUS_ITEM_USABLE away for its 3 s, like a stun.
        var character = A.Fake<ICharacterService>();
        var connection = new StorageTestHarness.FrameConnection(Array.Empty<byte>());
        var client = StorageTestHarness.NewGameClient(connection);
        var info = StorageTestHarness.Session(client);
        info.CharacterHandle = 1;
        info.CharacterName = "Tester";
        var now = ServerClock.Now;
        info.ActiveBuffs.Add(new Navislamia.Game.Services.Buffs.ActiveBuff(1, Navislamia.Game.Services.Creatures.SummonFall.StateId,
            0, 1, now, unchecked(now + Navislamia.Game.Services.Creatures.SummonFall.Duration)));

        await new ItemUseService(character, Catalog(), A.Fake<IPetSummonService>(), A.Fake<ISkillCastService>(),
            A.Fake<IStatService>()).UseAsync(client, new GameActionPackets.UseItemRequest(124, 1));

        A.CallTo(() => character.GetItemByHandleAsync(A<string>._, A<uint>._)).MustNotHaveHappened();
        connection.Sent.Should().ContainSingle().Which.AsSpan(9, 2).ToArray()
            .Should().Equal(BitConverter.GetBytes((ushort)ResultCode.NotActable));
    }

    [Test]
    public async Task ScrollUsesItsSkillAndLevel()
    {
        var character = A.Fake<ICharacterService>();
        var states = A.Fake<ISkillCastService>();
        var pets = A.Fake<IPetSummonService>();
        var catalog = Catalog(new ItemUseFields(9000002, 0, 0, ItemBaseType.Supply,
            OptTypes: new short[] { 5 }, OptVar1: new decimal[] { 6047 },
            OptVar2: new decimal[] { 3 }));
        var client = StorageTestHarness.NewGameClient(
            new StorageTestHarness.FrameConnection(Array.Empty<byte>()));
        var info = StorageTestHarness.Session(client);
        info.CharacterHandle = 1;
        info.CharacterName = "Tester";
        info.CharacterLevel = 1;
        A.CallTo(() => character.GetItemByHandleAsync("Tester", 124))
            .Returns(Task.FromResult(new ItemEntity { Id = 124, ItemResourceId = 9000002, Amount = 1 }));
        A.CallTo(() => character.ConsumeItemAsync("Tester", 124, 1))
            .Returns(Task.FromResult<long?>(0));
        A.CallTo(() => pets.TryUseCageAsync(client, 9000002, 124)).Returns(Task.FromResult(false));

        await new ItemUseService(character, catalog, pets, states, A.Fake<IStatService>())
            .UseAsync(client, new GameActionPackets.UseItemRequest(124, 1));

        A.CallTo(() => states.ApplyItemSkill(client, 6047, 3, 1u)).MustHaveHappenedOnceExactly();
    }

    [Test]
    public async Task RetailPotionSkillRestoresHitPoints()
    {
        var repository = A.Fake<IItemResourceRepository>();
        A.CallTo(() => repository.GetUseFields()).Returns(Array.Empty<ItemUseFields>());
        var catalog = new ItemUseCatalog(repository,
            Path.Combine(TestContext.CurrentContext.TestDirectory, "item-use.73.json"));
        var character = A.Fake<ICharacterService>();
        var pets = A.Fake<IPetSummonService>();
        var stats = A.Fake<IStatService>();
        var client = StorageTestHarness.NewGameClient(
            new StorageTestHarness.FrameConnection(Array.Empty<byte>()));
        var info = StorageTestHarness.Session(client);
        info.CharacterHandle = 1;
        info.CharacterName = "Tester";
        info.CharacterLevel = 1;
        info.CharacterHp = 50;
        A.CallTo(() => character.GetItemByHandleAsync("Tester", 125))
            .Returns(Task.FromResult(new ItemEntity { Id = 125, ItemResourceId = 608001, Amount = 1 }));
        A.CallTo(() => character.ConsumeItemAsync("Tester", 125, 1))
            .Returns(Task.FromResult<long?>(0));
        A.CallTo(() => stats.Compute(info)).Returns(new CharacterStatResult(
            new StatBlock { MaxHp = 200, MaxMp = 200 }, new StatBlock()));
        A.CallTo(() => pets.TryUseCageAsync(client, 608001, 125)).Returns(Task.FromResult(false));

        await new ItemUseService(character, catalog, pets, A.Fake<ISkillCastService>(), stats)
            .UseAsync(client, new GameActionPackets.UseItemRequest(125, 1));

        info.CharacterHp.Should().Be(130);
    }
}
