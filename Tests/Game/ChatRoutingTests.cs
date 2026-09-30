using System;
using System.Buffers.Binary;
using System.Text;
using FakeItEasy;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Navislamia.Game.Network.Packets;
using Navislamia.Game.Network.Packets.Enums;
using Navislamia.Game.Services;

namespace Tests.Game;

[TestFixture]
public class ChatRoutingTests
{
    [TestCase(ChatType.Normal, 100f, true)]
    [TestCase(ChatType.Normal, 1000f, false)]
    [TestCase(ChatType.Global, 1000f, true)]
    [TestCase(ChatType.Whisper, 1000f, true)]
    public void RoutesToTheIntendedPlayer(ChatType type, float distance, bool reachesPeer)
    {
        var visibility = new PlayerVisibilityService(A.Fake<ILogger<PlayerVisibilityService>>());
        var frame = Request(type, "hello", type == ChatType.Whisper ? "Target" : "");
        var senderConnection = new StorageTestHarness.FrameConnection(frame);
        var peerConnection = new StorageTestHarness.FrameConnection(Array.Empty<byte>());
        var sender = StorageTestHarness.NewGameClient(senderConnection, playerVisibilityService: visibility);
        var peer = StorageTestHarness.NewGameClient(peerConnection, playerVisibilityService: visibility);
        StorageTestHarness.Session(sender).CharacterHandle = 1;
        StorageTestHarness.Session(sender).CharacterName = "Sender";
        StorageTestHarness.Session(peer).CharacterHandle = 2;
        StorageTestHarness.Session(peer).CharacterName = "Target";
        visibility.Registry.Register(1, sender);
        visibility.Registry.Register(2, peer);
        visibility.Index.Add(new PlayerPresence(1, new PlayerAppearance(), 0, 0, 0, 0));
        visibility.Index.Add(new PlayerPresence(2, new PlayerAppearance(), 0, distance, 0, 0));

        sender.OnDataReceived(frame.Length);

        senderConnection.Sent.Should().ContainSingle();
        peerConnection.Sent.Count.Should().Be(reachesPeer ? 1 : 0);
    }

    [Test]
    public void PartyMessageOnlyReachesMembers()
    {
        var visibility = new PlayerVisibilityService(A.Fake<ILogger<PlayerVisibilityService>>());
        var frame = Request(ChatType.Party, "party");
        var senderConnection = new StorageTestHarness.FrameConnection(frame);
        var peerConnection = new StorageTestHarness.FrameConnection(Array.Empty<byte>());
        var sender = StorageTestHarness.NewGameClient(senderConnection, playerVisibilityService: visibility);
        var peer = StorageTestHarness.NewGameClient(peerConnection, playerVisibilityService: visibility);
        StorageTestHarness.Session(sender).CharacterHandle = 1;
        StorageTestHarness.Session(sender).CharacterName = "Sender";
        StorageTestHarness.Session(sender).PartyId = 5;
        StorageTestHarness.Session(peer).CharacterHandle = 2;
        StorageTestHarness.Session(peer).CharacterName = "Target";
        StorageTestHarness.Session(peer).PartyId = 6;
        visibility.Registry.Register(1, sender);
        visibility.Registry.Register(2, peer);

        sender.OnDataReceived(frame.Length);

        senderConnection.Sent.Should().ContainSingle();
        peerConnection.Sent.Should().BeEmpty();
    }

    [TestCase("Target", ResultCode.Success, 1)]
    [TestCase("Nobody", ResultCode.NotExist, 0)]
    public void WhisperAnswersTheSenderWithAResultAndIgnoresTheRequestId(string target, ResultCode expected,
        int reachesPeer)
    {
        var visibility = new PlayerVisibilityService(A.Fake<ILogger<PlayerVisibilityService>>());
        var frame = Request(ChatType.Whisper, "psst", target, requestId: 0x41);
        var senderConnection = new StorageTestHarness.FrameConnection(frame);
        var peerConnection = new StorageTestHarness.FrameConnection(Array.Empty<byte>());
        var sender = StorageTestHarness.NewGameClient(senderConnection, playerVisibilityService: visibility);
        var peer = StorageTestHarness.NewGameClient(peerConnection, playerVisibilityService: visibility);
        StorageTestHarness.Session(sender).CharacterHandle = 1;
        StorageTestHarness.Session(sender).CharacterName = "Sender";
        StorageTestHarness.Session(peer).CharacterHandle = 2;
        StorageTestHarness.Session(peer).CharacterName = "Target";
        visibility.Registry.Register(1, sender);
        visibility.Registry.Register(2, peer);

        sender.OnDataReceived(frame.Length);

        // The sender is answered on the chat request (NGemity onChatRequest), never echoed the line.
        var result = senderConnection.Sent.Should().ContainSingle().Subject;
        BinaryPrimitives.ReadUInt16LittleEndian(result.AsSpan(4, 2)).Should().Be((ushort)GamePackets.TM_SC_RESULT);
        BinaryPrimitives.ReadUInt16LittleEndian(result.AsSpan(7, 2)).Should().Be((ushort)GamePackets.TM_CS_CHAT_REQUEST);
        BinaryPrimitives.ReadUInt16LittleEndian(result.AsSpan(9, 2)).Should().Be((ushort)expected);
        peerConnection.Sent.Count.Should().Be(reachesPeer);
    }

    private static byte[] Request(ChatType type, string text, string target = "", byte requestId = 0)
    {
        var message = Encoding.ASCII.GetBytes(text + "\0");
        var frame = new byte[31 + message.Length];
        BinaryPrimitives.WriteUInt32LittleEndian(frame.AsSpan(0, 4), (uint)frame.Length);
        BinaryPrimitives.WriteUInt16LittleEndian(frame.AsSpan(4, 2), (ushort)GamePackets.TM_CS_CHAT_REQUEST);
        frame[6] = StorageTestHarness.Checksum(frame);
        Encoding.ASCII.GetBytes(target).CopyTo(frame, 7);
        frame[28] = requestId;
        frame[29] = (byte)message.Length;
        frame[30] = (byte)type;
        message.CopyTo(frame, 31);
        return frame;
    }
}
