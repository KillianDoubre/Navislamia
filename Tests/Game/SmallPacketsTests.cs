using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using FakeItEasy;
using FluentAssertions;
using Navislamia.Game.Network.Packets.Enums;
using Navislamia.Game.Network.Packets.Game;
using Navislamia.Game.Services.Creatures;
using NUnit.Framework;

namespace Tests.Game;

[TestFixture]
public class SmallPacketsTests
{
    private static void Header(byte[] frame, int size, ushort id)
    {
        frame.Should().HaveCount(size);
        BinaryPrimitives.ReadUInt32LittleEndian(frame).Should().Be((uint)size);
        BinaryPrimitives.ReadUInt16LittleEndian(frame.AsSpan(4)).Should().Be(id);
        frame[6].Should().Be(unchecked((byte)frame.Take(6).Sum(b => b)));
    }

    [TestCase(322)] [TestCase(512)]
    public void Handle_frames_have_only_one_handle(int id)
    {
        var frame = id == 322 ? GameSmallPackets.ShowSummonNameChange(0x12345678) : GameSmallPackets.Target(0x12345678);
        Header(frame, 11, (ushort)id);
        frame.AsSpan(7).ToArray().Should().Equal(0x78, 0x56, 0x34, 0x12);
    }

    [Test]
    public void SP_is_handle_and_two_signed_shorts()
    {
        var frame = GameSmallPackets.Sp(0x12345678, 234, 1000);
        Header(frame, 15, 514);
        BinaryPrimitives.ReadUInt32LittleEndian(frame.AsSpan(7)).Should().Be(0x12345678);
        BinaryPrimitives.ReadInt16LittleEndian(frame.AsSpan(11)).Should().Be(234);
        BinaryPrimitives.ReadInt16LittleEndian(frame.AsSpan(13)).Should().Be(1000);
        Action overflow = () => GameSmallPackets.Sp(1, 40000, 40000);
        overflow.Should().Throw<OverflowException>();
    }

    [Test]
    public void Skill_levels_have_five_byte_entries_and_no_actor_handle()
    {
        var frame = GameSmallPackets.SkillLevels(new[] { new KeyValuePair<int, byte>(40011, 2), new KeyValuePair<int, byte>(40210, 3) });
        Header(frame, 19, 451);
        BinaryPrimitives.ReadUInt16LittleEndian(frame.AsSpan(7)).Should().Be(2);
        BinaryPrimitives.ReadInt32LittleEndian(frame.AsSpan(9)).Should().Be(40011); frame[13].Should().Be(2);
        BinaryPrimitives.ReadInt32LittleEndian(frame.AsSpan(14)).Should().Be(40210); frame[18].Should().Be(3);
        Header(GameSmallPackets.SkillLevels(Array.Empty<KeyValuePair<int, byte>>()), 9, 451);
    }

    [Test]
    public void Skill_levels_keep_the_official_4096_byte_buffer_limit()
    {
        var frame = GameSmallPackets.SkillLevels(Enumerable.Range(1, 1000).Select(i => new KeyValuePair<int, byte>(i, 1)).ToArray());
        Header(frame, 4094, 451);
        BinaryPrimitives.ReadUInt16LittleEndian(frame.AsSpan(7)).Should().Be(817);
        BinaryPrimitives.ReadInt32LittleEndian(frame.AsSpan(4089)).Should().Be(817);
    }

    [Test]
    public void Window_strings_are_utf8_byte_counts_and_have_no_nul()
    {
        var frame = GameSmallPackets.ShowWindow("confirm_window", "Été", "go()");
        Header(frame, 36, 3003);
        BinaryPrimitives.ReadUInt16LittleEndian(frame.AsSpan(7)).Should().Be(14);
        BinaryPrimitives.ReadUInt16LittleEndian(frame.AsSpan(9)).Should().Be(5);
        BinaryPrimitives.ReadUInt16LittleEndian(frame.AsSpan(11)).Should().Be(4);
        frame.AsSpan(13).ToArray().Should().Equal(Encoding.UTF8.GetBytes("confirm_windowÉtégo()"));
    }

