using System;
using System.Buffers.Binary;
using System.Linq;
using System.Text;
using FluentAssertions;
using Navislamia.Game.Network.Clients;
using Navislamia.Game.Network.Packets;
using Navislamia.Game.Network.Packets.Enums;
using Navislamia.Game.Network.Packets.Game;
using Navislamia.Game.Services;

namespace Tests.Game;

/// <summary>
/// The game rules of the player booth socle (docs/packet-specs/socle-booths.md §5.3): the refusals of
/// <c>TM_CS_START_BOOTH</c> with their codes, the action lock a booth open puts in front of the
/// receive chain, and the per connection state it leaves. The refusal code is the one the client and
/// the reference already declare — 55, <c>RESULT_NOT_ACTABLE_WHILE_USING_BOOTH</c> — and the strings
/// the client shows (<c>smsg_booth_cant_setup</c>, <c>_cant_setup_item</c>, <c>_name_short</c>,
/// <c>_name_long</c>) state the rules, never a code.
/// </summary>
[TestFixture]
public class BoothRulesTests
{
    private static readonly ushort[] GuardedActions =
    {
        (ushort)GamePackets.TM_CS_PUTON_ITEM,
        (ushort)GamePackets.TM_CS_PUTOFF_ITEM,
        (ushort)GamePackets.TM_CS_DROP_ITEM,
        (ushort)GamePackets.TM_CS_TAKE_ITEM,
        (ushort)GamePackets.TM_CS_ERASE_ITEM,
        (ushort)GamePackets.TM_CS_CHANGE_ITEM_POSITION,
        (ushort)GamePackets.TM_CS_ARRANGE_ITEM,
        (ushort)GamePackets.TM_CS_USE_ITEM,
        (ushort)GamePackets.TM_CS_SKILL,
        (ushort)GamePackets.TM_CS_WATCH_BOOTH
    };

    [Test]
    public void NotActableWhileUsingBooth_KeepsTheCodeBothSidesDeclare()
    {
        ((ushort)ResultCode.NotActableWhileUsingBooth).Should().Be(55);
    }

    [Test]
    public void ValidateStartBooth_AcceptsATypeOneOrTwo()
    {
        foreach (var type in new byte[] { 1, 2 })
        {
            BoothRules.ValidateStartBooth(Request(type: type), characterLevel: 10).Should().Be(ResultCode.Success);
        }
    }

    [Test]
    public void ValidateStartBooth_RefusesATypeOutsideOneAndTwo()
    {
        // The client proves the domain itself: setne al / inc al at VA 0x48D5FB gives 1 or 2 and
        // nothing else. Anything else is a hostile or broken frame.
        foreach (var type in new byte[] { 0, 3, 255 })
        {
            BoothRules.ValidateStartBooth(Request(type: type), characterLevel: 10)
                .Should().Be(ResultCode.InvalidArgument);
        }
    }

    [Test]
    public void ValidateStartBooth_RefusesABoothWithoutAnyItem()
    {
        // A frame of zero item is valid on the wire, but the rule is "at least 1 item for sale"
        // (smsg_booth_cant_setup_item, fiche §3.2).
        BoothRules.ValidateStartBooth(Request(items: 0), characterLevel: 10)
            .Should().Be(ResultCode.InvalidArgument);

        BoothRules.ValidateStartBooth(Request(items: 1), characterLevel: 10).Should().Be(ResultCode.Success);
    }

    [Test]
    public void ValidateStartBooth_RefusesANameShorterThanSixCharacters()
    {
        BoothRules.ValidateStartBooth(Request(nameLength: 5), characterLevel: 10)
            .Should().Be(ResultCode.InvalidArgument, "smsg_booth_name_short asks for 6 characters at least");

        BoothRules.ValidateStartBooth(Request(nameLength: 6), characterLevel: 10)
            .Should().Be(ResultCode.Success);
    }

    [Test]
    public void ValidateStartBooth_RefusesANameLongerThanFortyCharacters()
    {
        BoothRules.ValidateStartBooth(Request(nameLength: 41), characterLevel: 10)
            .Should().Be(ResultCode.InvalidArgument, "smsg_booth_name_long caps the name at 40 characters");

        BoothRules.ValidateStartBooth(Request(nameLength: 40), characterLevel: 10)
            .Should().Be(ResultCode.Success);
    }

    [Test]
    public void ValidateStartBooth_RefusesACharacterBelowLevelTen()
    {
        BoothRules.ValidateStartBooth(Request(), characterLevel: 9).Should().Be(ResultCode.NotEnoughLevel,
            "smsg_booth_cant_setup: you must be Lv 10 or higher to open a store");

        BoothRules.ValidateStartBooth(Request(), characterLevel: 10).Should().Be(ResultCode.Success);
        BoothRules.ValidateStartBooth(Request(), characterLevel: 1).Should().Be(ResultCode.NotEnoughLevel);
    }

