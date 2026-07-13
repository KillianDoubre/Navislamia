using System.Buffers.Binary;
using FluentAssertions;
using Navislamia.Game.DataAccess.Entities.Enums;
using Navislamia.Game.DataAccess.Entities.Telecaster;
using Navislamia.Game.Network.Packets.Enums;
using Navislamia.Game.Network.Packets.Game;

namespace Tests.Game;

[TestFixture]
public class GameCharacterPacketsTests
{
    [Test]
    public void ModelIds_UseTheClientCalibratedFaceThenHairOrder()
    {
        var character = new CharacterEntity { Models = new[] { 101, 205, 301, 401, 501 } };

        GameCharacterPackets.GetFaceId(character).Should().Be(101);
        GameCharacterPackets.GetHairId(character).Should().Be(205);
    }

    [Test]
    public void BuildWearInfo_UsesTheClientCalibratedEpic73Models()
    {
        var character = new CharacterEntity
        {
            Models = new[] { 101, 205, 301, 401, 501 },
            Items = new List<ItemEntity>
            {
                new() { ItemResourceId = 240100, WearInfo = ItemWearType.Armor, Level = 1 }
            }
        };

        var packet = GameCharacterPackets.BuildWearInfo(0x11223344, character);

        packet.Length.Should().Be(323);
        AssertFrame(packet, GamePackets.TM_SC_WEAR_INFO);
        BinaryPrimitives.ReadUInt32LittleEndian(packet.AsSpan(7, 4)).Should().Be(0x11223344);

        const int codeBase = 11;
        ReadSlot(packet, codeBase, ItemWearType.Armor).Should().Be(240100);
        ReadSlot(packet, codeBase, ItemWearType.Glove).Should().Be(401);
        ReadSlot(packet, codeBase, ItemWearType.Boots).Should().Be(501);
        ReadSlot(packet, codeBase, ItemWearType.Face).Should().Be(0);
        ReadSlot(packet, codeBase, ItemWearType.Hair).Should().Be(205);
    }

    [Test]
    public void BuildInventory_WritesAnEpic73Item()
    {
        var character = new CharacterEntity
        {
            Items = new List<ItemEntity>
            {
                new()
                {
                    Id = 123,
                    ItemResourceId = 240100,
                    Amount = 2,
                    EtherealDurability = 3,
                    Endurance = 50,
                    Enhance = 4,
                    Level = 5,
                    WearInfo = ItemWearType.Armor,
                    Idx = 6
                }
            }
        };

        var packet = GameCharacterPackets.BuildInventory(character).Single();

        packet.Length.Should().Be(94);
        AssertFrame(packet, GamePackets.TM_SC_INVENTORY);
        BinaryPrimitives.ReadUInt16LittleEndian(packet.AsSpan(7, 2)).Should().Be(1);
        BinaryPrimitives.ReadUInt32LittleEndian(packet.AsSpan(9, 4)).Should().Be(123);
        BinaryPrimitives.ReadInt32LittleEndian(packet.AsSpan(13, 4)).Should().Be(240100);
        BinaryPrimitives.ReadInt64LittleEndian(packet.AsSpan(17, 8)).Should().Be(123);
        BinaryPrimitives.ReadInt64LittleEndian(packet.AsSpan(25, 8)).Should().Be(2);
        BinaryPrimitives.ReadInt32LittleEndian(packet.AsSpan(33, 4)).Should().Be(3);
        BinaryPrimitives.ReadUInt32LittleEndian(packet.AsSpan(37, 4)).Should().Be(50);
        packet[41].Should().Be(4);
        packet[42].Should().Be(5);
        BinaryPrimitives.ReadInt32LittleEndian(packet.AsSpan(80, 4)).Should().Be(0);
        BinaryPrimitives.ReadInt16LittleEndian(packet.AsSpan(84, 2)).Should().Be((short)ItemWearType.Armor);
        BinaryPrimitives.ReadInt32LittleEndian(packet.AsSpan(90, 4)).Should().Be(6);
    }

