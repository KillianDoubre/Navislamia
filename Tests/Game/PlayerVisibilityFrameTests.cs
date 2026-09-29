using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using FakeItEasy;
using FluentAssertions;
using Navislamia.Game.Network.Clients;
using Navislamia.Game.Network.Packets.Enums;
using Navislamia.Game.Network.Packets.Game;
using Navislamia.Game.Services;
using Navislamia.Game.Services.Interfaces;
using Navislamia.Game.Services.Pets;
using NUnit.Framework;

namespace Tests.Game;

/// <summary>
/// The frames the player visibility socle emits, byte by byte: the definitive 118-byte player
/// <c>TS_SC_ENTER</c> (docs/packet-specs/socle-visibilite-joueurs.md §3.2-3.3), the 11-byte
/// <c>TS_SC_LEAVE</c> (§3.4) and the <c>TS_SC_MOVE</c> that carries the whole received path
/// (19 + 8 × N, §3.5).
/// </summary>
[TestFixture]
public class PlayerVisibilityFrameTests
{
    private const uint Handle = 0x4000002A;

    [Test]
    public void PlayerEnterFrame_IsOneHundredAndEighteenBytesWithEveryFieldAtItsOffset()
    {
        var connection = new ConnectionInfo
        {
            CharacterName = "Killian",
            CharacterHandle = Handle,
            CharacterHp = 1000,
            CharacterMaxHp = 1200,
            CharacterMp = 300,
            CharacterLevel = 42,
            CharacterJob = 7,
            PkMode = true,
        };

        var frame = PlayerVisibilityService.BuildEnterFrame(Presence(1234.5f, -678.25f, 12f, layer: 2), connection);

        frame.Length.Should().Be(118);

        BinaryPrimitives.ReadUInt32LittleEndian(frame.AsSpan(0, 4)).Should().Be(118);
        BinaryPrimitives.ReadUInt16LittleEndian(frame.AsSpan(4, 2))
            .Should().Be((ushort)GamePackets.TM_SC_ENTER);
        frame[6].Should().Be(StorageTestHarness.Checksum(frame));

        frame[7].Should().Be(0, "ET_Player");
        BinaryPrimitives.ReadUInt32LittleEndian(frame.AsSpan(8, 4)).Should().Be(Handle);
        BinaryPrimitives.ReadSingleLittleEndian(frame.AsSpan(12, 4)).Should().Be(1234.5f);
        BinaryPrimitives.ReadSingleLittleEndian(frame.AsSpan(16, 4)).Should().Be(-678.25f);
        BinaryPrimitives.ReadSingleLittleEndian(frame.AsSpan(20, 4)).Should().Be(12f);
        frame[24].Should().Be(2, "layer");
        frame[25].Should().Be(0, "objType, EOT_Player");

        BinaryPrimitives.ReadUInt32LittleEndian(frame.AsSpan(26, 4))
            .Should().Be(ActorStatus.ForPlayer(true, false, false, false));
        BinaryPrimitives.ReadSingleLittleEndian(frame.AsSpan(30, 4)).Should().Be(0f, "face_direction");
        BinaryPrimitives.ReadInt32LittleEndian(frame.AsSpan(34, 4)).Should().Be(1000, "hp");
        BinaryPrimitives.ReadInt32LittleEndian(frame.AsSpan(38, 4)).Should().Be(1200, "max_hp");
        BinaryPrimitives.ReadInt32LittleEndian(frame.AsSpan(42, 4)).Should().Be(300, "mp");
        BinaryPrimitives.ReadInt32LittleEndian(frame.AsSpan(46, 4)).Should().Be(300, "max_mp, the session's only mp");
        BinaryPrimitives.ReadInt32LittleEndian(frame.AsSpan(50, 4)).Should().Be(42, "level");
        frame[54].Should().Be(9, "race, from the appearance snapshot");
        BinaryPrimitives.ReadUInt32LittleEndian(frame.AsSpan(55, 4)).Should().Be(0x11223344, "skin_color");
        frame[59].Should().Be(1, "is_first_enter");
        BinaryPrimitives.ReadInt32LittleEndian(frame.AsSpan(60, 4)).Should().Be(0, "energy");

        frame[64].Should().Be(1, "sex");
        BinaryPrimitives.ReadUInt32LittleEndian(frame.AsSpan(65, 4)).Should().Be(0x0000A001, "faceId");
        BinaryPrimitives.ReadUInt32LittleEndian(frame.AsSpan(69, 4)).Should().Be(0x0000B002, "faceTextureId");
        BinaryPrimitives.ReadUInt32LittleEndian(frame.AsSpan(73, 4)).Should().Be(0x0000C003, "hairId");
        BinaryPrimitives.ReadUInt32LittleEndian(frame.AsSpan(77, 4)).Should().Be(5, "hairColorIndex");
        BinaryPrimitives.ReadUInt32LittleEndian(frame.AsSpan(81, 4)).Should().Be(0x00A0B0C0, "hairColorRGB");
        BinaryPrimitives.ReadUInt32LittleEndian(frame.AsSpan(85, 4)).Should().Be(0x3, "hideEquipFlag");

        frame.AsSpan(89, 7).ToArray().Should().Equal("Killian"u8.ToArray(), "name, 19 bytes");
        frame[96].Should().Be(0, "the name is NUL terminated and padded");
        frame[107].Should().Be(0, "the name is padded to 19 bytes");
        BinaryPrimitives.ReadUInt16LittleEndian(frame.AsSpan(108, 2)).Should().Be(7, "job_id");
        BinaryPrimitives.ReadUInt32LittleEndian(frame.AsSpan(110, 4)).Should().Be(0, "ride_handle");
        BinaryPrimitives.ReadUInt32LittleEndian(frame.AsSpan(114, 4)).Should().Be(0, "guild_id");
    }

