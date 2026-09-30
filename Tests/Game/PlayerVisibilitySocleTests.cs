using System.Buffers.Binary;
using System.Collections.Generic;
using System.Linq;
using FakeItEasy;
using FluentAssertions;
using Navislamia.Game.Network;
using Navislamia.Game.Network.Clients;
using Navislamia.Game.Network.Packets.Enums;
using Navislamia.Game.Services;
using Navislamia.Game.Services.Interfaces;
using Navislamia.Game.Services.Pets;
using NUnit.Framework;

namespace Tests.Game;

/// <summary>
/// The two sides of the player visibility socle: reciprocity — the window is symmetric, each
/// synchronisation puts both views back in step — and the five triggers of
/// docs/packet-specs/socle-visibilite-joueurs.md §5.3, reached through the receive loop the client
/// really drives.
/// </summary>
[TestFixture]
public class PlayerVisibilitySocleTests
{
    private const uint HandleA = 0x40000001;
    private const uint HandleB = 0x40000002;

    [Test]
    public void Entry_PairsBothSidesWithTheirOwnEnterFrame()
    {
        var visibility = NewService();
        var a = NewPlayer(HandleA, 0f, 0f);
        var b = NewPlayer(HandleB, 300f, 400f);

        visibility.EnterWorld(a);
        SentOf(a).Should().BeEmpty("nobody else is in the window");

        visibility.EnterWorld(b);

        SentOf(a).Should().ContainSingle();
        EnterFrameOf(SentOf(a)[0]).Should().Be(HandleB);
        SentOf(b).Should().ContainSingle();
        EnterFrameOf(SentOf(b)[0]).Should().Be(HandleA);
    }

    [Test]
    public void Entry_SendsEachPeersWearAfterItsEnter()
    {
        var visibility = NewService();
        var a = NewPlayer(HandleA, 0f, 0f);
        var b = NewPlayer(HandleB, 100f, 0f);
        var wearA = new byte[] { 1, 2, 3 };
        var wearB = new byte[] { 4, 5, 6 };
        StorageTestHarness.Session(a).WearFrame = wearA;
        StorageTestHarness.Session(b).WearFrame = wearB;

        visibility.EnterWorld(a);
        visibility.EnterWorld(b);

        SentOf(a).Should().HaveCount(2);
        EnterFrameOf(SentOf(a)[0]).Should().Be(HandleB);
        SentOf(a)[1].Should().BeSameAs(wearB);
        SentOf(b).Should().HaveCount(2);
        EnterFrameOf(SentOf(b)[0]).Should().Be(HandleA);
        SentOf(b)[1].Should().BeSameAs(wearA);
    }

    [Test]
    public void Entry_OutOfTheWindow_ProducesNothingOnEitherSide()
    {
        var visibility = NewService();
        var a = NewPlayer(HandleA, 0f, 0f);
        var b = NewPlayer(HandleB, 40000f, 0f);

        visibility.EnterWorld(a);
        visibility.EnterWorld(b);

        SentOf(a).Should().BeEmpty();
        SentOf(b).Should().BeEmpty();
    }

    [Test]
    public void Entry_OnAnotherLayer_ProducesNothingOnEitherSide()
    {
        var visibility = NewService();
        var a = NewPlayer(HandleA, 0f, 0f, layer: 1);
        var b = NewPlayer(HandleB, 100f, 100f, layer: 2);

        visibility.EnterWorld(a);
        visibility.EnterWorld(b);

        SentOf(a).Should().BeEmpty();
        SentOf(b).Should().BeEmpty();
    }

    [Test]
    public void Entry_WithoutACharacterHandle_IsIgnored()
    {
        var visibility = NewService();
        var a = NewPlayer(HandleA, 0f, 0f);
        var b = NewPlayer(0, 100f, 100f);

        visibility.EnterWorld(a);
        visibility.EnterWorld(b);

        SentOf(a).Should().BeEmpty();
        SentOf(b).Should().BeEmpty();
        visibility.Registry.Count.Should().Be(1);
    }

