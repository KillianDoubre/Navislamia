using System.Collections.Generic;
using FluentAssertions;
using Navislamia.Game.Network.Clients;
using Navislamia.Game.Network.Packets;
using Navislamia.Game.Network.Packets.Enums;
using Navislamia.Game.Network.Packets.Game;
using Navislamia.Game.Services;
using NUnit.Framework;

namespace Tests.Game;

/// <summary>
/// The pure rules of a booth trade (docs/packet-specs/705-buy-from-booth.md §5): a reservation takes the
/// units off the booth and prices them, a release gives them back, gold moves under the session's lock,
/// and the booth type reaches the status mask.
/// </summary>
[TestFixture]
public class BoothTradeRulesTests
{
    private static StartBoothRequest Booth(byte type, params BoothOpenItem[] items) =>
        new(type, "Boutique"u8.ToArray(), items);

    [Test]
    public void Purchase_ReservesTheUnitsAndPricesThemAtTheDeclaredUnitPrice()
    {
        var booth = Booth(1, new BoothOpenItem(10, 5, 100), new BoothOpenItem(11, 2, 7));

        var (next, reservation) = BoothTradeRules.ReservePurchase(booth,
            new[] { new BoothBuyLine(10, 240100, 3), new BoothBuyLine(11, 240200, 2) });

        reservation.Result.Should().Be(ResultCode.Success);
        reservation.Total.Should().Be(3 * 100 + 2 * 7);
        reservation.Lines.Should().Equal(new BoothTradeLine(0, 10, 3, 100), new BoothTradeLine(1, 11, 2, 7));
        next.Items[0].Count.Should().Be(2);
        next.Items[1].Count.Should().Be(0);
        booth.Items[0].Count.Should().Be(5, "the rules are pure: the old state is untouched");
        BoothTradeRules.IsSameBooth(next, booth).Should().BeTrue();
    }

    [Test]
    public void Purchase_OnABuyBooth_IsNotActable()
    {
        var booth = Booth(2, new BoothOpenItem(10, 5, 100));

        var (next, reservation) = BoothTradeRules.ReservePurchase(booth, new[] { new BoothBuyLine(10, 1, 1) });

        reservation.Result.Should().Be(ResultCode.NotActable);
        next.Should().BeSameAs(booth);
    }

    [Test]
    public void Purchase_OfMoreThanOnOffer_OrOfAnUndeclaredItem_IsRefusedWithoutChange()
    {
        var booth = Booth(1, new BoothOpenItem(10, 2, 100));

        BoothTradeRules.ReservePurchase(booth, new[] { new BoothBuyLine(10, 1, 3) }).Result.Result
            .Should().Be(ResultCode.NotEnoughItem);
        BoothTradeRules.ReservePurchase(booth, new[] { new BoothBuyLine(99, 1, 1) }).Result.Result
            .Should().Be(ResultCode.NotExist);
        BoothTradeRules.ReservePurchase(booth, new[] { new BoothBuyLine(10, 1, 0) }).Result.Result
            .Should().Be(ResultCode.NotEnoughItem);
        BoothTradeRules.ReservePurchase(booth, new[] { new BoothBuyLine(10, 1, 1), new BoothBuyLine(10, 1, 1) })
            .Result.Result.Should().Be(ResultCode.InvalidArgument, "one item asked twice is not a trade the client builds");
        BoothTradeRules.ReservePurchase(null, new[] { new BoothBuyLine(10, 1, 1) }).Result.Result
            .Should().Be(ResultCode.NotExist);
    }

    [Test]
    public void Purchase_AboveTheGoldCeiling_IsTooMuchMoney()
    {
        var booth = Booth(1, new BoothOpenItem(10, 2, BoothTradeRules.MaxGold));

        BoothTradeRules.ReservePurchase(booth, new[] { new BoothBuyLine(10, 1, 2) }).Result.Result
            .Should().Be(ResultCode.TooMuchMoney);
    }

    [Test]
    public void Sale_MatchesTheOfferedCodeAgainstTheBoothSamples()
    {
        var booth = Booth(2, new BoothOpenItem(10, 5, 40), new BoothOpenItem(11, 3, 90));
        var codeOf = new Dictionary<uint, int> { [10] = 240100, [11] = 240200 };

        var (next, reservation) = BoothTradeRules.ReserveSale(booth, 77, 240200, 2, codeOf);

        reservation.Result.Should().Be(ResultCode.Success);
        reservation.Lines.Should().Equal(new BoothTradeLine(1, 77, 2, 90));
        reservation.Total.Should().Be(180);
        next.Items[1].Count.Should().Be(1);

        BoothTradeRules.ReserveSale(booth, 77, 999999, 1, codeOf).Result.Result.Should().Be(ResultCode.NotExist);
        BoothTradeRules.ReserveSale(booth, 77, 240200, 4, codeOf).Result.Result.Should().Be(ResultCode.LimitMax);
        BoothTradeRules.ReserveSale(Booth(1, new BoothOpenItem(10, 5, 40)), 77, 240100, 1, codeOf).Result.Result
            .Should().Be(ResultCode.NotActable);
    }

    [Test]
    public void Release_GivesTheUnitsBack_ButNotToAnotherBooth()
    {
        var booth = Booth(1, new BoothOpenItem(10, 5, 100));
        var (reserved, reservation) =
            BoothTradeRules.ReservePurchase(booth, new[] { new BoothBuyLine(10, 1, 5) });

        BoothTradeRules.IsExhausted(reserved).Should().BeTrue();
        BoothTradeRules.Release(reserved, reserved, reservation.Lines).Items[0].Count.Should().Be(5);

        var reopened = Booth(1, new BoothOpenItem(10, 1, 100));
        BoothTradeRules.Release(reopened, reserved, reservation.Lines).Should().BeSameAs(reopened,
            "units reserved on a closed booth left with it");
        BoothTradeRules.Release(null, reserved, reservation.Lines).Should().BeNull();
    }

    [Test]
    public void Gold_IsDebitedAndCreditedOnlyWithinBounds()
    {
        var info = new ConnectionInfo { CharacterGold = 100 };

        info.TryDebitGold(150).Should().BeFalse();
        info.CharacterGold.Should().Be(100);
        info.TryDebitGold(60).Should().BeTrue();
        info.CharacterGold.Should().Be(40);

        info.TryCreditGold(61, 100).Should().BeFalse();
        info.TryCreditGold(60, 100).Should().BeTrue();
        info.CharacterGold.Should().Be(100);
        info.AddGold(-10).Should().Be(90);
    }

    [TestCase((byte)0, 0u)]
    [TestCase((byte)1, CreatureStatus.PlayerSellBooth)]
    [TestCase((byte)2, CreatureStatus.PlayerBuyBooth)]
    public void StatusMask_CarriesTheOpenBoothType(byte type, uint expected)
    {
        var info = new ConnectionInfo { PkMode = true };
        if (type != 0)
        {
            info.OpenBooth(Booth(type, new BoothOpenItem(10, 1, 1)));
        }

        ActorStatus.ForPlayer(info).Should().Be(CreatureStatus.PlayerPkOn | expected,
            "the mask is a snapshot: the booth bit travels with every other one");
    }
}
