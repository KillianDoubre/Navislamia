using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using FakeItEasy;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Navislamia.Game.DataAccess.Entities.Enums;
using Navislamia.Game.DataAccess.Entities.Telecaster;
using Navislamia.Game.DataAccess.Repositories.Interfaces;
using Navislamia.Game.Services;
using NUnit.Framework;

namespace Tests.Game;

/// <summary>
/// <see cref="CharacterService.TransferItemsAsync"/>: items change hands with every attribute they had, both
/// balances are written in the same save, and a trade that cannot apply whole applies not at all.
/// </summary>
[TestFixture]
public class ItemTransferTests
{
    private CharacterEntity _giver = null!;
    private CharacterEntity _receiver = null!;
    private ICharacterRepository _repository = null!;
    private CharacterService _service = null!;

    [SetUp]
    public void SetUp()
    {
        _giver = new CharacterEntity { Id = 1, CharacterName = "Giver", Gold = 10, Items = new List<ItemEntity>() };
        _receiver = new CharacterEntity
        {
            Id = 2, CharacterName = "Receiver", Gold = 20,
            Items = new List<ItemEntity> { new() { Id = 900, ItemResourceId = 1, Amount = 1, Idx = 1 } }
        };

        _repository = A.Fake<ICharacterRepository>();
        A.CallTo(() => _repository.GetCharacterByNameWithItemsAsync("Giver")).Returns(_giver);
        A.CallTo(() => _repository.GetCharacterByNameWithItemsAsync("Receiver")).Returns(_receiver);
        var factory = A.Fake<ICharacterRepositoryFactory>();
        A.CallTo(() => factory.Create()).Returns(_repository);

        _service = new CharacterService(A.Fake<IStarterItemsRepository>(), factory, new CharacterGate(),
            A.Fake<ILogger<CharacterService>>());
    }

    private Task<ItemTransferResult> Transfer(params ItemTransferLine[] lines) =>
        _service.TransferItemsAsync(new ItemTransfer("Giver", "Receiver", lines, GiverGold: 110, ReceiverGold: 0));

    [Test]
    public async Task AWholeStack_MovesAsTheSameRowToTheEndOfTheReceiversBag()
    {
        var sword = new ItemEntity { Id = 5, ItemResourceId = 101221, Amount = 1, Enhance = 7, Idx = 1, CharacterId = 1, WearInfo = ItemWearType.None };
        _giver.Items.Add(sword);

        var result = await Transfer(new ItemTransferLine(5, 1));

        result.Outcome.Should().Be(ItemTransferOutcome.Success);
        result.Moved.Should().ContainSingle().Which.Should().Be(new ItemTransferred(5, 0, sword));
        _giver.Items.Should().BeEmpty();
        _receiver.Items.Should().Contain(sword);
        sword.CharacterId.Should().Be(2);
        sword.Idx.Should().Be(2, "it lands after the receiver's last item");
        sword.Enhance.Should().Be(7, "the row itself moved, with its enhance");
        _giver.Gold.Should().Be(110);
        _receiver.Gold.Should().Be(0);
        A.CallTo(() => _repository.SaveChangesAsync()).MustHaveHappenedOnceExactly();
    }

    [Test]
    public async Task PartOfAStack_SplitsIntoANewRowCarryingEveryAttribute()
    {
        var potions = new ItemEntity
        {
            Id = 6, ItemResourceId = 603002, Amount = 10, Level = 3, Flag = ItemFlag.None, WearInfo = ItemWearType.None,
            SocketItemIds = new long[] { 1, 2, 3, 4 }, ElementalEffectAttackPoint = 9, Idx = 1
        };
        _giver.Items.Add(potions);

        var result = await Transfer(new ItemTransferLine(6, 4));

        result.Outcome.Should().Be(ItemTransferOutcome.Success);
        var moved = result.Moved.Single();
        moved.GiverRemaining.Should().Be(6);
        potions.Amount.Should().Be(6);
        moved.Received.Should().NotBeSameAs(potions);
        moved.Received.Amount.Should().Be(4);
        moved.Received.ItemResourceId.Should().Be(603002);
        moved.Received.Level.Should().Be(3);
        moved.Received.ElementalEffectAttackPoint.Should().Be(9);
        moved.Received.SocketItemIds.Should().Equal(1, 2, 3, 4);
        moved.Received.SocketItemIds.Should().NotBeSameAs(potions.SocketItemIds);
        _receiver.Items.Should().Contain(moved.Received);
    }

    [Test]
    public async Task AMissingOrShortOrWornItem_RefusesTheWholeTradeWithoutSaving()
    {
        _giver.Items.Add(new ItemEntity { Id = 7, ItemResourceId = 1, Amount = 2, Idx = 1, WearInfo = ItemWearType.None });
        _giver.Items.Add(new ItemEntity { Id = 8, ItemResourceId = 2, Amount = 1, Idx = 2, WearInfo = ItemWearType.Weapon });

        (await Transfer(new ItemTransferLine(7, 1), new ItemTransferLine(99, 1))).Outcome
            .Should().Be(ItemTransferOutcome.ItemMissing);
        (await Transfer(new ItemTransferLine(7, 3))).Outcome.Should().Be(ItemTransferOutcome.NotEnough);
        (await Transfer(new ItemTransferLine(8, 1))).Outcome.Should().Be(ItemTransferOutcome.Worn);

        _giver.Items.Should().HaveCount(2);
        _giver.Items.First().Amount.Should().Be(2, "the valid first line was not applied either");
        _giver.Gold.Should().Be(10);
        A.CallTo(() => _repository.SaveChangesAsync()).MustNotHaveHappened();
    }

    [Test]
    public async Task PairGate_TwoNamesOnTheSameStripe_DoNotDeadlock()
    {
        // Two distinct names that hash to the same stripe: a gate taken twice would wait forever.
        var first = "Giver";
        var second = Enumerable.Range(0, 100_000).Select(i => $"n{i}")
            .First(name => name != first && Stripe(name) == Stripe(first));
        var gate = new CharacterGate();

        var run = gate.RunPairAsync(first, second, () => Task.FromResult(42));

        (await Task.WhenAny(run, Task.Delay(TimeSpan.FromSeconds(2)))).Should().BeSameAs(run);
        (await run).Should().Be(42);
    }

    [Test]
    public async Task PairGate_OpposedOrders_DoNotDeadlock()
    {
        var gate = new CharacterGate();
        var release = new TaskCompletionSource<bool>();

        var one = gate.RunPairAsync("Alpha", "Omega", async () => { await release.Task; return 1; });
        var two = gate.RunPairAsync("Omega", "Alpha", () => Task.FromResult(2));
        release.SetResult(true);

        var both = Task.WhenAll(one, two);
        (await Task.WhenAny(both, Task.Delay(TimeSpan.FromSeconds(2)))).Should().BeSameAs(both);
    }

    private static uint Stripe(string name) => (uint)StringComparer.Ordinal.GetHashCode(name) % 64;
}