    [Test]
    public void WindowExit_TellsBothSidesLeavingTheWindow()
    {
        var visibility = NewService();
        var a = NewPlayer(HandleA, 0f, 0f);
        var b = NewPlayer(HandleB, 400f, 0f);
        visibility.EnterWorld(a);
        visibility.EnterWorld(b);
        Clear(a);
        Clear(b);

        // B walks out of A's window (40000 units away) and reports it.
        StorageTestHarness.Session(b).X = 40000f;
        visibility.OnMove(b, System.Array.Empty<byte>());

        SentOf(a).Should().ContainSingle("A must be told the pair it saw is gone");
        LeaveFrameOf(SentOf(a)[0]).Should().Be(HandleB);
        SentOf(b).Should().ContainSingle("B must be told the pair it saw is gone");
        LeaveFrameOf(SentOf(b)[0]).Should().Be(HandleA);
    }

    [Test]
    public void RegionBorder_ReIndexesThePresenceAndTellsBothSides()
    {
        var visibility = NewService();
        var a = NewPlayer(HandleA, 0f, 0f);
        var b = NewPlayer(HandleB, 400f, 0f);
        visibility.EnterWorld(a);
        visibility.EnterWorld(b);
        Clear(a);
        Clear(b);

        // A border crossing is the client's own position report: no path, no MOVE frame, but the new
        // place is the one the window is computed from.
        StorageTestHarness.Session(b).X = 40000f;
        visibility.Sync(b);

        visibility.Index.TryGet(HandleB, out var presence).Should().BeTrue();
        presence.X.Should().Be(40000f, "a synchronisation re-indexes the presence it computes from");

        SentOf(a).Should().ContainSingle("A must be told the pair left its window");
        LeaveFrameOf(SentOf(a)[0]).Should().Be(HandleB);
        SentOf(b).Should().ContainSingle("B must be told the pair left its window too");
        LeaveFrameOf(SentOf(b)[0]).Should().Be(HandleA);
        SentOf(a).Should().NotContain(frame => IsEnterFrame(frame), "a border crossing emits no entry");
    }

    [Test]
    public void Entry_EveryObserverIsGivenTheSameSharedHandle()
    {
        var visibility = NewService();
        const uint handleC = 0x40000003;
        var a = NewPlayer(HandleA, 0f, 0f);
        var b = NewPlayer(HandleB, 100f, 0f);
        var c = NewPlayer(handleC, 200f, 0f);

        visibility.EnterWorld(a);
        visibility.EnterWorld(b);
        visibility.EnterWorld(c);

        // The handle of a player is character.Id for every observer — never a per-client handle like
        // the ones WorldObjectStreamer allocates: TS_SC_MOVE and TS_SC_LEAVE carry a single handle
        // (docs/packet-specs/socle-visibilite-joueurs.md §9.2).
        SentOf(a).Select(EnterFrameOf).Should().Equal(HandleB, handleC);
        SentOf(b).Select(EnterFrameOf).Should().Equal(HandleA, handleC);
        SentOf(c).Select(EnterFrameOf).Should().BeEquivalentTo(new[] { HandleA, HandleB });

        var enterOfCSeenByA = SentOf(a).Single(frame => IsEnterFrameOf(frame, handleC));
        var enterOfCSeenByB = SentOf(b).Single(frame => IsEnterFrameOf(frame, handleC));

        enterOfCSeenByA.Should().Equal(enterOfCSeenByB,
            "the same character is presented identically to every observer of its window");
        IsEnterFrameOf(enterOfCSeenByA, handleC).Should().BeTrue();
    }

    [Test]
    public void Exit_TellsEveryObserverAndKeepsNoPresence()
    {
        var visibility = NewService();
        var a = NewPlayer(HandleA, 0f, 0f);
        var b = NewPlayer(HandleB, 200f, 0f);
        visibility.EnterWorld(a);
        visibility.EnterWorld(b);
        Clear(a);
        Clear(b);

        visibility.LeaveWorld(a);

        SentOf(b).Should().ContainSingle();
        LeaveFrameOf(SentOf(b)[0]).Should().Be(HandleA);
        SentOf(a).Should().BeEmpty("a departing client is told nothing, it is leaving the world");
        visibility.Registry.Count.Should().Be(1);
        StorageTestHarness.Session(b).SpawnedPlayers.Should().BeEmpty();
    }

