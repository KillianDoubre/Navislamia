using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using Navislamia.Game.DataAccess.Entities.Telecaster;
using Navislamia.Game.Network.Packets.Enums;

namespace Navislamia.Game.Network.Packets.Game;

/// <summary>
/// One <c>TS_QUEST_INFO</c> record: the 61-byte element of <c>TM_SC_QUEST_LIST</c> (600) and the payload
/// of <c>TM_SC_QUEST_STATUS</c> (601). The six <c>Status</c> slots are opaque in 7.3 — the client copies
/// them into its quest object without the handler revealing what they drive, and only the first three
/// exist in the NGemity model — so the server carries them through unchanged.
/// </summary>
public readonly record struct QuestListEntry(
    uint Code,
    uint StartId,
    int[] Value,
    int[] Status,
    byte Progress,
    uint TimeLimit);

/// <summary>
/// <c>TM_SC_QUEST_LIST</c> (600) and <c>TM_SC_QUEST_STATUS</c> (601), Epic 7.3.
/// Layout, sizes and gating: <c>docs/packet-specs/socle-quetes.md</c> §3.4-3.5 and §4.
/// </summary>
public static class GameQuestPackets
{
    private const int HeaderSize = 7;

    /// <summary>The two counters that open the frame: 2 bytes each, then the active table.</summary>
    public const int CountsSize = 4;

    /// <summary><c>TS_QUEST_INFO</c> is exactly 61 bytes (<c>0x3d</c>, the client's loop step).</summary>
    public const int QuestInfoSize = 61;

    /// <summary>Six <c>uint32</c> slots on the wire, twice (<c>value[6]</c> and <c>status[6]</c>).</summary>
    public const int StatusSlots = 6;

    /// <summary>Fixed size of <c>TM_SC_QUEST_STATUS</c> (601): 7 + 4 + 24 + 1 + 4.</summary>
    public const int QuestStatusSize = HeaderSize + 4 + StatusSlots * 4 + 1 + 4;

    /// <summary>
    /// Builds <c>TM_SC_QUEST_LIST</c> (600): <c>11 + 61·N</c> bytes. The frame carries two counters and
    /// the client reads its table at offset 11, so the empty form is a perfectly formed 11-byte frame.
    /// <para>
    /// <c>pendingQuests</c> is written as <c>0</c> and no table follows: the count is proven to exist
    /// (the client's table starts at <c>frame+0xb</c>) but the 8-byte entries themselves are never
    /// walked by the 7.3 handler and their content is <c>NON ÉTABLI</c>
    /// (<c>docs/packet-specs/socle-quetes.md</c> §6 écart 10, §8.7) — the socle therefore emits a zero
    /// count rather than entries it cannot justify.
    /// </para>
    /// </summary>
    public static byte[] BuildQuestList(IReadOnlyList<QuestListEntry> activeQuests)
    {
        var count = activeQuests?.Count ?? 0;
        var total = HeaderSize + CountsSize + count * QuestInfoSize;
        var packet = CreatePacket(GamePackets.TM_SC_QUEST_LIST, total);

        BinaryPrimitives.WriteUInt16LittleEndian(packet.AsSpan(HeaderSize, 2), (ushort)count);
        BinaryPrimitives.WriteUInt16LittleEndian(packet.AsSpan(HeaderSize + 2, 2), 0);

        for (var i = 0; i < count; i++)
        {
            WriteQuestInfo(packet.AsSpan(HeaderSize + CountsSize + i * QuestInfoSize, QuestInfoSize),
                activeQuests[i]);
        }

        WriteChecksum(packet);
        return packet;
    }

    /// <summary>
    /// Builds 600 from the stored character quests. The wire fields are <c>uint32</c>; the entity keeps
    /// the same bits in signed columns, so the slots are reinterpreted, never clamped.
    /// </summary>
    public static byte[] BuildQuestList(IReadOnlyList<CharacterQuestEntity> activeQuests)
    {
        if (activeQuests is null || activeQuests.Count == 0)
        {
            return BuildQuestList(Array.Empty<QuestListEntry>());
        }

        var entries = new QuestListEntry[activeQuests.Count];
        for (var i = 0; i < activeQuests.Count; i++)
        {
            entries[i] = ToQuestListEntry(activeQuests[i]);
        }

        return BuildQuestList(entries);
    }

