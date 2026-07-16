using System.Buffers.Binary;
using FluentAssertions;
using Navislamia.Game.Network.Clients;
using Navislamia.Game.Network.Packets;
using Navislamia.Game.Network.Packets.Enums;
using Navislamia.Game.Network.Packets.Game;

namespace Tests.Game;

[TestFixture]
public class ActionPacketsTests
{
    private static byte[] HandlePacket(uint handle)
    {
        var packet = new byte[11];
        BinaryPrimitives.WriteUInt32LittleEndian(packet.AsSpan(7, 4), handle);
        return packet;
    }

    [Test]
    public void ReadTargetHandle_ReturnsEncodedHandle()
    {
        var packet = HandlePacket(0x40000123u);

        GameActionPackets.ReadTargetHandle(packet).Should().Be(0x40000123u);
    }

    [Test]
    public void ReadTargetHandle_ReturnsZeroForDeselect()
    {
        var packet = HandlePacket(0u);

        GameActionPackets.ReadTargetHandle(packet).Should().Be(0u);
    }

    [Test]
    public void ReadCancelActionHandle_ReturnsEncodedHandle()
    {
        var packet = HandlePacket(0x40000456u);

        GameActionPackets.ReadCancelActionHandle(packet).Should().Be(0x40000456u);
    }

    [Test]
    public void TryReadLearnSkill_ReadsTheEpic73Layout()
    {
        var packet = new byte[17];
        BinaryPrimitives.WriteUInt32LittleEndian(packet.AsSpan(7, 4), 0x40000123u);
        BinaryPrimitives.WriteInt32LittleEndian(packet.AsSpan(11, 4), 1004);
        packet[15] = 1;

        GameActionPackets.TryReadLearnSkill(packet, out var request).Should().BeTrue();
        request.Handle.Should().Be(0x40000123u);
        request.SkillId.Should().Be(1004);
        request.TargetLevel.Should().Be(1);
    }

    [Test]
    public void TryReadLearnSkill_RejectsTruncatedOrNonZeroPadding()
    {
        GameActionPackets.TryReadLearnSkill(new byte[16], out _).Should().BeFalse();

        var packet = new byte[17];
        packet[16] = 1;
        GameActionPackets.TryReadLearnSkill(packet, out _).Should().BeFalse();
    }

    [Test]
    public void TryReadPutoffItem_ReadsPositionAndTargetHandle()
    {
        var packet = new byte[12];
        packet[7] = 2;
        BinaryPrimitives.WriteUInt32LittleEndian(packet.AsSpan(8, 4), 0x40000123u);

        GameActionPackets.TryReadPutoffItem(packet, out var request).Should().BeTrue();
        request.Position.Should().Be(2);
        request.TargetHandle.Should().Be(0x40000123u);
    }

    [Test]
    public void TryReadPutoffItem_RejectsTruncated()
    {
        GameActionPackets.TryReadPutoffItem(new byte[11], out _).Should().BeFalse();
    }

    [Test]
    public void TryReadPutonItem_ReadsPositionItemHandleAndTargetHandle()
    {
        var packet = new byte[16];
        packet[7] = 5;
        BinaryPrimitives.WriteUInt32LittleEndian(packet.AsSpan(8, 4), 0x80000456u);
        BinaryPrimitives.WriteUInt32LittleEndian(packet.AsSpan(12, 4), 0x40000123u);

        GameActionPackets.TryReadPutonItem(packet, out var request).Should().BeTrue();
        request.Position.Should().Be(5);
        request.ItemHandle.Should().Be(0x80000456u);
        request.TargetHandle.Should().Be(0x40000123u);
    }

    [Test]
    public void TryReadPutonItem_RejectsTruncated()
    {
        GameActionPackets.TryReadPutonItem(new byte[15], out _).Should().BeFalse();
    }

    [Test]
    public void TryReadPutonItem_ReadsNoneAsANegativePosition()
    {
        var packet = new byte[16];
        packet[7] = 0xFF;

        GameActionPackets.TryReadPutonItem(packet, out var request).Should().BeTrue();
        request.Position.Should().Be(-1);
    }

    [Test]
    public void TryReadArrangeItem_ReadsTheStorageFlagFromTheSingleBytePayload()
    {
        var inventory = new byte[8];
        GameActionPackets.TryReadArrangeItem(inventory, out var isStorage).Should().BeTrue();
        isStorage.Should().BeFalse();

        var storage = new byte[8];
        storage[7] = 1;
        GameActionPackets.TryReadArrangeItem(storage, out isStorage).Should().BeTrue();
        isStorage.Should().BeTrue();
    }

    [Test]
    public void TryReadArrangeItem_RejectsAHeaderOnlyPacket()
    {
        GameActionPackets.TryReadArrangeItem(new byte[7], out _).Should().BeFalse();
    }

