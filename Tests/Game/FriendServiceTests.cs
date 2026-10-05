using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using FluentAssertions;
using Navislamia.Game.Network.Clients;
using Navislamia.Game.Network.Packets.Enums;
using Navislamia.Game.Services.Friends;
using NUnit.Framework;

namespace Tests.Game;

/// <summary>Friends and the block list, the official server's rules (docs/packet-specs/socle-amis.md).</summary>
[TestFixture]
public class FriendServiceTests
{
    /// <summary>The CharacterFriends table, in memory.</summary>
    private sealed class MemoryStore : IFriendStore
    {
        public readonly Dictionary<long, string> Characters = new();
        public readonly List<(long Owner, long Target, bool Denial)> Rows = new();
        public bool Fail;

        public Task<FriendLists> LoadAsync(long ownerId)
        {
            FriendEntry Entry(long id) => new(id, Characters[id]);
            return Task.FromResult(new FriendLists(
                Rows.Where(r => r.Owner == ownerId && !r.Denial).Select(r => Entry(r.Target)).ToList(),
                Rows.Where(r => r.Owner == ownerId && r.Denial).Select(r => Entry(r.Target)).ToList()));
        }

        public Task<FriendEntry> FindCharacterAsync(string name) => Task.FromResult(Characters
            .Where(c => string.Equals(c.Value, name, StringComparison.OrdinalIgnoreCase))
            .Select(c => new FriendEntry(c.Key, c.Value)).FirstOrDefault());

        public Task AddAsync(long ownerId, long targetId, bool isDenial)
        {
            if (Fail) throw new InvalidOperationException("database down");
            Rows.Add((ownerId, targetId, isDenial));
            return Task.CompletedTask;
        }

        public Task RemoveAsync(long ownerId, long targetId, bool isDenial)
        {
            Rows.RemoveAll(r => r == (ownerId, targetId, isDenial));
            return Task.CompletedTask;
        }

        public Task RemoveFromOthersAsync(long targetId)
        {
            Rows.RemoveAll(r => r.Target == targetId);
            return Task.CompletedTask;
        }
    }

    private MemoryStore _store = null!;
    private FriendService _service = null!;

    [SetUp]
    public void SetUp()
    {
        _store = new MemoryStore();
        _store.Characters[1] = "Alice";
        _store.Characters[2] = "Bobby";
        _store.Characters[3] = "Carol";
        _store.Characters[4] = "Dennis";
        _service = new FriendService(_store);
    }

    private static (GameClient Client, StorageTestHarness.FrameConnection Connection) Player(long id, string name)
    {
        var connection = new StorageTestHarness.FrameConnection(Array.Empty<byte>());
        var client = StorageTestHarness.NewGameClient(connection);
        var info = StorageTestHarness.Session(client);
        info.CharacterHandle = (uint)id;
        info.CharacterName = name;
        return (client, connection);
    }

    private async Task<(GameClient Client, StorageTestHarness.FrameConnection Connection)> Enter(long id, string name)
    {
        var player = Player(id, name);
        await _service.OnWorldEntryAsync(player.Client);
        return player;
    }

    /// <summary>The (type, text) of every TS_SC_CHAT sent, sender checked.</summary>
    private static List<(byte Type, string Text)> Lines(StorageTestHarness.FrameConnection connection) =>
        connection.Sent.Select(p =>
        {
            BinaryPrimitives.ReadUInt16LittleEndian(p.AsSpan(4, 2)).Should().Be((ushort)GamePackets.TM_SC_CHAT);
            Encoding.ASCII.GetString(p, 7, 21).TrimEnd('\0').Should().Be("@FRIEND");
            return (p[30], Encoding.ASCII.GetString(p, 31, p.Length - 31).TrimEnd('\0'));
        }).ToList();

    private static List<string> Texts(StorageTestHarness.FrameConnection connection) =>
        Lines(connection).Select(l => l.Text).ToList();

    /// <summary>Commands run asynchronously; the in-memory store completes at once, so a short wait is enough.</summary>
    private static async Task Command(GameClient client, FriendService service, string line)
    {
        service.TryHandleCommand(client, line).Should().BeTrue();
        await Task.Delay(30);
    }

    [Test]
    public void Messages_AreTheOfficialLines()
    {
        FriendMessages.FriendList(new[] { ("Bobby", FriendStatus.Online), ("Carol", FriendStatus.Offline), ("Dennis", FriendStatus.Blocking) })
            .Should().Be("FLIST|Bobby|1|Carol|0|Dennis|2|");
        FriendMessages.DenialList(new[] { "Bobby", "Carol" }).Should().Be("DLIST|Bobby|Carol|");
        FriendMessages.DenialList(Array.Empty<string>()).Should().Be("DLIST|");
        FriendMessages.Status("Alice", true).Should().Be("FSTATUS|Alice|1|");
        FriendMessages.Named(467, "friend_name", "Bobby").Should().Be("@467\v#@friend_name@#\vBobby");
    }