    [Test]
    public void Move_IsDiffusedWithTheReceivedPointsAndTheObserverClock()
    {
        var visibility = NewService();
        var a = NewPlayer(HandleA, 0f, 0f);
        var b = NewPlayer(HandleB, 200f, 0f);
        visibility.EnterWorld(a);
        visibility.EnterWorld(b);
        StorageTestHarness.Session(b).ClientClockOffset = 500;
        Clear(a);
        Clear(b);

        StorageTestHarness.Session(a).X = 250f;
        var waypoints = Points((100f, 100f), (250f, 300f));
        var before = unchecked(ServerClock.Now + 500);
        visibility.OnMove(a, waypoints);
        var after = unchecked(ServerClock.Now + 500);

        var move = SentOf(b).Single(frame => BinaryPrimitives.ReadUInt16LittleEndian(frame.AsSpan(4, 2))
            == (ushort)GamePackets.TM_SC_MOVE);

        move.Length.Should().Be(35);
        BinaryPrimitives.ReadUInt32LittleEndian(move.AsSpan(11, 4)).Should().Be(HandleA);
        move[15].Should().Be(0, "the walker's layer");
        move[16].Should().Be(ConnectionInfo.EchoedMoveSpeed);
        BinaryPrimitives.ReadUInt16LittleEndian(move.AsSpan(17, 2)).Should().Be(2);
        move.AsSpan(19, 16).ToArray().Should().Equal(waypoints, "the received points, verbatim");

        var startTime = BinaryPrimitives.ReadUInt32LittleEndian(move.AsSpan(7, 4));
        startTime.Should().BeInRange(before, after, "the observer's clock offset is applied");
    }

    [Test]
    public void Move_WithNoWaypoint_SendsNoMoveFrame()
    {
        var visibility = NewService();
        var a = NewPlayer(HandleA, 0f, 0f);
        var b = NewPlayer(HandleB, 200f, 0f);
        visibility.EnterWorld(a);
        visibility.EnterWorld(b);
        Clear(a);
        Clear(b);

        visibility.OnMove(a, System.Array.Empty<byte>());

        SentOf(a).Should().BeEmpty();
        SentOf(b).Should().BeEmpty("a request without a waypoint is a stop: only the walker is told");
    }

    [Test]
    public void Move_AloneInTheWorld_SendsNothing()
    {
        var visibility = NewService();
        var a = NewPlayer(HandleA, 0f, 0f);
        visibility.EnterWorld(a);
        Clear(a);

        visibility.OnMove(a, Points((10f, 10f)));

        SentOf(a).Should().BeEmpty();
    }

    [Test]
    public void Move_ToAPresenceThatHasNoSession_IsNotSent()
    {
        var visibility = NewService();
        var a = NewPlayer(HandleA, 0f, 0f);
        visibility.EnterWorld(a);
        // A presence the index knows and no session can resolve: the pair is never shown, so it is
        // never walked either.
        visibility.Index.Add(new PlayerPresence(0x40000009, Appearance(), 0, 300f, 0f, 0f));
        Clear(a);

        visibility.OnMove(a, Points((10f, 10f)));

        SentOf(a).Should().BeEmpty();
    }

    [Test]
    public void MoveRequest_WellFormed_HandsTheWaypointsToTheSocle()
    {
        var visibility = A.Fake<IPlayerVisibilityService>();
        var captured = new List<byte[]>();
        A.CallTo(() => visibility.OnMove(A<GameClient>._, A<byte[]>._))
            .Invokes((GameClient _, byte[] points) => captured.Add(points));

        var connection = new StorageTestHarness.FrameConnection(
            MoveRequest(HandleA, 0x1234, (100f, 100f), (250f, 300f)));
        var walker = Player(connection, visibility, HandleA);

        walker.OnDataReceived(connection.BytesAvailable);

        SentOf(connection).Should().ContainSingle("the walker keeps its echo");
        captured.Should().ContainSingle();
        captured[0].Should().Equal(Points((100f, 100f), (250f, 300f)));
    }

