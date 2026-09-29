using System;
using System.Collections.Generic;
using Navislamia.Game.Network.Packets;
using Navislamia.Game.Network.Packets.Enums;
using Navislamia.Game.Network.Packets.Game;

namespace Navislamia.Game.Services;

/// <summary>
/// The game rules of the player booth socle (docs/packet-specs/socle-booths.md §5.3). The 7.3 client
/// states them itself in <c>db_string.rdb</c>: <c>smsg_booth_cant_setup</c> ("must be Lv 10 or higher"),
/// <c>smsg_booth_cant_setup_item</c> ("at least 1 item for sale"), <c>smsg_booth_name_short</c> /
/// <c>smsg_booth_name_long</c> ("at least 6" / "no more than 40 characters"). The refusal code is the
/// one both sides already declare, <see cref="ResultCode.NotActableWhileUsingBooth"/> (55): it is
/// reused rather than invented, and the texts prove the rule, never a code.
/// </summary>
public static class BoothRules
{
    public const int MinCharacterLevel = 10;
    public const int MinBoothNameLength = 6;
    public const int MaxBoothNameLength = 40;

    /// <summary>
    /// The actions the client itself announces as refused while its booth is open —
    /// <c>smsg_booth_not_use_item</c>, <c>smsg_booth_not_use_skill</c>, <c>smsg_booth_not_action</c>.
    /// The list is bounded to the actions the receive loop already handles.
    /// <c>smsg_booth_not_use_store</c> ("another store") found its object with the visibility socle:
    /// <c>TM_CS_WATCH_BOOTH</c> (702) is now handled and is guarded here. <c>TM_CS_STOP_WATCH_BOOTH</c>
    /// (704) stays out, exactly like <c>TM_CS_STOP_BOOTH</c> (701): closing is the way out of the lock
    /// (docs/packet-specs/socle-booths-visibilite.md §5.2 point 9).
    /// </summary>
    private static readonly HashSet<ushort> GuardedActionIds = new()
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

    /// <summary>Whether a booth lock covers <paramref name="packetId"/> at all.</summary>
    public static bool IsGuardedAction(ushort packetId)
    {
        return GuardedActionIds.Contains(packetId);
    }

    /// <summary>
    /// The single gate the receive loop applies in front of its dispatch chain: while a booth is open,
    /// a guarded action is answered with <see cref="ResultCode.NotActableWhileUsingBooth"/> (55) and
    /// nothing else runs. Every other packet — including <c>TM_CS_START_BOOTH</c> and
    /// <c>TM_CS_STOP_BOOTH</c> (701), which is the way out of the lock — returns
    /// <see cref="ResultCode.Success"/> and keeps its normal path.
    /// </summary>
    public static ResultCode GateAction(bool isBoothOpen, ushort packetId)
    {
        return isBoothOpen && GuardedActionIds.Contains(packetId)
            ? ResultCode.NotActableWhileUsingBooth
            : ResultCode.Success;
    }

    /// <summary>
    /// Judges a received <c>TM_CS_START_BOOTH</c>: the wire reader first, then the rules that need a
    /// value or the character. Answers the code the client must receive, so the handler stays a
    /// one-line send. A frame of zero item is refused here even though the client can build it
    /// (docs/packet-specs/socle-booths.md §3.2): the rule is "at least one item", not a wire limit.
    /// </summary>
    public static bool TryAcceptStartBooth(ReadOnlySpan<byte> packet, int characterLevel,
        out StartBoothRequest request, out ResultCode result)
    {
        if (!BoothPackets.TryReadStartBooth(packet, out request, out result))
        {
            return false;
        }

        result = ValidateStartBooth(request, characterLevel);
        return result == ResultCode.Success;
    }

    /// <summary>
    /// The field and level rules of docs/packet-specs/socle-booths.md §5.3 point 2. The name is counted
    /// in bytes, the way the client copied them: no encoding is interpreted (fiche §7.9). The client
    /// proves the <c>type</c> domain itself (<c>setne al</c> / <c>inc al</c> gives 1 or 2 and nothing
    /// else) — which of the two means "selling" stays open (fiche §7.2), so both are kept as they are.
    /// </summary>
    public static ResultCode ValidateStartBooth(StartBoothRequest request, int characterLevel)
    {
        if (request.Type is not (1 or 2))
        {
            return ResultCode.InvalidArgument;
        }

        if (request.Items.Length == 0)
        {
            return ResultCode.InvalidArgument;
        }

        if (request.Name.Length < MinBoothNameLength || request.Name.Length > MaxBoothNameLength)
        {
            return ResultCode.InvalidArgument;
        }

        return characterLevel < MinCharacterLevel ? ResultCode.NotEnoughLevel : ResultCode.Success;
    }
}
