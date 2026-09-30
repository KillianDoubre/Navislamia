using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using FakeItEasy;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Navislamia.Game.DataAccess.Repositories.Interfaces;
using Navislamia.Game.Network.Clients;
using Navislamia.Game.Network.Packets.Enums;
using Navislamia.Game.Services;
using Navislamia.Game.Services.Party;
using Navislamia.Game.Services.Stats;

namespace Tests.Game;

[TestFixture]
public class PartyServiceTests
{
    private PlayerVisibilityService _visibility;
    private IBannedWordsRepository _bannedWords;
    private PartyService _parties;

    [SetUp]
    public void SetUp()
    {
        _visibility = new PlayerVisibilityService(A.Fake<ILogger<PlayerVisibilityService>>());
        _bannedWords = A.Fake<IBannedWordsRepository>();
        _parties = new PartyService(_visibility, A.Fake<IStatService>(), _bannedWords);
    }

    [Test]
    public void MessagesHaveTheOfficialServerFormats()
    {
        PartyMessages.Create("Wolves", "Ana").Should().Be("CREATE|Wolves|Ana|0|");
        PartyMessages.Invite("Ana", "Wolves", 3, 777).Should().Be("INVITE|Ana|Wolves|3|777|");
        PartyMessages.Kick("Wolves", "Bo").Should().Be("KICK|Wolves|Bo|");
        PartyMessages.Mode(PartyShareMode.Linear).Should().Be("MODE|2|");
        PartyMessages.MemberInfo(new PartyMemberView(true, 9, "Ana", 3, 100, 12, 50, 7, 1000, 2000))
            .Should().Be("MINFO|9|Ana|3|100|50|7|1000|2000|2|");
        PartyMessages.Entry(new PartyMemberView(false, 0, "Bo", 5, 110, 20, 0, 0, 0, 0))
            .Should().Be("0|Bo|20|110|0|0|0|0|0|");
    }

    [TestCase(0, 100, 0)]
    [TestCase(1, 1000, 1)]
    [TestCase(50, 100, 50)]
    [TestCase(100, 100, 100)]
    public void PercentIsTruncatedAndNeverZeroWhileAlive(int value, int maximum, int expected)
    {
        PartyMessages.Percent(value, maximum).Should().Be(expected);
    }

    [Test]
    public void CreateAnswersCreateThenPartyInfo()
    {
        var ana = Player(1, "Ana");

        _parties.TryHandleCommand(ana, "/pcreate Wolves").Should().BeTrue();

        Lines(ana).Should().Equal("CREATE|Wolves|Ana|0|", "PINFO|1|Wolves|Ana|0|10|10|0|1|Ana|0|0|0|0|0|0|2|");
        Info(ana).PartyId.Should().Be(1);
    }

    [TestCase("/pcreate Bad|Name", "INVALID_PARTY_NAME")]
    [TestCase("/pcreate Bad-Name", "INVALID_PARTY_NAME")]
    public void CreateRefusesANameThatIsNotLettersAndDigits(string command, string error)
    {
        var ana = Player(1, "Ana");

        _parties.TryHandleCommand(ana, command);

        Lines(ana).Should().Equal(error);
        Info(ana).PartyId.Should().BeNull();
    }

    [Test]
    public void AnInvitationCarriesThePasswordThatJoinRequires()
    {
        var ana = Player(1, "Ana");
        var bo = Player(2, "Bo");
        _parties.TryHandleCommand(ana, "/pcreate Wolves");

        _parties.TryHandleCommand(ana, "/pinvite bo");

        var invite = Lines(bo).Single().Split('|');
        invite[0].Should().Be("INVITE");
        invite[1].Should().Be("Ana");
        invite[2].Should().Be("Wolves");
        invite[3].Should().Be("1");

        Clear(ana, bo);
        _parties.TryHandleCommand(bo, "/pjoin 1 12345");
        Lines(bo).Should().Equal("HAS_NO_AUTHORITY");
        Info(bo).PartyId.Should().BeNull();

        Clear(ana, bo);
        _parties.TryHandleCommand(bo, $"/pjoin 1 {invite[4]}");

        Lines(ana).Should().Equal("NEW|Bo|", "MINFO|2|Bo|0|0|0|0|0|0|2|");
        Lines(bo).Should().HaveCount(3);
        Lines(bo)[0].Should().Be("JOIN|Wolves|");
        Lines(bo)[1].Should().StartWith("PINFO|1|Wolves|Ana|");
        Lines(bo)[2].Should().Be("MINFO|2|Bo|0|0|0|0|0|0|2|");
        Info(bo).PartyId.Should().Be(1);
    }