    [Test]
    public async Task WorldEntry_SendsBothListsWithEachFriendsStatus()
    {
        _store.Rows.Add((1, 2, false));
        _store.Rows.Add((1, 3, false));
        _store.Rows.Add((1, 4, false));
        _store.Rows.Add((1, 3, true));
        _store.Rows.Add((4, 1, true)); // Dennis blocks Alice
        await Enter(2, "Bobby");
        await Enter(4, "Dennis");

        var (_, alice) = await Enter(1, "Alice");

        Lines(alice).Should().Equal(
            ((byte)ChatType.FriendSystem, "FLIST|Bobby|1|Carol|0|Dennis|2|"),
            ((byte)ChatType.FriendSystem, "DLIST|Carol|"));
    }

    [Test]
    public async Task WorldEntryAndExit_AreAnnouncedToThePlayersListingThem_SaveThoseTheyBlock()
    {
        _store.Rows.Add((2, 1, false)); // Bobby lists Alice
        _store.Rows.Add((3, 1, false)); // Carol lists Alice
        _store.Rows.Add((1, 3, true));  // Alice blocks Carol
        var (_, bobby) = await Enter(2, "Bobby");
        var (_, carol) = await Enter(3, "Carol");
        bobby.Sent.Clear();
        carol.Sent.Clear();

        var (aliceClient, _) = await Enter(1, "Alice");
        _service.OnWorldExit(aliceClient);

        Texts(bobby).Should().Equal("FSTATUS|Alice|1|", "FSTATUS|Alice|0|");
        carol.Sent.Should().BeEmpty("SendStatusMessageToFriendOfPlayer skips the players Alice blocks");
    }

    [Test]
    public async Task AddFriend_StoresIt_SendsTheListThenTheLine()
    {
        var (alice, connection) = await Enter(1, "Alice");
        await Enter(2, "Bobby");
        connection.Sent.Clear();

        await Command(alice, _service, "/add_friend bobby");

        _store.Rows.Should().Equal((1L, 2L, false));
        Lines(connection).Should().Equal(
            ((byte)ChatType.FriendSystem, "FLIST|Bobby|1|"),
            ((byte)ChatType.Friend, "@467\v#@friend_name@#\vBobby"));
    }

    [TestCase("/add_friend Alice", "@482")]
    [TestCase("/add_friend Al", "@481")]
    [TestCase("/add_friend Bo!by", "@481")]
    [TestCase("/add_friend Nobody", "@481")]
    [TestCase("/add_denial alice", "@482")]
    public async Task AddFriend_RefusesLikeOnAddFriend(string line, string answer)
    {
        var (alice, connection) = await Enter(1, "Alice");
        connection.Sent.Clear();

        await Command(alice, _service, line);

        Texts(connection).Should().Equal(answer);
        _store.Rows.Should().BeEmpty();
    }

    [Test]
    public async Task AddFriend_RefusesTheTwentySixthAndAnExistingFriend()
    {
        _store.Rows.Add((1, 2, false));
        var (alice, connection) = await Enter(1, "Alice");
        connection.Sent.Clear();

        await Command(alice, _service, "/add_friend Bobby");
        Texts(connection).Should().Equal("@469\v#@friend_name@#\vBobby");

        for (var id = 10; id < 10 + FriendService.MaxEntries; id++)
        {
            _store.Rows.Add((1, id, false));
            _store.Characters[id] = $"Friend{id}";
        }

        var (full, fullConnection) = await Enter(1, "Alice");
        fullConnection.Sent.Clear();
        await Command(full, _service, "/add_friend Carol");
        Texts(fullConnection).Should().Equal("@478");
    }

    [Test]
    public async Task AFailedWrite_ChangesNothing()
    {
        var (alice, connection) = await Enter(1, "Alice");
        connection.Sent.Clear();
        _store.Fail = true;

        await Command(alice, _service, "/add_friend Bobby");

        Texts(connection).Should().Equal("@103");
        _store.Fail = false;
        connection.Sent.Clear();
        await Command(alice, _service, "/add_friend Bobby");
        Texts(connection).Should().Equal("FLIST|Bobby|0|", "@467\v#@friend_name@#\vBobby");
    }