    [Test]
    public void Message_strings_are_utf8_byte_counts_without_terminator()
    {
        var frame = GameSmallPackets.GeneralMessageBox("Été");
        Header(frame, 14, 3004);
        BinaryPrimitives.ReadUInt16LittleEndian(frame.AsSpan(7)).Should().Be(5);
        frame.AsSpan(9).ToArray().Should().Equal(Encoding.UTF8.GetBytes("Été"));
    }

    [Test]
    public void Script_frames_accept_1024_bytes_and_ignore_larger_frames()
    {
        Header(GameSmallPackets.ShowWindow("w", new string('a', 1009), "t"), 1024, 3003);
        GameSmallPackets.ShowWindow("w", new string('a', 1010), "t").Should().BeNull();
        Header(GameSmallPackets.GeneralMessageBox(new string('a', 1015)), 1024, 3004);
        GameSmallPackets.GeneralMessageBox(new string('a', 1016)).Should().BeNull();
        GameSmallPackets.GeneralMessageBox(new string('é', 508)).Should().BeNull("the limit counts bytes");
    }

    [Test]
    public void Script_strings_follow_strlen_for_embedded_nuls_and_allow_empty_text()
    {
        Header(GameSmallPackets.GeneralMessageBox(null), 9, 3004);
        GameSmallPackets.GeneralMessageBox("a\0b").AsSpan(9).ToArray().Should().Equal((byte)'a');
        Header(GameSmallPackets.ShowWindow(null, "", ""), 13, 3003);
    }

    [TestCase(322)] [TestCase(450)] [TestCase(451)] [TestCase(512)] [TestCase(514)] [TestCase(3003)] [TestCase(3004)]
    public void Declared_ids_are_consumed_without_client_side_effects(int id)
    {
        var frame = new byte[7]; BinaryPrimitives.WriteUInt32LittleEndian(frame, 7);
        BinaryPrimitives.WriteUInt16LittleEndian(frame.AsSpan(4), (ushort)id);
        frame[6] = unchecked((byte)frame.Take(6).Sum(b => b));
        var wire = new StorageTestHarness.FrameConnection(frame);
        var client = StorageTestHarness.NewGameClient(wire);
        Action receive = () => client.OnDataReceived(frame.Length);
        receive.Should().NotThrow(); wire.BytesAvailable.Should().Be(0); wire.Sent.Should().BeEmpty();
    }

    [Test]
    public void A_server_target_is_private_and_a_client_target_is_not_echoed()
    {
        var wire = new StorageTestHarness.FrameConnection(Array.Empty<byte>());
        var client = StorageTestHarness.NewGameClient(wire);
        client.SetTarget(123);
        StorageTestHarness.Session(client).TargetHandle.Should().Be(123);
        Header(wire.Sent.Single(), 11, 512);
        var request = GameSmallPackets.Target(321);
        BinaryPrimitives.WriteUInt16LittleEndian(request.AsSpan(4), 511);
        request[6] = unchecked((byte)request.Take(6).Sum(b => b));
        var input = new StorageTestHarness.FrameConnection(request);
        var recipient = StorageTestHarness.NewGameClient(input);
        recipient.OnDataReceived(request.Length);
        StorageTestHarness.Session(recipient).TargetHandle.Should().Be(321);
        input.Sent.Should().BeEmpty("echoing 511 with 512 can create a targeting loop");
    }

    [Test]
    public void The_323_dispatch_passes_the_name_to_the_existing_creature_service()
    {
        var frame = new byte[26]; BinaryPrimitives.WriteUInt32LittleEndian(frame, 26);
        BinaryPrimitives.WriteUInt16LittleEndian(frame.AsSpan(4), 323);
        Encoding.ASCII.GetBytes("Renamed").CopyTo(frame, 7);
        frame[6] = unchecked((byte)frame.Take(6).Sum(b => b));
        var creatures = A.Fake<ICreatureService>();
        var wire = new StorageTestHarness.FrameConnection(frame);
        var client = StorageTestHarness.NewGameClient(wire, creatureService: creatures);
        client.OnDataReceived(frame.Length);
        A.CallTo(() => creatures.ChangeNameAsync(client, "Renamed")).MustHaveHappenedOnceExactly();
        wire.BytesAvailable.Should().Be(0);
    }
}
