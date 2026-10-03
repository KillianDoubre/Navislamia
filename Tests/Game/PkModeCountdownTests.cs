using System;
using FakeItEasy;
using FluentAssertions;
using Navislamia.Game.Network.Clients;
using Navislamia.Game.Network.Packets;
using Navislamia.Game.Services;
using Navislamia.Game.Services.Interfaces;

namespace Tests.Game;

/// <summary>The PK switch's countdown (onTurnOnPkMode / onTurnOffPkMode, StructPlayer::TurnOn/OffPkMode).</summary>
[TestFixture]
public class PkModeCountdownTests
{
    [Test]
    public void The_switch_rules_are_the_official_ones()
    {
        uint on = 0, off = 0;
        PkModeRules.TurnOn(ref on, ref off, 100).Should().BeTrue();
        on.Should().Be(100 + PkModeRules.OnDelayTicks);

        PkModeRules.TurnOn(ref on, ref off, 200).Should().BeFalse("a second request cancels the countdown");
        on.Should().Be(0);

        PkModeRules.TurnOff(ref on, ref off, 300).Should().BeTrue();
        off.Should().Be(300 + PkModeRules.OffDelayTicks);
        PkModeRules.TurnOff(ref on, ref off, 400).Should().BeFalse("the switch-off is already pending");
        PkModeRules.TurnOn(ref on, ref off, 500).Should().BeTrue("asking on cancels a pending switch-off");
        off.Should().Be(0);
        on.Should().Be(0);

        PkModeRules.Reached(0, 10).Should().BeFalse();
        PkModeRules.Reached(10, 10).Should().BeTrue();
    }

    private static (PkModeService Service, GameClient Client, ConnectionInfo Info, IPkFieldService Fields, ICombatService Combat) Build()
    {
        var fields = A.Fake<IPkFieldService>();
        var combat = A.Fake<ICombatService>();
        var service = new PkModeService(fields, combat, runTicks: false);
        var client = StorageTestHarness.NewGameClient(new StorageTestHarness.FrameConnection(Array.Empty<byte>()));
        var info = StorageTestHarness.Session(client);
        info.CharacterHandle = 7;
        return (service, client, info, fields, combat);
    }

    [Test]
    public void The_mode_goes_on_after_ten_seconds_in_a_pk_field_only()
    {
        var (service, client, info, fields, combat) = Build();
        A.CallTo(() => fields.IsPkField(info)).Returns(false);
        service.RequestOn(client).Should().Be(ResultCode.NotActable);
        info.TurnOnPkAt.Should().Be(0);

        A.CallTo(() => fields.IsPkField(info)).Returns(true);
        service.RequestOn(client).Should().Be(ResultCode.Success);
        info.PkMode.Should().BeFalse("not before the countdown");

        service.Process(client, unchecked(info.TurnOnPkAt - 1));
        info.PkMode.Should().BeFalse();
        service.Process(client, info.TurnOnPkAt);
        info.PkMode.Should().BeTrue();
        A.CallTo(() => combat.OnPkEnabled(client)).MustHaveHappenedOnceExactly();
    }

    [Test]
    public void The_mode_goes_off_after_thirty_seconds_and_a_cancelled_switch_on_costs_nothing()
    {
        var (service, client, info, fields, combat) = Build();
        A.CallTo(() => fields.IsPkField(info)).Returns(true);
        info.PkMode = true;

        service.RequestOff(client).Should().Be(ResultCode.Success);
        var at = info.TurnOffPkAt;
        service.Process(client, at);
        info.PkMode.Should().BeFalse();

        service.RequestOn(client).Should().Be(ResultCode.Success);
        service.RequestOff(client).Should().Be(ResultCode.Success, "it cancels the pending switch-on");
        service.Process(client, unchecked(ServerClock.Now + PkModeRules.OffDelayTicks + 1));
        info.PkMode.Should().BeFalse();
        A.CallTo(() => combat.OnPkEnabled(A<GameClient>._)).MustNotHaveHappened();
    }
}
