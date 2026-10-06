using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Linq;
using FakeItEasy;
using FluentAssertions;
using Navislamia.Game.Maps.Collision;
using Navislamia.Game.Network;
using Navislamia.Game.Network.Clients;
using Navislamia.Game.Network.Packets;
using Navislamia.Game.Network.Packets.Enums;
using Navislamia.Game.Services;
using Navislamia.Game.Services.Interfaces;
using Navislamia.Game.Services.Movement;
using NUnit.Framework;

namespace Tests.Game;

/// <summary>
/// The official <c>onMoveRequest</c>/<c>GetValidWayPoint</c> checks and the death stop
/// (docs/packet-specs/socle-anti-triche-deplacement.md).
/// </summary>
[TestFixture]
public class PlayerMoveTests
{
    private static BlockPolygon Box(float minX, float minY, float maxX, float maxY) =>
        new(new[] { minX, maxX, maxX, minX }, new[] { minY, minY, maxY, maxY });

    /// <summary>A wall from (1090, 900) to (1110, 1100): what lies between x = 1000 and x = 1200 on y = 1000.</summary>
    private static readonly CollisionMap Wall = new(new[] { Box(1090, 900, 1110, 1100) });

    private static readonly (float X, float Y)[] East = { (1080, 1000) };

    [Test]
    public void A_walk_from_where_the_server_has_the_player_is_accepted()
    {
        PlayerMoveRules.Judge(1000, 1000, 1010, 1000, East, Wall).Should().Be(MoveVerdict.Accept);
        PlayerMoveRules.Judge(1000, 1000, 1010, 1000, Array.Empty<(float, float)>(), null)
            .Should().Be(MoveVerdict.Accept, "a stop, and no obstacle loaded");
    }

    [Test]
    public void A_claimed_position_too_far_or_outside_the_map_is_refused()
    {
        PlayerMoveRules.Judge(1000, 1000, 1000, 1526, East, null).Should().Be(MoveVerdict.Refuse, "VISIBLE_RANGE 525");
        PlayerMoveRules.Judge(1000, 1000, 1000, 1524, Array.Empty<(float, float)>(), null).Should().Be(MoveVerdict.Accept);
        PlayerMoveRules.Judge(10, 10, -1, 10, Array.Empty<(float, float)>(), null).Should().Be(MoveVerdict.Refuse);
        PlayerMoveRules.Judge(10, 10, float.NaN, 10, Array.Empty<(float, float)>(), null).Should().Be(MoveVerdict.Refuse);
        PlayerMoveRules.Judge(1000, 1000, 1000, 1000, new[] { (1000f, 700001f) }, null).Should().Be(MoveVerdict.Refuse);
    }

    [Test]
    public void Out_of_a_dungeon_the_client_detours_are_trusted_and_only_a_blocked_destination_is_corrected()
    {
        // onMoveRequest of the 2012-11 server: no leg is checked out of the dungeons, and no server-to-client segment.
        PlayerMoveRules.Judge(1000, 1000, 1200, 1000, Array.Empty<(float, float)>(), Wall)
            .Should().Be(MoveVerdict.Accept, "the server-to-client segment is a 2015 check");
        PlayerMoveRules.Judge(1000, 1000, 1010, 1000, new[] { (1200f, 1000f) }, Wall)
            .Should().Be(MoveVerdict.Accept, "a leg brushing or crossing a polygon is the client's own detour");
        PlayerMoveRules.Judge(1000, 1000, 1010, 1000, new[] { (1100f, 1000f) }, Wall)
            .Should().Be(MoveVerdict.Correct, "GameContent::IsBlocked on the destination");
        PlayerMoveRules.Judge(1100, 1000, 1010, 1000, new[] { (1100f, 950f) }, Wall)
            .Should().Be(MoveVerdict.Ignore, "the server's own position is inside the wall");
    }

