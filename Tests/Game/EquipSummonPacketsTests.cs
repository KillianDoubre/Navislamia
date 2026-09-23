using System.Buffers.Binary;
using FluentAssertions;
using Navislamia.Game.Network.Packets.Enums;
using Navislamia.Game.Network.Packets.Game;

namespace Tests.Game;

/// <summary>
/// TM_EQUIP_SUMMON (303) in the client to server direction, sent when the player validates a creature
/// formation: 32 bytes, the <c>open_dialog</c> byte at 7 then six card handles from 8. The id is declared
/// for the server's own 303, so without a receive arm this frame reached the "Unknown Packet Type" throw —
/// which is what the first in-game try of the formation window logged. The arm re-sends the stored
/// formation: NGemity's answer when no card carries ITEM_FLAG_SUMMON, which none can here.
/// See docs/packet-specs/324-get-summon-setup-info.md §14.
/// </summary>
[TestFixture]
public class EquipSummonPacketsTests
{
    private const int PacketLength = 32;

    private static byte[] ClientFrame(byte openDialog, params uint[] handles)
    {
        var packet = new byte[PacketLength];
        BinaryPrimitives.WriteUInt32LittleEndian(packet.AsSpan(0, 4), PacketLength);
        BinaryPrimitives.WriteUInt16LittleEndian(packet.AsSpan(4, 2), (ushort)GamePackets.TM_EQUIP_SUMMON);
        packet[7] = openDialog;
        for (var i = 0; i < handles.Length; i++)
        {
            BinaryPrimitives.WriteUInt32LittleEndian(packet.AsSpan(8 + i * 4, 4), handles[i]);
        }

        packet[6] = StorageTestHarness.Checksum(packet);
        return packet;
    }

    private static uint HandleAt(byte[] packet, int slot) =>
        BinaryPrimitives.ReadUInt32LittleEndian(packet.AsSpan(8 + slot * 4, 4));

    [Test]
    public void TryReadEquipSummon_ReadsTheDialogByteAndTheSixHandles()
    {
        var frame = ClientFrame(0, 11, 0, 33, 0, 0, 0x01020304);

        GameActionPackets.TryReadEquipSummon(frame, out var request).Should().BeTrue();

        request.OpenDialog.Should().BeFalse();
        request.CardHandles.Should().Equal(11u, 0u, 33u, 0u, 0u, 0x01020304u);
    }

    [TestCase(31)]
    [TestCase(33)]
    [TestCase(8)]
    public void TryReadEquipSummon_RefusesAnyLengthOtherThanThirtyTwo(int length)
    {
        GameActionPackets.TryReadEquipSummon(new byte[length], out _).Should().BeFalse();
    }

    [Test]
    public void OnDataReceived_SendsTheStoredFormationBackInsteadOfThrowing()
    {
        var connection = new StorageTestHarness.FrameConnection(ClientFrame(0, 540001));
        var client = StorageTestHarness.NewGameClient(connection);
        var info = StorageTestHarness.Session(client);
        info.CharacterHandle = 77;
        info.SummonSlots = new long[] { 401 };

        var receive = () => client.OnDataReceived(PacketLength);

        receive.Should().NotThrow("the declared id must be claimed before the throwing switch");
        connection.Sent.Should().ContainSingle();
        var answer = connection.Sent[0];
        BinaryPrimitives.ReadUInt16LittleEndian(answer.AsSpan(4, 2)).Should().Be(303);
        answer[7].Should().Be(0, "the request's open_dialog is replayed");
        HandleAt(answer, 0).Should().Be(401, "the untamed card is refused and the stored formation stands");
        info.SummonSlots.Should().Equal(401L);
    }

    [Test]
    public void OnDataReceived_AnswersNothingBeforeTheCharacterEnteredTheWorld()
    {
        var connection = new StorageTestHarness.FrameConnection(ClientFrame(0, 540001));
        var client = StorageTestHarness.NewGameClient(connection);

        var receive = () => client.OnDataReceived(PacketLength);

        receive.Should().NotThrow();
        connection.Sent.Should().BeEmpty();
    }
}