    [Test]
    public void OnlyTheLeaderInvitesAndAFullPartyRefuses()
    {
        var members = Enumerable.Range(1, PartyService.MaxMembers + 1)
            .Select(i => Player((uint)i, $"P{i}")).ToArray();
        _parties.TryHandleCommand(members[0], "/pcreate Wolves");
        for (var i = 1; i < PartyService.MaxMembers; i++)
        {
            Join(members[0], members[i]);
        }

        Clear(members);
        _parties.TryHandleCommand(members[1], $"/pinvite {Info(members[8]).CharacterName}");
        Lines(members[8]).Should().BeEmpty();

        _parties.TryHandleCommand(members[0], $"/pinvite {Info(members[8]).CharacterName}");
        Lines(members[0]).Should().Equal("ERROR_MAX");
        Lines(members[8]).Should().BeEmpty();
    }

    [Test]
    public void KickTellsEveryoneThenRemovesTheMember()
    {
        var ana = Player(1, "Ana");
        var bo = Player(2, "Bo");
        _parties.TryHandleCommand(ana, "/pcreate Wolves");
        Join(ana, bo);
        Clear(ana, bo);

        _parties.TryHandleCommand(bo, "/pkick Ana");
        Lines(ana).Should().BeEmpty();

        _parties.TryHandleCommand(ana, "/pkick Bo");

        Lines(ana).Should().Equal("KICK|Wolves|Bo|");
        Lines(bo).Should().Equal("KICK|Wolves|Bo|");
        Info(bo).PartyId.Should().BeNull();
    }

    [Test]
    public void TheLeaderCannotLeaveButCanPromoteThenLeave()
    {
        var ana = Player(1, "Ana");
        var bo = Player(2, "Bo");
        _parties.TryHandleCommand(ana, "/pcreate Wolves");
        Join(ana, bo);
        Clear(ana, bo);

        _parties.TryHandleCommand(ana, "/pleave");
        Lines(ana).Should().BeEmpty();
        Info(ana).PartyId.Should().Be(1);

        _parties.TryHandleCommand(ana, "/ppromote Bo");
        _parties.TryHandleCommand(ana, "/pleave");

        Lines(bo).Should().Equal("PROMOTE|Bo|", "LEAVE|Ana|");
        Info(ana).PartyId.Should().BeNull();
        Info(bo).PartyId.Should().Be(1);
    }

    [Test]
    public void DestroyReachesEveryMemberAndEndsTheParty()
    {
        var ana = Player(1, "Ana");
        var bo = Player(2, "Bo");
        _parties.TryHandleCommand(ana, "/pcreate Wolves");
        Join(ana, bo);
        Clear(ana, bo);

        _parties.TryHandleCommand(ana, "/pdestroy");

        Lines(ana).Should().Equal("DESTROY|Wolves|");
        Lines(bo).Should().Equal("DESTROY|Wolves|");
        Info(ana).PartyId.Should().BeNull();
        Info(bo).PartyId.Should().BeNull();

        Clear(ana, bo);
        _parties.TryHandleCommand(bo, "/pcreate Wolves");
        Lines(bo).Should().StartWith("CREATE|Wolves|Bo|0|");
    }