    [Test]
    public void ValidateStartBooth_JudgesTheFieldsBeforeTheLevel()
    {
        // A level 9 character sending a malformed frame is answered for the frame: the level is the
        // last rule, so the client is told what is really wrong (fiche §5.3 point 2).
        BoothRules.ValidateStartBooth(Request(nameLength: 2), characterLevel: 1)
            .Should().Be(ResultCode.InvalidArgument);
    }

    [Test]
    public void TryAcceptStartBooth_AcceptsAFullFrameForALevelTenCharacter()
    {
        var packet = BuildStartBooth(count: 2);

        BoothRules.TryAcceptStartBooth(packet, characterLevel: 10, out var request, out var result)
            .Should().BeTrue();

        result.Should().Be(ResultCode.Success);
        request.Type.Should().Be(1);
        request.Name.Should().HaveCount(8);
        request.Items.Should().HaveCount(2);
        request.Items[1].Gold.Should().Be(2000);
    }

    [Test]
    public void TryAcceptStartBooth_AnswersNotEnoughLevelForALowCharacter()
    {
        var packet = BuildStartBooth(count: 1);

        BoothRules.TryAcceptStartBooth(packet, characterLevel: 9, out _, out var result).Should().BeFalse();

        result.Should().Be(ResultCode.NotEnoughLevel);
    }

    [Test]
    public void TryAcceptStartBooth_AnswersInvalidArgumentForAMalformedFrame()
    {
        BoothRules.TryAcceptStartBooth(new byte[58], characterLevel: 10, out _, out var result)
            .Should().BeFalse();

        result.Should().Be(ResultCode.InvalidArgument);
    }

    [Test]
    public void TryAcceptStartBooth_AnswersLimitMaxForACountAboveTheCeiling()
    {
        var packet = BuildStartBooth(count: 0);
        BinaryPrimitives.WriteUInt16LittleEndian(packet.AsSpan(BoothPackets.CountOffset, 2), 9);

        BoothRules.TryAcceptStartBooth(packet, characterLevel: 10, out _, out var result).Should().BeFalse();

        result.Should().Be(ResultCode.LimitMax);
    }

    [Test]
    public void TryAcceptStartBooth_AnswersInvalidArgumentForAnEmptyBooth()
    {
        var packet = BuildStartBooth(count: 0);

        BoothRules.TryAcceptStartBooth(packet, characterLevel: 10, out _, out var result).Should().BeFalse();

        result.Should().Be(ResultCode.InvalidArgument);
    }

    [Test]
    public void GateAction_RefusesEveryGuardedActionOfTheLockWhileABoothIsOpen()
    {
        foreach (var id in GuardedActions)
        {
            BoothRules.IsGuardedAction(id).Should().BeTrue();
            BoothRules.GateAction(isBoothOpen: true, id).Should().Be(ResultCode.NotActableWhileUsingBooth,
                $"action {id} is one the client announces as refused while its store is open");
            BoothRules.GateAction(isBoothOpen: false, id).Should().Be(ResultCode.Success,
                "with no booth open the action keeps its normal path");
        }
    }

    [Test]
    public void GateAction_CoversTheActionsTheClientAnnouncesAndNoMore()
    {
        foreach (var id in new ushort[] { 0, 1, 5, 20, 100, 150, 200, 201, 203, 204, 208, 218, 219, 253, 400, 500, 702, 704, 1202 })
        {
            var guarded = new[] { 200, 201, 203, 204, 208, 218, 219, 253, 400, 702 }.Contains(id);

            BoothRules.IsGuardedAction(id).Should().Be(guarded, $"action {id}");
        }

        // 704 is the way out of the observation, exactly like 701 is the way out of the booth: the lock
        // never covers it (docs/packet-specs/socle-booths-visibilite.md §5.2 point 9).
        BoothRules.GateAction(isBoothOpen: true, (ushort)GamePackets.TM_CS_STOP_WATCH_BOOTH)
            .Should().Be(ResultCode.Success);
    }

    [Test]
    public void GateAction_NeverGuardsThePacketsThatOpenOrCloseTheBooth()
    {
        // 701 is the way out of the lock and 700 is what creates it: both must reach their handler
        // while a booth is open.
        foreach (var id in new ushort[] { 700, 701 })
        {
            BoothRules.IsGuardedAction(id).Should().BeFalse();
            BoothRules.GateAction(isBoothOpen: true, id).Should().Be(ResultCode.Success);
        }
    }