    [Test]
    public void In_a_dungeon_each_leg_is_checked()
    {
        PlayerMoveRules.Judge(1000, 1000, 1010, 1000, new[] { (1200f, 1000f) }, Wall, inDungeon: true)
            .Should().Be(MoveVerdict.Correct, "IsInDungeon: CollisionToLine on every leg");
        PlayerMoveRules.Judge(1000, 1000, 1010, 1000, East, Wall, inDungeon: true).Should().Be(MoveVerdict.Accept);
        PlayerMoveRules.IsDungeonLocation(4).Should().BeTrue();
        PlayerMoveRules.IsDungeonLocation(12).Should().BeTrue();
        PlayerMoveRules.IsDungeonLocation(14).Should().BeTrue();
        PlayerMoveRules.IsDungeonLocation(2).Should().BeFalse("a town");
    }

    [Test]
    public void A_target_farther_than_a_map_is_dropped()
    {
        PlayerMoveRules.Judge(1000, 1000, 1000, 1000, new[] { (1000f, 1000f + 16129f) }, null)
            .Should().Be(MoveVerdict.Ignore);
    }

    [Test]
    public void A_reported_position_is_kept_only_near_the_estimate()
    {
        PlayerMoveRules.Trusted(1000, 1000, 1100, 1000).Should().Be((1100f, 1000f));
        PlayerMoveRules.Trusted(1000, 1000, 1130, 1000).Should().Be((1000f, 1000f), "CHANGE_LOCATION_ERROR_RANGE 120");
    }

    [Test]
    public void The_estimate_follows_every_leg_of_the_walk_and_a_rebase_keeps_the_rest()
    {
        var info = new ConnectionInfo { MoveSpeed = 30 }; // 30 ticks per unit × 30 / 30 = 1 tick per unit
        info.BeginWalk(0, 0, new[] { (100f, 0f), (100f, 100f) }, 1000);

        info.PositionAt(1050).Should().Be((50f, 0f));
        info.PositionAt(1150).Should().Be((100f, 50f), "the corner is taken, not cut");
        info.PositionAt(5000).Should().Be((100f, 100f));

        info.Rebase(100, 40, 1150);
        info.PositionAt(1160).Should().Be((100f, 50f), "the first leg is dropped, the second goes on from there");
        (info.DestinationX, info.DestinationY).Should().Be((100f, 100f));
    }

    [Test]
    public void A_dead_player_does_not_walk_and_is_stopped_where_it_lies()
    {
        var (client, connection) = Session(hp: 0, frame: Move(1, 0, 0, (10f, 0f)));
        Receive(client, connection);

        // onMoveRequest returns for a dead creature; the client walked the click already, so it gets a stop: a move
        // of its own handle with no waypoint, and nothing else.
        var stop = connection.Sent.Should().ContainSingle().Subject;
        Id(stop).Should().Be((ushort)GamePackets.TM_SC_MOVE);
        BinaryPrimitives.ReadUInt32LittleEndian(stop.AsSpan(11, 4)).Should().Be(StorageTestHarness.Session(client).CharacterHandle);
        BinaryPrimitives.ReadUInt16LittleEndian(stop.AsSpan(17, 2)).Should().Be(0, "no waypoint");
        StorageTestHarness.Session(client).CharacterHp.Should().Be(0);
    }

    [Test]
    public void A_teleport_is_refused_and_the_player_walked_back()
    {
        var (client, connection) = Session(hp: 100, frame: Move(1, 5000, 5000, (5010f, 5000f)));
        var info = StorageTestHarness.Session(client);
        info.BeginWalk(1000, 1000, Array.Empty<(float, float)>(), ServerClock.Now);

        Receive(client, connection);

        var ids = connection.Sent.Select(Id).ToList();
        ids.Should().Contain((ushort)GamePackets.TM_SC_RESULT);
        var result = connection.Sent.First(f => Id(f) == (ushort)GamePackets.TM_SC_RESULT);
        BinaryPrimitives.ReadUInt16LittleEndian(result.AsSpan(7, 2)).Should().Be((ushort)GamePackets.TM_CS_MOVE_REQUEST);
        BinaryPrimitives.ReadUInt16LittleEndian(result.AsSpan(9, 2)).Should().Be((ushort)ResultCode.AccessDenied);
        var back = connection.Sent.Single(f => Id(f) == (ushort)GamePackets.TM_SC_MOVE);
        BinaryPrimitives.ReadSingleLittleEndian(back.AsSpan(19, 4)).Should().Be(1000f, "walked back to the server's x");
        (info.X, info.Y).Should().Be((1000f, 1000f));
    }