    [Test]
    public void TryReadChangeItemPosition_ReadsTheStorageFlagAndBothHandles()
    {
        var packet = new byte[16];
        packet[7] = 0;
        BinaryPrimitives.WriteUInt32LittleEndian(packet.AsSpan(8, 4), 0x80000111u);
        BinaryPrimitives.WriteUInt32LittleEndian(packet.AsSpan(12, 4), 0x80000222u);

        GameActionPackets.TryReadChangeItemPosition(packet, out var request).Should().BeTrue();
        request.IsStorage.Should().BeFalse();
        request.ItemHandle1.Should().Be(0x80000111u);
        request.ItemHandle2.Should().Be(0x80000222u);
    }

    [Test]
    public void TryReadChangeItemPosition_RejectsTruncated()
    {
        GameActionPackets.TryReadChangeItemPosition(new byte[15], out _).Should().BeFalse();
    }

    [Test]
    public void ConnectionInfo_TargetHandleDefaultsToZero()
    {
        new ConnectionInfo().TargetHandle.Should().Be(0u);
    }

    [TestCase(GamePackets.TM_CS_REQUEST_RETURN_LOBBY)]
    [TestCase(GamePackets.TM_CS_RETURN_LOBBY)]
    public void ResultPacket_UsesTheRequestIdExpectedByTheClient(GamePackets requestId)
    {
        var packet = new Packet<TS_SC_RESULT>((ushort)GamePackets.TM_SC_RESULT,
            new TS_SC_RESULT((ushort)requestId, 0));

        packet.Data.Should().HaveCount(15);
        BinaryPrimitives.ReadUInt32LittleEndian(packet.Data).Should().Be(15);
        BinaryPrimitives.ReadUInt16LittleEndian(packet.Data.AsSpan(4)).Should().Be((ushort)GamePackets.TM_SC_RESULT);
        BinaryPrimitives.ReadUInt16LittleEndian(packet.Data.AsSpan(7)).Should().Be((ushort)requestId);
        BinaryPrimitives.ReadUInt16LittleEndian(packet.Data.AsSpan(9)).Should().Be(0);
        BinaryPrimitives.ReadInt32LittleEndian(packet.Data.AsSpan(11)).Should().Be(0);
    }

    [Test]
    public void ClearCharacterSession_RemovesWorldStateButPreservesTheAccountSession()
    {
        var info = new ConnectionInfo
        {
            AccountName = "account",
            AccountId = 42,
            AuthVerified = true,
            CharacterHandle = 100,
            TargetHandle = 200,
            CharacterName = "Character",
            CharacterHp = 500,
            CharacterLevel = 25,
            CharacterRace = 3,
            CharacterJob = 300,
            CharacterJobLevel = 12,
            CharacterExp = 1234,
            CharacterJp = 567,
            CharacterGold = 890,
            CharacterChaos = 12,
            NameToDelete = "OtherCharacter",
            Layer = 3,
            X = 1000,
            Y = 2000,
            Z = 3000
        };
        info.SpawnedNpcs[1] = 10;
        info.SpawnedNpcIdsByHandle[10] = 1;
        info.SpawnedMonsters[2] = 20;
        info.NpcDialogHandle = 10;
        info.NpcDialogTriggers.Add("Next()");
        info.LearnedSkills[1004] = 1;

        info.ClearCharacterSession();

        info.AccountName.Should().Be("account");
        info.AccountId.Should().Be(42);
        info.AuthVerified.Should().BeTrue();
        info.CharacterHandle.Should().Be(0);
        info.TargetHandle.Should().Be(0);
        info.CharacterName.Should().BeEmpty();
        info.CharacterHp.Should().Be(0);
        info.CharacterLevel.Should().Be(0);
        info.CharacterRace.Should().Be(0);
        info.CharacterJob.Should().Be(0);
        info.CharacterJobLevel.Should().Be(0);
        info.CharacterExp.Should().Be(0);
        info.CharacterJp.Should().Be(0);
        info.CharacterGold.Should().Be(0);
        info.CharacterChaos.Should().Be(0);
        info.NameToDelete.Should().BeEmpty();
        info.Layer.Should().Be(0);
        info.X.Should().Be(0);
        info.Y.Should().Be(0);
        info.Z.Should().Be(0);
        info.SpawnedNpcs.Should().BeEmpty();
        info.SpawnedNpcIdsByHandle.Should().BeEmpty();
        info.SpawnedMonsters.Should().BeEmpty();
        info.NpcDialogHandle.Should().Be(0);
        info.NpcDialogTriggers.Should().BeEmpty();
        info.LearnedSkills.Should().BeEmpty();
    }
}
