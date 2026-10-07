using System.Buffers.Binary;
using FluentAssertions;
using Navislamia.Game.DataAccess.Entities.Enums;
using Navislamia.Game.DataAccess.Entities.Telecaster;
using Navislamia.Game.Network.Packets.Game;
using Navislamia.Game.Services.Creatures;
using NUnit.Framework;

namespace Tests.Game;

/// <summary>
/// The creature a bound card holds travels at offset 71 of its item record (<c>summon_code</c> of the official
/// <c>SetSummonCode</c>); the 7.3 client draws every creature portrait from it (SFrame.exe <c>0x4a69a0</c>).
/// </summary>
public class CardSummonCodeTests
{
    private static ItemEntity BoundCard(long id, int code = 0) => new()
    {
        Id = id, ItemResourceId = 540014, Amount = 1, Flag = CreatureRules.WithSummonFlag(ItemFlag.None),
        AppearanceCode = code, SocketItemIds = new long[4]
    };

    [Test]
    public void World_entry_writes_the_summon_code_on_each_bound_card_like_DB_Login()
    {
        var bound = BoundCard(60);
        var already = BoundCard(61, 2102);
        var empty = new ItemEntity { Id = 62, ItemResourceId = 540014, Amount = 3, Flag = ItemFlag.None };
        var summons = new[]
        {
            new SummonEntity { CardItemId = 60, SummonResourceId = 2101 },
            new SummonEntity { CardItemId = 61, SummonResourceId = 2102 },
            new SummonEntity { CardItemId = 62, SummonResourceId = 2103 }
        };

        CreatureRules.SyncCardSummonCodes(new[] { bound, already, empty }, summons).Should().BeTrue();

        bound.AppearanceCode.Should().Be(2101);
        already.AppearanceCode.Should().Be(2102);
        empty.AppearanceCode.Should().Be(0, "an unbound card names no creature");
        CreatureRules.SyncCardSummonCodes(new[] { bound, already, empty }, summons).Should().BeFalse("nothing left to write");
    }

    [Test]
    public void The_inventory_record_carries_the_card_creature_at_offset_71()
    {
        var packet = GameCharacterPackets.BuildInventory(new[] { BoundCard(60, 2101) })[0];
        var record = packet.AsSpan(packet.Length - 85, 85);

        BinaryPrimitives.ReadUInt32LittleEndian(record.Slice(0, 4)).Should().Be(60u);
        BinaryPrimitives.ReadInt32LittleEndian(record.Slice(71, 4)).Should().Be(2101);
    }
}