    [Test]
    public void A_region_update_far_from_the_estimate_keeps_the_estimate()
    {
        var (client, connection) = Session(hp: 100, frame: Region(3000, 3000, 0));
        var info = StorageTestHarness.Session(client);
        info.BeginWalk(1000, 1000, Array.Empty<(float, float)>(), ServerClock.Now);

        Receive(client, connection);

        (info.X, info.Y).Should().Be((1000f, 1000f));
    }

    [Test]
    public void Death_stops_the_walk_for_the_player_and_whoever_sees_it()
    {
        var (client, connection) = Session(hp: 0);
        var (observer, observerConnection) = Session(hp: 100, handle: 2);
        var players = A.Fake<IPlayerVisibilityService>();
        A.CallTo(() => players.Observers(client)).Returns(new[] { observer });
        var info = StorageTestHarness.Session(client);
        info.BeginWalk(1000, 1000, new[] { (2000f, 1000f) }, ServerClock.Now);

        PlayerMoves.Stop(client, players);

        connection.Sent.Should().ContainSingle(f => Id(f) == (ushort)GamePackets.TM_SC_MOVE);
        observerConnection.Sent.Should().ContainSingle(f => Id(f) == (ushort)GamePackets.TM_SC_MOVE);
        (info.DestinationX, info.DestinationY).Should().Be((info.X, info.Y), "the server stops it too");
    }

    private static (GameClient Client, StorageTestHarness.FrameConnection Connection) Session(int hp, uint handle = 1,
        byte[] frame = null)
    {
        var connection = new StorageTestHarness.FrameConnection(frame ?? Array.Empty<byte>());
        var client = StorageTestHarness.NewGameClient(connection, playerVisibilityService: A.Fake<IPlayerVisibilityService>());
        var info = StorageTestHarness.Session(client);
        info.CharacterHandle = handle;
        info.CharacterHp = hp;
        return (client, connection);
    }

    private static void Receive(GameClient client, StorageTestHarness.FrameConnection connection) =>
        client.OnDataReceived(connection.BytesAvailable);

    private static ushort Id(byte[] frame) => BinaryPrimitives.ReadUInt16LittleEndian(frame.AsSpan(4, 2));

    private static byte[] Move(uint handle, float x, float y, params (float X, float Y)[] points)
    {
        var frame = new byte[26 + points.Length * 8];
        BinaryPrimitives.WriteUInt32LittleEndian(frame, (uint)frame.Length);
        BinaryPrimitives.WriteUInt16LittleEndian(frame.AsSpan(4), (ushort)GamePackets.TM_CS_MOVE_REQUEST);
        BinaryPrimitives.WriteUInt32LittleEndian(frame.AsSpan(7), handle);
        BinaryPrimitives.WriteSingleLittleEndian(frame.AsSpan(11), x);
        BinaryPrimitives.WriteSingleLittleEndian(frame.AsSpan(15), y);
        BinaryPrimitives.WriteUInt16LittleEndian(frame.AsSpan(24), (ushort)points.Length);
        for (var i = 0; i < points.Length; i++)
        {
            BinaryPrimitives.WriteSingleLittleEndian(frame.AsSpan(26 + i * 8), points[i].X);
            BinaryPrimitives.WriteSingleLittleEndian(frame.AsSpan(30 + i * 8), points[i].Y);
        }

        frame[6] = StorageTestHarness.Checksum(frame);
        return frame;
    }

    private static byte[] Region(float x, float y, float z)
    {
        var frame = new byte[23];
        BinaryPrimitives.WriteUInt32LittleEndian(frame, 23);
        BinaryPrimitives.WriteUInt16LittleEndian(frame.AsSpan(4), (ushort)GamePackets.TM_CS_REGION_UPDATE);
        BinaryPrimitives.WriteSingleLittleEndian(frame.AsSpan(11), x);
        BinaryPrimitives.WriteSingleLittleEndian(frame.AsSpan(15), y);
        BinaryPrimitives.WriteSingleLittleEndian(frame.AsSpan(19), z);
        frame[6] = StorageTestHarness.Checksum(frame);
        return frame;
    }
}