    [Test]
    public async Task Blocking_HidesTheBlockerFromTheBlockedFriendAndUnblockingShowsThemAgain()
    {
        _store.Rows.Add((2, 1, false)); // Bobby lists Alice
        var (alice, aliceConnection) = await Enter(1, "Alice");
        var (bobby, bobbyConnection) = await Enter(2, "Bobby");
        aliceConnection.Sent.Clear();
        bobbyConnection.Sent.Clear();

        await Command(alice, _service, "/add_denial Bobby");

        Texts(aliceConnection).Should().Equal("DLIST|Bobby|", "@472\v#@denial_name@#\vBobby");
        Texts(bobbyConnection).Should().Equal("FSTATUS|Alice|0|");
        _service.Blocks(alice, bobby).Should().BeTrue("a whisper from Bobby is refused");
        _service.Blocks(bobby, alice).Should().BeFalse();

        aliceConnection.Sent.Clear();
        bobbyConnection.Sent.Clear();
        await Command(alice, _service, "/del_denial bobby");

        Texts(aliceConnection).Should().Equal("DLIST|", "@474\v#@denial_name@#\vBobby");
        Texts(bobbyConnection).Should().Equal("FSTATUS|Alice|1|");
        _service.Blocks(alice, bobby).Should().BeFalse();
        _store.Rows.Should().Equal((2L, 1L, false));
    }

    [Test]
    public async Task RemovingSomeoneNotListed_NamesThemAsTyped()
    {
        var (alice, connection) = await Enter(1, "Alice");
        connection.Sent.Clear();

        await Command(alice, _service, "/del_friend Carol");
        await Command(alice, _service, "/del_denial Carol");

        Texts(connection).Should().Equal("@471\v#@friend_name@#\vCarol", "@475\v#@denial_name@#\vCarol");
    }

    [Test]
    public async Task DeleteFriend_RemovesItAndSendsTheList()
    {
        _store.Rows.Add((1, 2, false));
        var (alice, connection) = await Enter(1, "Alice");
        connection.Sent.Clear();

        await Command(alice, _service, "/del_friend BOBBY");

        _store.Rows.Should().BeEmpty();
        Texts(connection).Should().Equal("FLIST|", "@470\v#@friend_name@#\vBobby");
    }

    [Test]
    public async Task AMalformedLineIsConsumedSilently_AndOtherCommandsAreNotTaken()
    {
        var (alice, connection) = await Enter(1, "Alice");
        connection.Sent.Clear();

        await Command(alice, _service, "/add_friend");
        await Command(alice, _service, "/add_friend Bobby Carol");
        _service.TryHandleCommand(alice, "/pcreate Party").Should().BeFalse();

        connection.Sent.Should().BeEmpty();
    }

    [Test]
    public async Task ADeletedCharacter_LeavesTheListsOfThePlayersOnline()
    {
        _store.Rows.Add((1, 2, false));
        _store.Rows.Add((1, 3, true));
        var (_, connection) = await Enter(1, "Alice");
        connection.Sent.Clear();

        _service.OnCharacterDeleted(2);
        _service.OnCharacterDeleted(3);

        Texts(connection).Should().Equal("FLIST|", "DLIST|");
    }

    [Test]
    public async Task ARename_TakesTheCharacterOffEveryOtherList_AndKeepsItsOwn()
    {
        _store.Rows.Add((2, 1, false)); // Bobby lists Alice
        _store.Rows.Add((3, 1, true));  // Carol blocks Alice
        _store.Rows.Add((1, 3, false)); // Alice lists Carol
        var (alice, aliceConnection) = await Enter(1, "Alice");
        var (_, bobbyConnection) = await Enter(2, "Bobby");
        var (_, carolConnection) = await Enter(3, "Carol");
        aliceConnection.Sent.Clear();
        bobbyConnection.Sent.Clear();
        carolConnection.Sent.Clear();
        StorageTestHarness.Session(alice).CharacterName = "Alicia";

        await _service.OnRenamedAsync(alice);

        _store.Rows.Should().Equal((1L, 3L, false));
        Texts(bobbyConnection).Should().Equal("FLIST|");
        Texts(carolConnection).Should().Equal("DLIST|");
        Texts(aliceConnection).Should().Equal(new[] { "FSTATUS|Carol|1|" }, "Carol no longer blocks her: she is seen online");
        _service.Blocks(alice, alice).Should().BeFalse();
    }

    [Test]
    public async Task ACommandBeforeWorldEntry_IsIgnored()
    {
        var (client, connection) = Player(1, "Alice");

        await Command(client, _service, "/add_friend Bobby");

        connection.Sent.Should().BeEmpty();
        _store.Rows.Should().BeEmpty();
    }
}