    [Test]
    public void MoveRequest_ClaimingAForeignHandle_IsDroppedWithoutEchoOrDiffusion()
    {
        var visibility = A.Fake<IPlayerVisibilityService>();
        var connection = new StorageTestHarness.FrameConnection(MoveRequest(HandleB, 0x1234, (100f, 100f)));
        var walker = Player(connection, visibility, HandleA);

        walker.OnDataReceived(connection.BytesAvailable);

        SentOf(connection).Should().BeEmpty();
        A.CallTo(() => visibility.OnMove(A<GameClient>._, A<byte[]>._)).MustNotHaveHappened();
    }

    [Test]
    public void RegionUpdate_ReachesTheSocle()
    {
        var visibility = A.Fake<IPlayerVisibilityService>();
        var connection = new StorageTestHarness.FrameConnection(RegionUpdate(11f, 22f, 33f));
        var walker = Player(connection, visibility, HandleA);

        walker.OnDataReceived(connection.BytesAvailable);

        A.CallTo(() => visibility.Sync(walker)).MustHaveHappenedOnceExactly();
        A.CallTo(() => visibility.OnMove(A<GameClient>._, A<byte[]>._))
            .MustNotHaveHappened();
    }

    [Test]
    public void Warp_AnnouncesTheDepartureAndRegistersTheNewPlace()
    {
        var visibility = NewService();
        var a = NewPlayer(HandleA, 100f, 100f);
        var b = NewPlayer(HandleB, 300f, 300f);
        visibility.EnterWorld(a);
        visibility.EnterWorld(b);
        Clear(a);
        Clear(b);

        var warp = new WarpService(A.Fake<INpcSpawnService>(), A.Fake<IMonsterSpawnService>(),
            A.Fake<IFieldPropService>(), A.Fake<ICombatService>(), A.Fake<IPetSummonService>(),
            visibility, A.Fake<IGroundItemService>());

        warp.Warp(a, 40000f, 40000f);

        SentOf(b).Should().ContainSingle("the observer is told the character left its window");
        LeaveFrameOf(SentOf(b)[0]).Should().Be(HandleA);
        SentOf(a).Should().ContainSingle(frame => IsLeaveFrameOf(frame, HandleB),
            "the parting client is told its peer is gone, as LeaveEverything does for the objects");
        visibility.Registry.Count.Should().Be(2, "the character is registered again at the new place");
        SentOf(a).Should().NotContain(frame => IsEnterFrame(frame),
            "the new place shares no window with the old one");
    }

    private static PlayerVisibilityService NewService() =>
        new(A.Fake<Microsoft.Extensions.Logging.ILogger<PlayerVisibilityService>>());

    private static PlayerAppearance Appearance() => new()
    {
        Race = 2,
        Sex = 1,
        SkinColor = 0x10203040,
        FaceId = 0x0A000001,
        FaceTextureId = 0x0B000002,
        HairId = 0x0C000003,
        HairColorIndex = 4,
        HairColorRgb = 0x00A0B0C0,
        HideEquipFlag = 0,
    };

    private static GameClient NewPlayer(uint handle, float x, float y, byte layer = 0)
    {
        var connection = new StorageTestHarness.FrameConnection(System.Array.Empty<byte>());
        var client = StorageTestHarness.NewGameClient(connection);
        var info = StorageTestHarness.Session(client);

        info.CharacterHandle = handle;
        info.CharacterName = $"player{handle}";
        info.CharacterHp = 1000;
        info.CharacterMaxHp = 1200;
        info.CharacterMp = 300;
        info.CharacterLevel = 30;
        info.CharacterJob = 1;
        info.Layer = layer;
        info.X = x;
        info.Y = y;
        info.Z = 0f;
        info.Appearance = Appearance();

        return client;
    }

    /// <summary>A game client whose visibility service is the fake, so the wiring itself is asserted.</summary>
    private static GameClient Player(StorageTestHarness.FrameConnection connection,
        IPlayerVisibilityService visibility, uint handle)
    {
        var client = StorageTestHarness.NewGameClient(connection, playerVisibilityService: visibility);
        var info = StorageTestHarness.Session(client);
        info.CharacterHandle = handle;
        info.Layer = 0;
        info.X = 0f;
        info.Y = 0f;
        return client;
    }

