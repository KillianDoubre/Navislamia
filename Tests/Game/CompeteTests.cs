using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using FakeItEasy;
using FluentAssertions;
using Navislamia.Game.Network.Clients;
using Navislamia.Game.Network.Packets;
using Navislamia.Game.Network.Packets.Enums;
using Navislamia.Game.Network.Packets.Game;
using Navislamia.Game.Services;
using Navislamia.Game.Services.Compete;
using Navislamia.Game.Services.Interfaces;

namespace Tests.Game;

/// <summary>The official duel (docs/packet-specs/socle-competition-joueurs.md §10) and player-versus-player enmity.</summary>
[TestFixture]
public class CompeteTests
{
    private DateTime _now = new(2026, 10, 2, 12, 0, 0, DateTimeKind.Utc);

    private (CompeteService Service, GameClient A, GameClient B) Build(float bx = 100f)
    {
        var players = A.Fake<IPlayerVisibilityService>();
        var registry = new PlayerRegistry();
        A.CallTo(() => players.Registry).Returns(registry);

        GameClient Player(uint handle, string name, float x)
        {
            var client = StorageTestHarness.NewGameClient(new StorageTestHarness.FrameConnection(Array.Empty<byte>()));
            var info = StorageTestHarness.Session(client);
            info.CharacterHandle = handle;
            info.CharacterName = name;
            info.CharacterHp = 100;
            info.X = x;
            registry.Register(handle, client);
            return client;
        }

        var a = Player(1, "Alice", 0f);
        var b = Player(2, "Bob", bx);
        return (new CompeteService(players, () => _now, runTicks: false), a, b);
    }

    private static List<byte[]> Sent(GameClient client) => ((StorageTestHarness.FrameConnection)client.Connection).Sent;

    private static ushort Id(byte[] packet) => BinaryPrimitives.ReadUInt16LittleEndian(packet.AsSpan(4, 2));

    private static ResultCode LastResult(GameClient client) =>
        (ResultCode)BinaryPrimitives.ReadUInt16LittleEndian(Sent(client).Last(p => Id(p) == 0).AsSpan(9, 2));

    private static GameCompetePackets.CompeteRequest Invite(string name) => new(0, name);

    [Test]
    public void The_request_rules_follow_the_official_order()
    {
        CompeteService.CheckRequest(CompeteStatus.Requested, null, 0f).Should().Be(ResultCode.WaitingCompeteRequestAnswer);
        CompeteService.CheckRequest(CompeteStatus.Started, null, 0f).Should().Be(ResultCode.AlreadyInCompete);
        CompeteService.CheckRequest(null, CompeteStatus.Requested, 0f).Should().Be(ResultCode.TargetWaitingCompeteRequestAnswer);
        CompeteService.CheckRequest(null, CompeteStatus.Countdown, 0f).Should().Be(ResultCode.TargetAlreadyInCompete);
        CompeteService.CheckRequest(null, null, 501f).Should().Be(ResultCode.TooFar);
        CompeteService.CheckRequest(null, null, 500f).Should().Be(ResultCode.Success);
    }

    [Test]
    public void An_accepted_invitation_counts_down_then_starts()
    {
        var (service, a, b) = Build();

        service.Request(a, Invite("bob"));
        LastResult(a).Should().Be(ResultCode.Success);
        Sent(b).Last().Should().Match<byte[]>(p => Id(p) == (ushort)GamePackets.TM_SC_COMPETE_REQUEST);

        service.Answer(b, new GameCompetePackets.CompeteAnswer(0, 0));
        Sent(a).Select(Id).Should().Contain(new[]
            { (ushort)GamePackets.TM_SC_COMPETE_ANSWER, (ushort)GamePackets.TM_SC_COMPETE_COUNTDOWN });
        var countdown = Sent(b).Last();
        Id(countdown).Should().Be((ushort)GamePackets.TM_SC_COMPETE_COUNTDOWN);
        BinaryPrimitives.ReadUInt32LittleEndian(countdown.AsSpan(39, 4)).Should().Be(1u, "Alice's handle");
        service.AreCompeting(a, b).Should().BeFalse("not before the countdown ends");

        _now += CompeteService.CountdownTime;
        service.Process(_now);

        Id(Sent(a).Last()).Should().Be((ushort)GamePackets.TM_SC_COMPETE_START);
        service.AreCompeting(a, b).Should().BeTrue();
    }