    public static QuestListEntry ToQuestListEntry(CharacterQuestEntity quest)
    {
        return new QuestListEntry(
            unchecked((uint)quest.Code),
            unchecked((uint)quest.StartId),
            quest.Value,
            quest.Status,
            quest.Progress,
            unchecked((uint)quest.TimeLimit));
    }

    /// <summary>
    /// Builds <c>TM_SC_QUEST_STATUS</c> (601): a fixed 40-byte frame — <c>code</c> (<c>int32</c> at 7),
    /// the six 32-bit <c>status</c> slots at 11, <c>nProgress</c> (<c>int8</c>) at 35 and
    /// <c>nTimeLimit</c> (<c>uint32</c>) at 36. The 7.3 client only reads the byte at 35, but the frame
    /// keeps its full size.
    /// </summary>
    public static byte[] BuildQuestStatus(QuestListEntry quest)
    {
        var packet = CreatePacket(GamePackets.TM_SC_QUEST_STATUS, QuestStatusSize);
        var body = packet.AsSpan(HeaderSize);

        BinaryPrimitives.WriteInt32LittleEndian(body.Slice(0, 4), unchecked((int)quest.Code));
        WriteSlots(body.Slice(4, StatusSlots * 4), quest.Status, nameof(quest) + "." + nameof(quest.Status));
        body[4 + StatusSlots * 4] = quest.Progress;
        BinaryPrimitives.WriteUInt32LittleEndian(body.Slice(5 + StatusSlots * 4, 4), quest.TimeLimit);

        WriteChecksum(packet);
        return packet;
    }

    private static void WriteQuestInfo(Span<byte> element, QuestListEntry quest)
    {
        BinaryPrimitives.WriteUInt32LittleEndian(element.Slice(0, 4), quest.Code);
        BinaryPrimitives.WriteUInt32LittleEndian(element.Slice(4, 4), quest.StartId);
        WriteSlots(element.Slice(8, StatusSlots * 4), quest.Value, nameof(quest) + "." + nameof(quest.Value));
        WriteSlots(element.Slice(32, StatusSlots * 4), quest.Status, nameof(quest) + "." + nameof(quest.Status));

        // progress (u8) at +56 and timeLimit (ar_time_t = u32) at +57 close the 61-byte element.
        element[56] = quest.Progress;
        BinaryPrimitives.WriteUInt32LittleEndian(element.Slice(57, 4), quest.TimeLimit);
    }

    /// <summary>
    /// Writes exactly six 32-bit slots. A missing or short array is zero-padded — the slots the socle
    /// cannot fill keep the value the fiche reserves (<c>0</c>) — while an over-long one is refused
    /// rather than silently truncated.
    /// </summary>
    private static void WriteSlots(Span<byte> destination, int[] slots, string fieldName)
    {
        if (slots is not null && slots.Length > StatusSlots)
        {
            throw new ArgumentException($"A quest field carries at most {StatusSlots} slots, got {slots.Length}.",
                fieldName);
        }

        for (var i = 0; i < StatusSlots; i++)
        {
            var value = slots is not null && i < slots.Length ? unchecked((uint)slots[i]) : 0u;
            BinaryPrimitives.WriteUInt32LittleEndian(destination.Slice(i * 4, 4), value);
        }
    }

    private static byte[] CreatePacket(GamePackets id, int total)
    {
        var packet = new byte[total];
        BinaryPrimitives.WriteUInt32LittleEndian(packet.AsSpan(0, 4), (uint)total);
        BinaryPrimitives.WriteUInt16LittleEndian(packet.AsSpan(4, 2), (ushort)id);
        return packet;
    }

    private static void WriteChecksum(byte[] packet)
    {
        byte checksum = 0;
        for (var i = 0; i < 6; i++)
        {
            checksum += packet[i];
        }

        packet[6] = checksum;
    }
}