    [Test]
    public void PlayerEnterFrame_ReservesTheNineteenNameBytesWithoutRunningIntoTheJobId()
    {
        var connection = new ConnectionInfo { CharacterName = new string('N', 40) };

        var frame = PlayerVisibilityService.BuildEnterFrame(Presence(0f, 0f, 0f), connection);

        frame.Length.Should().Be(118, "the marshalled name is capped at 19 bytes");
        frame.AsSpan(89, 18).ToArray().Should().OnlyContain(value => value == (byte)'N');
        frame[107].Should().Be(0, "the nineteenth byte is the terminator of the capped name");
        BinaryPrimitives.ReadUInt16LittleEndian(frame.AsSpan(108, 2))
            .Should().Be(0, "job_id is untouched by the name");
    }

    [Test]
    public void LeaveFrame_IsElevenBytesAndCarriesTheHandleAtSeven()
    {
        var frame = GameSpawnPackets.BuildLeave(Handle);

        frame.Length.Should().Be(11);
        BinaryPrimitives.ReadUInt16LittleEndian(frame.AsSpan(4, 2))
            .Should().Be((ushort)GamePackets.TM_SC_LEAVE);
        BinaryPrimitives.ReadUInt32LittleEndian(frame.AsSpan(7, 4)).Should().Be(Handle);
        frame[6].Should().Be(StorageTestHarness.Checksum(frame));
    }

    [Test]
    public void MoveFrame_WithTwoWaypoints_IsThirtyFiveBytesAndKeepsTheReceivedPoints()
    {
        var waypoints = new byte[16];
        BinaryPrimitives.WriteSingleLittleEndian(waypoints.AsSpan(0, 4), 100f);
        BinaryPrimitives.WriteSingleLittleEndian(waypoints.AsSpan(4, 4), 200f);
        BinaryPrimitives.WriteSingleLittleEndian(waypoints.AsSpan(8, 4), 300.5f);
        BinaryPrimitives.WriteSingleLittleEndian(waypoints.AsSpan(12, 4), 400.5f);

        var frame = GameMovePackets.BuildMove(Handle, 0x0BADF00D, 3, 100, waypoints);

        frame.Length.Should().Be(35, "19 + 8 x 2");
        BinaryPrimitives.ReadUInt32LittleEndian(frame.AsSpan(0, 4)).Should().Be(35);
        BinaryPrimitives.ReadUInt16LittleEndian(frame.AsSpan(4, 2))
            .Should().Be((ushort)GamePackets.TM_SC_MOVE);
        BinaryPrimitives.ReadUInt32LittleEndian(frame.AsSpan(7, 4)).Should().Be(0x0BADF00D, "start_time");
        BinaryPrimitives.ReadUInt32LittleEndian(frame.AsSpan(11, 4)).Should().Be(Handle);
        frame[15].Should().Be(3, "tlayer");
        frame[16].Should().Be(100, "speed");
        BinaryPrimitives.ReadUInt16LittleEndian(frame.AsSpan(17, 2)).Should().Be(2, "count");
        frame.AsSpan(19, 16).ToArray().Should().Equal(waypoints);
        frame[6].Should().Be(StorageTestHarness.Checksum(frame));
    }

    [Test]
    public void MoveFrame_WithOneWaypoint_KeepsTheHistoricalLayout()
    {
        var frame = GameMovePackets.BuildMove(Handle, 7, 0, 100, 12.5f, -3.5f);

        frame.Length.Should().Be(27, "19 + 8");
        BinaryPrimitives.ReadSingleLittleEndian(frame.AsSpan(19, 4)).Should().Be(12.5f);
        BinaryPrimitives.ReadSingleLittleEndian(frame.AsSpan(23, 4)).Should().Be(-3.5f);
        BinaryPrimitives.ReadUInt16LittleEndian(frame.AsSpan(17, 2)).Should().Be(1);
    }

    [Test]
    public void MoveFrame_WithoutAWaypointOrWithAPartialOne_IsRefused()
    {
        var refused = () => GameMovePackets.BuildMove(Handle, 0, 0, 100, Array.Empty<byte>());
        var partial = () => GameMovePackets.BuildMove(Handle, 0, 0, 100, new byte[12]);

        refused.Should().Throw<ArgumentException>();
        partial.Should().Throw<ArgumentException>();
    }

    private static PlayerPresence Presence(float x, float y, float z, byte layer = 0) =>
        new(Handle, new PlayerAppearance
        {
            Race = 9,
            Sex = 1,
            SkinColor = 0x11223344,
            FaceId = 0x0000A001,
            FaceTextureId = 0x0000B002,
            HairId = 0x0000C003,
            HairColorIndex = 5,
            HairColorRgb = 0x00A0B0C0,
            HideEquipFlag = 0x3,
        }, layer, x, y, z);
}