    [Test]
    public void A_refusal_or_silence_closes_the_invitation()
    {
        var (service, a, b) = Build();
        service.Request(a, Invite("Bob"));
        service.Answer(b, new GameCompetePackets.CompeteAnswer(0, 1));
        Sent(a).Last()[8].Should().Be(1, "the answer type is relayed");

        service.Request(a, Invite("Bob"));
        _now += CompeteService.AnswerTime;
        service.Process(_now);
        Sent(b).Last()[8].Should().Be(CompeteService.AnswerTimedOut);
        service.Answer(b, new GameCompetePackets.CompeteAnswer(0, 0));
        LastResult(b).Should().Be(ResultCode.NotInCompete);
    }

    [Test]
    public void Inviting_twice_or_someone_far_is_refused()
    {
        var (service, a, b) = Build(bx: 600f);
        service.Request(a, Invite("Bob"));
        LastResult(a).Should().Be(ResultCode.TooFar);
        service.Request(a, Invite("Nobody"));
        LastResult(a).Should().Be(ResultCode.NotExist);
    }

    private (CompeteService Service, GameClient A, GameClient B) Started()
    {
        var (service, a, b) = Build();
        service.Request(a, Invite("Bob"));
        service.Answer(b, new GameCompetePackets.CompeteAnswer(0, 0));
        _now += CompeteService.CountdownTime;
        service.Process(_now);
        return (service, a, b);
    }

    private static (byte EndType, string Winner, string Loser) EndOf(GameClient client)
    {
        var end = Sent(client).Last(p => Id(p) == (ushort)GamePackets.TM_SC_COMPETE_END);
        string Name(int offset) => Encoding.ASCII.GetString(end, offset, 31).TrimEnd('\0');
        return (end[8], Name(9), Name(40));
    }

    [Test]
    public void A_death_ends_the_duel_and_lets_the_loser_come_back()
    {
        var (service, a, b) = Started();

        service.OnKilledBy(b, a);

        EndOf(a).Should().Be(((byte)CompeteEndType.Win, "Alice", "Bob"));
        service.AreCompeting(a, b).Should().BeFalse();
        service.ConsumeLoss(b).Should().BeTrue();
        service.ConsumeLoss(b).Should().BeFalse("once per loss");
        ResurrectionRules.CompeteHp(1234f).Should().Be(123);
    }

    [Test]
    public void Range_time_departure_and_third_parties_end_it()
    {
        var (service, a, b) = Started();
        StorageTestHarness.Session(b).X = 1000f;
        service.Process(_now);
        EndOf(a).Should().Be(((byte)CompeteEndType.OutOfRange, "Alice", "Bob"));

        (service, a, b) = Started();
        _now += CompeteService.DuelTime;
        service.Process(_now);
        EndOf(b).EndType.Should().Be((byte)CompeteEndType.Timeout);

        (service, a, b) = Started();
        service.Leave(a, CompeteEndType.Logout);
        EndOf(b).Should().Be(((byte)CompeteEndType.Logout, "Bob", "Alice"));

        (service, a, b) = Started();
        service.OnDamagedByOther(b, null);
        EndOf(a).EndType.Should().Be((byte)CompeteEndType.Interfered);
        service.AreCompeting(a, b).Should().BeFalse();
    }

    [Test]
    public void The_server_frames_have_the_measured_sizes()
    {
        GameCompeteServerPackets.BuildRequest(0, "Alice").Length.Should().Be(39);
        GameCompeteServerPackets.BuildAnswer(0, 1, "Bob").Length.Should().Be(40);
        GameCompeteServerPackets.BuildCountdown(0, "Bob", 2).Length.Should().Be(43);
        GameCompeteServerPackets.BuildStart(0, "Bob").Length.Should().Be(39);
        GameCompeteServerPackets.BuildEnd(0, 0, "Alice", "Bob").Length.Should().Be(71);
        GameCompeteServerPackets.BuildEnd(0, 0, new string('x', 40), "Bob")[39].Should().Be(0, "the name keeps its NUL");
    }

    [Test]
    public void Players_are_enemies_in_a_duel_or_in_a_pk_field_with_pk_mode()
    {
        var a = new ConnectionInfo { PkMode = true };
        var b = new ConnectionInfo();

        CombatService.IsPlayerEnemy(a, b, competing: true, pkField: false).Should().BeTrue();
        CombatService.IsPlayerEnemy(a, b, competing: false, pkField: false).Should().BeFalse("no PK field known");
        CombatService.IsPlayerEnemy(a, b, competing: false, pkField: true).Should().BeTrue();
        CombatService.IsPlayerEnemy(b, new ConnectionInfo(), competing: false, pkField: true).Should().BeFalse();
        CombatService.IsPlayerEnemy(new ConnectionInfo { PkMode = true, PartyId = 5 }, new ConnectionInfo { PartyId = 5 },
            competing: false, pkField: true).Should().BeFalse("same party");
    }
}