    [Test]
    public void BuildProgressionPackets_WriteTheExpectedEpic73Fields()
    {
        var level = GameCharacterPackets.BuildLevelUpdate(42, 10, 7);
        var exp = GameCharacterPackets.BuildExpUpdate(42, 1234, 5678);
        var gold = GameCharacterPackets.BuildGoldUpdate(9999, 12);

        AssertFrame(level, GamePackets.TM_SC_LEVEL_UPDATE);
        level.Length.Should().Be(19);
        BinaryPrimitives.ReadUInt32LittleEndian(level.AsSpan(7, 4)).Should().Be(42);
        BinaryPrimitives.ReadInt32LittleEndian(level.AsSpan(11, 4)).Should().Be(10);
        BinaryPrimitives.ReadInt32LittleEndian(level.AsSpan(15, 4)).Should().Be(7);

        AssertFrame(exp, GamePackets.TM_SC_EXP_UPDATE);
        exp.Length.Should().Be(27);
        BinaryPrimitives.ReadUInt64LittleEndian(exp.AsSpan(11, 8)).Should().Be(1234);
        BinaryPrimitives.ReadUInt64LittleEndian(exp.AsSpan(19, 8)).Should().Be(5678);

        AssertFrame(gold, GamePackets.TM_SC_GOLD_UPDATE);
        gold.Length.Should().Be(19);
        BinaryPrimitives.ReadUInt64LittleEndian(gold.AsSpan(7, 8)).Should().Be(9999);
        BinaryPrimitives.ReadUInt32LittleEndian(gold.AsSpan(15, 4)).Should().Be(12);
    }

    [Test]
    public void BuildBaseStatePackets_UseTheExpectedEpic73Sizes()
    {
        var emptyInventory = GameCharacterPackets.BuildInventory(new CharacterEntity()).Single();
        var summon = GameCharacterPackets.BuildEquipSummon(null!);
        var skills = GameCharacterPackets.BuildEmptyAddedSkillList(42);
        var belt = GameCharacterPackets.BuildBeltSlotInfo(null!);
        var status = GameCharacterPackets.BuildStatusChange(42);

        emptyInventory.Length.Should().Be(9);
        summon.Length.Should().Be(32);
        skills.Length.Should().Be(13);
        belt.Length.Should().Be(31);
        status.Length.Should().Be(15);

        AssertFrame(emptyInventory, GamePackets.TM_SC_INVENTORY);
        AssertFrame(summon, GamePackets.TM_EQUIP_SUMMON);
        AssertFrame(skills, GamePackets.TM_SC_ADDED_SKILL_LIST);
        AssertFrame(belt, GamePackets.TM_SC_BELT_SLOT_INFO);
        AssertFrame(status, GamePackets.TM_SC_STATUS_CHANGE);
    }

    [Test]
    public void BuildAppearancePackets_WriteAllClientAppearanceFields()
    {
        var hair = GameCharacterPackets.BuildHairInfo(42, 205, 3, 0x00112233);
        var hide = GameCharacterPackets.BuildHideEquipInfo(42, 7);
        var skin = GameCharacterPackets.BuildSkinInfo(42, unchecked((int)0x00818080));

        hair.Length.Should().Be(23);
        hide.Length.Should().Be(15);
        skin.Length.Should().Be(15);

        AssertFrame(hair, GamePackets.TM_SC_HAIR_INFO);
        AssertFrame(hide, GamePackets.TM_SC_HIDE_EQUIP_INFO);
        AssertFrame(skin, GamePackets.TM_SC_SKIN_INFO);

        BinaryPrimitives.ReadUInt32LittleEndian(hair.AsSpan(7, 4)).Should().Be(42);
        BinaryPrimitives.ReadUInt32LittleEndian(hair.AsSpan(11, 4)).Should().Be(205);
        BinaryPrimitives.ReadInt32LittleEndian(hair.AsSpan(15, 4)).Should().Be(3);
        BinaryPrimitives.ReadUInt32LittleEndian(hair.AsSpan(19, 4)).Should().Be(0x00112233);
        BinaryPrimitives.ReadUInt32LittleEndian(hide.AsSpan(11, 4)).Should().Be(7);
        BinaryPrimitives.ReadUInt32LittleEndian(skin.AsSpan(11, 4)).Should().Be(0x00818080);
    }

    private static uint ReadSlot(byte[] packet, int codeBase, ItemWearType slot)
    {
        return BinaryPrimitives.ReadUInt32LittleEndian(packet.AsSpan(codeBase + (int)slot * 4, 4));
    }

    private static void AssertFrame(byte[] packet, GamePackets id)
    {
        BinaryPrimitives.ReadUInt32LittleEndian(packet.AsSpan(0, 4)).Should().Be((uint)packet.Length);
        BinaryPrimitives.ReadUInt16LittleEndian(packet.AsSpan(4, 2)).Should().Be((ushort)id);

        byte checksum = 0;
        for (var i = 0; i < 6; i++)
        {
            checksum += packet[i];
        }

        packet[6].Should().Be(checksum);
    }
}
