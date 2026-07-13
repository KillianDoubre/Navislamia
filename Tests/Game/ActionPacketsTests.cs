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
    }
}