    private static List<byte[]> SentOf(GameClient client) =>
        SentOf((StorageTestHarness.FrameConnection)client.Connection);

    private static List<byte[]> SentOf(StorageTestHarness.FrameConnection connection) => connection.Sent;

    private static void Clear(GameClient client) => SentOf(client).Clear();

    private static uint EnterFrameOf(byte[] frame)
    {
        BinaryPrimitives.ReadUInt16LittleEndian(frame.AsSpan(4, 2))
            .Should().Be((ushort)GamePackets.TM_SC_ENTER);
        frame.Length.Should().Be(118);
        return BinaryPrimitives.ReadUInt32LittleEndian(frame.AsSpan(8, 4));
    }

    private static uint LeaveFrameOf(byte[] frame)
    {
        BinaryPrimitives.ReadUInt16LittleEndian(frame.AsSpan(4, 2))
            .Should().Be((ushort)GamePackets.TM_SC_LEAVE);
        frame.Length.Should().Be(11);
        return BinaryPrimitives.ReadUInt32LittleEndian(frame.AsSpan(7, 4));
    }

    /// <summary>
    /// The frame predicates stay plain methods: their body reads a <c>Span</c>, which an assertion's
    /// expression tree cannot carry.
    /// </summary>
    private static bool IsEnterFrame(byte[] frame) =>
        BinaryPrimitives.ReadUInt16LittleEndian(frame.AsSpan(4, 2)) == (ushort)GamePackets.TM_SC_ENTER;

    private static bool IsEnterFrameOf(byte[] frame, uint handle) =>
        IsEnterFrame(frame) && BinaryPrimitives.ReadUInt32LittleEndian(frame.AsSpan(8, 4)) == handle;

    private static bool IsLeaveFrameOf(byte[] frame, uint handle) =>
        BinaryPrimitives.ReadUInt16LittleEndian(frame.AsSpan(4, 2)) == (ushort)GamePackets.TM_SC_LEAVE
        && BinaryPrimitives.ReadUInt32LittleEndian(frame.AsSpan(7, 4)) == handle;

    private static byte[] Points(params (float X, float Y)[] points)
    {
        var block = new byte[points.Length * 8];
        for (var index = 0; index < points.Length; index++)
        {
            BinaryPrimitives.WriteSingleLittleEndian(block.AsSpan(index * 8, 4), points[index].X);
            BinaryPrimitives.WriteSingleLittleEndian(block.AsSpan(index * 8 + 4, 4), points[index].Y);
        }

        return block;
    }

    private static byte[] MoveRequest(uint handle, uint time, params (float X, float Y)[] points)
    {
        var frame = Frame((ushort)GamePackets.TM_CS_MOVE_REQUEST, 26 + points.Length * 8);
        BinaryPrimitives.WriteUInt32LittleEndian(frame.AsSpan(7, 4), handle);
        BinaryPrimitives.WriteUInt32LittleEndian(frame.AsSpan(19, 4), time);
        BinaryPrimitives.WriteUInt16LittleEndian(frame.AsSpan(24, 2), (ushort)points.Length);
        Points(points).CopyTo(frame.AsSpan(26));
        frame[6] = StorageTestHarness.Checksum(frame);
        return frame;
    }

    private static byte[] RegionUpdate(float x, float y, float z)
    {
        var frame = Frame((ushort)GamePackets.TM_CS_REGION_UPDATE, 23);
        BinaryPrimitives.WriteSingleLittleEndian(frame.AsSpan(11, 4), x);
        BinaryPrimitives.WriteSingleLittleEndian(frame.AsSpan(15, 4), y);
        BinaryPrimitives.WriteSingleLittleEndian(frame.AsSpan(19, 4), z);
        frame[6] = StorageTestHarness.Checksum(frame);
        return frame;
    }

    private static byte[] Frame(ushort id, int length)
    {
        var frame = new byte[length];
        BinaryPrimitives.WriteUInt32LittleEndian(frame, (uint)length);
        BinaryPrimitives.WriteUInt16LittleEndian(frame.AsSpan(4, 2), id);
        frame[6] = StorageTestHarness.Checksum(frame);
        return frame;
    }
}