    [Test]
    public void GateAction_RefusesAGuardedActionOnlyBetweenSevenHundredAndSevenHundredOne()
    {
        var info = new ConnectionInfo();

        info.IsBoothOpen.Should().BeFalse();
        BoothRules.GateAction(info.IsBoothOpen, (ushort)GamePackets.TM_CS_USE_ITEM)
            .Should().Be(ResultCode.Success);

        info.OpenBooth(Request());
        info.IsBoothOpen.Should().BeTrue();
        BoothRules.GateAction(info.IsBoothOpen, (ushort)GamePackets.TM_CS_USE_ITEM)
            .Should().Be(ResultCode.NotActableWhileUsingBooth);
        BoothRules.GateAction(info.IsBoothOpen, (ushort)GamePackets.TM_CS_STOP_BOOTH)
            .Should().Be(ResultCode.Success);

        info.CloseBooth().Should().BeTrue();
        info.IsBoothOpen.Should().BeFalse();
        BoothRules.GateAction(info.IsBoothOpen, (ushort)GamePackets.TM_CS_USE_ITEM)
            .Should().Be(ResultCode.Success);

        info.CloseBooth().Should().BeFalse("closing an already closed booth is idempotent");
    }

    [Test]
    public void OpenBooth_KeepsTheDeclarationUntilItIsClosed()
    {
        var info = new ConnectionInfo();
        var name = new byte[] { (byte)'B', (byte)'o', (byte)'u', (byte)'t', (byte)'i', (byte)'q', (byte)'u', (byte)'e' };
        var items = new[] { new BoothOpenItem(0x80000001u, 3, 250), new BoothOpenItem(0x80000002u, 1, 9999) };

        info.OpenBooth(new StartBoothRequest(2, name, items));

        info.IsBoothOpen.Should().BeTrue();
        info.Booth.Type.Should().Be(2);
        info.Booth.Name.Should().Equal(name);
        info.Booth.Items.Should().Equal(items);
        info.Booth.Items[0].ItemHandle.Should().Be(0x80000001u, "the socle keeps what the client declared");
        info.Booth.Items[0].Count.Should().Be(3);
        info.Booth.Items[0].Gold.Should().Be(250);

        info.CloseBooth().Should().BeTrue();
        info.Booth.Should().BeNull("the declared items are forgotten with the booth");
    }

    [Test]
    public void OpenBooth_ReplacesAPreviousDeclarationInsteadOfAccumulating()
    {
        var info = new ConnectionInfo();

        info.OpenBooth(new StartBoothRequest(1, "Premiere"u8.ToArray(), new[] { new BoothOpenItem(1, 1, 1) }));
        info.OpenBooth(new StartBoothRequest(2, "Seconde"u8.ToArray(), new[]
        {
            new BoothOpenItem(2, 2, 2),
            new BoothOpenItem(3, 3, 3)
        }));

        info.Booth.Type.Should().Be(2);
        info.Booth.Items.Should().HaveCount(2);
    }

    [Test]
    public void ClearCharacterSession_ClosesTheBoothSoTheNextCharacterNeverInheritsIt()
    {
        var info = new ConnectionInfo();
        info.OpenBooth(Request());

        info.ClearCharacterSession();

        info.IsBoothOpen.Should().BeFalse();
        info.Booth.Should().BeNull("a booth does not survive the character session, let alone a disconnection");
    }

    private static StartBoothRequest Request(byte type = 1, int nameLength = 8, int items = 1)
    {
        var name = Enumerable.Repeat((byte)'b', nameLength).ToArray();
        var declared = Enumerable.Range(0, items)
            .Select(i => new BoothOpenItem(0x80000000u + (uint)i, i + 1, 100 * (i + 1)))
            .ToArray();

        return new StartBoothRequest(type, name, declared);
    }

    private static byte[] BuildStartBooth(int count, string name = "Boutique", byte type = 1)
    {
        var length = BoothPackets.StartBoothMinLength + BoothPackets.StartBoothItemSize * count;
        var packet = new byte[length];
        BinaryPrimitives.WriteUInt32LittleEndian(packet.AsSpan(0, 4), (uint)length);
        BinaryPrimitives.WriteUInt16LittleEndian(packet.AsSpan(4, 2),
            (ushort)GamePackets.TM_CS_START_BOOTH);
        Encoding.ASCII.GetBytes(name).CopyTo(packet, BoothPackets.NameOffset);
        packet[BoothPackets.TypeOffset] = type;
        BinaryPrimitives.WriteUInt16LittleEndian(packet.AsSpan(BoothPackets.CountOffset, 2), (ushort)count);

        for (var i = 0; i < count; i++)
        {
            var record = packet.AsSpan(BoothPackets.ItemsOffset + i * BoothPackets.StartBoothItemSize,
                BoothPackets.StartBoothItemSize);
            BinaryPrimitives.WriteUInt32LittleEndian(record.Slice(0, 4), 0x80000000u + (uint)i);
            BinaryPrimitives.WriteInt32LittleEndian(record.Slice(4, 4), i + 1);
            BinaryPrimitives.WriteInt64LittleEndian(record.Slice(8, 8), 1000L * (i + 1));
        }

        return packet;
    }
}
