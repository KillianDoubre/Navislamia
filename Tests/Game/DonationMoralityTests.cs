using System;
using System.Linq;
using System.Threading.Tasks;
using FakeItEasy;
using FluentAssertions;
using Navislamia.Game.DataAccess.Entities.Telecaster;
using Navislamia.Game.Network.Packets;
using Navislamia.Game.Network.Packets.Game;
using Navislamia.Game.Services;
using Navislamia.Game.Services.Interfaces;

namespace Tests.Game;

/// <summary>The altar: gifts become moral points (onDonateItem) and moral points buy rewards (onDonateReward).</summary>
[TestFixture]
public class DonationMoralityTests
{
    [Test]
    public void Ten_thousand_gold_is_worth_one_moral_point()
    {
        ItemDonateService.MoralPoints(10_000).Should().Be(1m);
        ItemDonateService.MoralPoints(12_345).Should().Be(1.2345m);
        ItemDonateService.MoralPoints(1).Should().Be(0.0001m);
        ItemDonateService.MoralPoints(-5).Should().Be(0m);
    }

    [Test]
    public async Task A_gift_of_gold_and_items_lowers_the_immorality_by_its_worth()
    {
        var characters = A.Fake<ICharacterService>();
        var catalog = A.Fake<IItemSellCatalog>();
        ItemSellTemplate template = new(1, 50_000);
        A.CallTo(() => catalog.TryGetTemplate(700, out template)).Returns(true).AssignsOutAndRefParameters(template);
        A.CallTo(() => characters.GetItemByHandleAsync("Ana", 5u)).Returns(new ItemEntity { Id = 5, ItemResourceId = 700, Amount = 3 });
        A.CallTo(() => characters.ConsumeItemAsync("Ana", 5u, 2)).Returns(1L);
        var service = new ItemDonateService(characters, catalog);
        var client = StorageTestHarness.NewGameClient(new StorageTestHarness.FrameConnection(Array.Empty<byte>()));
        var info = StorageTestHarness.Session(client);
        info.CharacterName = "Ana";
        info.CharacterHandle = 7;
        info.CharacterGold = 100_000;
        info.ImmoralPoint = 120m;

        await service.DonateAsync(client, new GameActionPackets.DonateItemRequest(30_000, 0,
            new[] { new GameActionPackets.DonateItemEntry(5, 2) }));

        // 30 000 gold = 3 points, two items of base price 50 000 = 10 points.
        info.ImmoralPoint.Should().Be(107m);
        info.CharacterGold.Should().Be(70_000);
    }

    [Test]
    public async Task Moral_points_buy_the_official_rewards_or_the_selection_is_refused()
    {
        var characters = A.Fake<ICharacterService>();
        A.CallTo(() => characters.AddItemAsync("Ana", A<int>._, A<long>._))
            .ReturnsLazily((string _, int code, long count) => Task.FromResult(new ItemEntity { Id = 90, ItemResourceId = code, Amount = count }));
        var service = new ItemDonateService(characters);
        var client = StorageTestHarness.NewGameClient(new StorageTestHarness.FrameConnection(Array.Empty<byte>()));
        var info = StorageTestHarness.Session(client);
        info.CharacterName = "Ana";
        info.CharacterHandle = 7;
        info.ImmoralPoint = -6000m;

        (await service.RewardAsync(client, new[] { new GameActionPackets.DonateRewardEntry(3, 1) }))
            .Should().Be(ResultCode.NotOwn, "30 000 points are not covered by 6 000");

        (await service.RewardAsync(client, new[]
            {
                new GameActionPackets.DonateRewardEntry(0, 1), new GameActionPackets.DonateRewardEntry(1, 1)
            })).Should().Be(ResultCode.Success);
        info.ImmoralPoint.Should().Be(0m, "1 000 + 5 000 points spent");
        A.CallTo(() => characters.AddItemAsync("Ana", 3620026, 1)).MustHaveHappenedOnceExactly();
        A.CallTo(() => characters.AddItemAsync("Ana", 3620025, 1)).MustHaveHappenedOnceExactly();

        (await service.RewardAsync(client, new[] { new GameActionPackets.DonateRewardEntry(9, 1) }))
            .Should().Be(ResultCode.InvalidArgument);
    }
}