    [Test]
    public void AMemberStaysInThePartyAcrossALogout()
    {
        var ana = Player(1, "Ana");
        var bo = Player(2, "Bo");
        _parties.TryHandleCommand(ana, "/pcreate Wolves");
        Join(ana, bo);
        Clear(ana, bo);

        _visibility.Registry.Unregister(2, bo);
        _parties.OnWorldExit(bo);
        Lines(ana).Should().Equal("LOGOUT|Bo|");

        Clear(ana);
        _parties.TryHandleCommand(ana, "/plist");
        Lines(ana).Single().Should().EndWith("0|Bo|10|0|0|0|0|0|0|");

        Clear(ana, bo);
        Info(bo).PartyId = null;
        _visibility.Registry.Register(2, bo);
        _parties.OnWorldEntry(bo);

        Lines(ana).Should().Equal("LOGIN|Wolves|Bo|", "MINFO|2|Bo|0|0|0|0|0|0|2|");
        Lines(bo).Should().HaveCount(3);
        Lines(bo)[2].Should().StartWith("PINFO|1|Wolves|Ana|");
        Info(bo).PartyId.Should().Be(1);
    }

    [Test]
    public void VitalsReachThePartyOnlyWhenAPercentageMoves()
    {
        var ana = Player(1, "Ana");
        var bo = Player(2, "Bo");
        _parties.TryHandleCommand(ana, "/pcreate Wolves");
        Join(ana, bo);
        Info(bo).CharacterMaxHp = 1000;
        Info(bo).CharacterHp = 995;
        _parties.OnVitalsChanged(bo);
        Clear(ana, bo);

        // 99.5 % then 99.1 %: both truncate to 99, nothing to tell.
        Info(bo).CharacterHp = 991;
        _parties.OnVitalsChanged(bo);
        Lines(ana).Should().BeEmpty();

        Info(bo).CharacterHp = 500;
        _parties.OnVitalsChanged(bo);
        Lines(ana).Should().Equal("MINFO|2|Bo|0|0|50|0|0|0|2|");
    }

    [Test]
    public void AnythingElseIsNotAPartyCommand()
    {
        var ana = Player(1, "Ana");

        _parties.TryHandleCommand(ana, "/pk on").Should().BeFalse();
        _parties.TryHandleCommand(ana, "/position").Should().BeFalse();
        Lines(ana).Should().BeEmpty();
    }

    private void Join(GameClient leader, GameClient member)
    {
        Clear(member);
        _parties.TryHandleCommand(leader, $"/pinvite {Info(member).CharacterName}");
        var invite = Lines(member).Single().Split('|');
        _parties.TryHandleCommand(member, $"/pjoin {invite[3]} {invite[4]}");
    }

    private GameClient Player(uint handle, string name)
    {
        var client = StorageTestHarness.NewGameClient(new StorageTestHarness.FrameConnection(Array.Empty<byte>()),
            playerVisibilityService: _visibility);
        var info = StorageTestHarness.Session(client);
        info.CharacterHandle = handle;
        info.CharacterName = name;
        info.CharacterLevel = 10;
        _visibility.Registry.Register(handle, client);
        return client;
    }

    private static ConnectionInfo Info(GameClient client) => StorageTestHarness.Session(client);

    private static void Clear(params GameClient[] clients)
    {
        foreach (var client in clients)
        {
            ((StorageTestHarness.FrameConnection)client.Connection).Sent.Clear();
        }
    }

    /// <summary>The <c>@PARTY</c> lines a client received: TS_SC_CHAT type 100, message from offset 31.</summary>
    private static List<string> Lines(GameClient client) =>
        ((StorageTestHarness.FrameConnection)client.Connection).Sent
            .Where(frame => BinaryPrimitives.ReadUInt16LittleEndian(frame.AsSpan(4, 2)) == (ushort)GamePackets.TM_SC_CHAT)
            .Select(frame =>
            {
                frame[30].Should().Be((byte)ChatType.PartySystem);
                Encoding.ASCII.GetString(frame, 7, 6).Should().Be("@PARTY");
                var length = BinaryPrimitives.ReadUInt16LittleEndian(frame.AsSpan(28, 2));
                return Encoding.ASCII.GetString(frame, 31, length - 1);
            })
            .ToList();
}
