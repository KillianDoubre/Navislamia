using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Text;
using Navislamia.Game.Network.Packets.Enums;

namespace Navislamia.Game.Network.Packets.Game;

/// <summary>
/// The creature farm (ferme de créatures) socle, <c>6000</c>-<c>6008</c>. All nine ids are
/// <c>X(&lt;id&gt;, true)</c> in rzu under a <c>// Since EPIC_7_3</c> marker, so 7.3 keeps the plain ids and no
/// field of the family is version gated. The 7.3 client routes exactly the four server to client ids
/// <c>6001</c>, <c>6003</c>, <c>6005</c>, <c>6007</c> and drops the five client to server ones as
/// "message non traité", which is an independent confirmation of the direction of each frame.
///
/// Only the frames this lot needs are modelled. The server reads <c>6000</c>, <c>6002</c>, <c>6004</c>,
/// <c>6006</c> and <c>6008</c> and emits one answer, <c>6001</c>; the three result frames
/// <c>6003</c>/<c>6005</c>/<c>6007</c> carry a <c>result</c> byte whose values no reference establishes and
/// are therefore neither declared nor emitted (see docs/packet-specs/socle-ferme-creatures.md §5.2, §7.5).
///
/// Nothing here decides when a farm fills, what a ticket costs or how long a creature stays: the server has
/// no farm storage at all (§7.1), so the only content this file can produce is an empty farm.
/// </summary>
public static class GameFarmPackets
{
    private const int HeaderSize = 7;

    /// <summary>
    /// Total size of <c>TM_CS_REQUEST_FARM_INFO</c> (6000) and <c>TM_CS_REQUEST_FARM_MARKET</c> (6008): the
    /// 7-byte header and no payload. The 7.3 client writes the length 7 in hard in both frame builders.
    /// </summary>
    public const int EmptyLength = HeaderSize;

    /// <summary>
    /// Total size of <c>TM_CS_RETRIEVE_CREATURE</c> (6004) and <c>TM_CS_NURSE_CREATURE</c> (6006): the header
    /// plus the single <c>creature_card_handle</c> the client writes at offset 7.
    /// </summary>
    public const int CreatureCardHandleLength = HeaderSize + 4;

    /// <summary>
    /// Offset of <c>summons</c> in <c>TM_SC_FARM_INFO</c> (6001) — the <c>int8</c> entry counter, and the
    /// first byte after the header.
    /// </summary>
    public const int SummonsOffset = HeaderSize;

    /// <summary>
    /// Start of the entry area of <c>TM_SC_FARM_INFO</c> (6001): the counter occupies one byte, and the client
    /// copies <c>summons x 120</c> bytes from there (SFrame.exe 0x6721e3-0x6721fa). There is no padding between
    /// the counter and the first entry.
    /// </summary>
    public const int InfoHeaderSize = HeaderSize + 1;

    /// <summary>
    /// Size of one <c>TS_FARM_SUMMON_INFO</c> entry: 120 bytes, the sum of its nine 7.3 fields (45) and the
    /// 75-byte <c>card_info</c>. The client's own allocation confirms the stride (<c>summons x 0x78</c>).
    /// </summary>
    public const int SummonEntrySize = 120;

    /// <summary>Width of the entry's <c>name</c> field in 7.3: 19 bytes, 18 characters plus the NUL.</summary>
    public const int SummonNameLength = 19;

    /// <summary>Characters <c>name</c> may hold before the mandatory NUL: 18.</summary>
    public const int SummonNameMaxLength = SummonNameLength - 1;

    /// <summary>
    /// Protocol bound on <c>summons</c>, not a content choice: rzu serialises the counter as an
    /// <c>int8_t</c> clamped to 127 (<c>PacketDeclaration.h:83-88</c>), which caps the frame at
    /// <c>8 + 120 x 127 = 15 248</c> bytes, under the 16 KiB limit. The client reads the byte as a signed
    /// value, so a larger counter would wrap and a negative one would cancel its allocation.
    /// </summary>
    public const int MaxSummons = 127;

    /// <summary>Offset of <c>creature_card_handle</c> in <c>TM_CS_FOSTER_CREATURE</c> (6002).</summary>
    public const int FosterCardHandleOffset = HeaderSize;

    /// <summary>Offset of the <c>ticket_info</c> entry counter in <c>TM_CS_FOSTER_CREATURE</c> (6002).</summary>
    public const int FosterTicketCountOffset = HeaderSize + 4;

    /// <summary>Offset of the <c>cracker_info</c> entry counter in <c>TM_CS_FOSTER_CREATURE</c> (6002).</summary>
    public const int FosterCrackerCountOffset = HeaderSize + 8;

    /// <summary>
    /// Start of the two chained arrays of <c>TM_CS_FOSTER_CREATURE</c> (6002): the header, the card handle and
    /// both counters, i.e. 19 bytes. rzu serialises <c>ticket_info</c> and then <c>cracker_info</c> right after
    /// the two counters, and every entry of either array is 8 bytes.
    /// </summary>
    public const int FosterArraysOffset = HeaderSize + 12;

    /// <summary>Size of one <c>ticket_info</c> entry of 6002, and of one <c>cracker_info</c> entry: 8 bytes.</summary>
    public const int FosterStackEntrySize = 8;

    /// <summary>
    /// One entry of the <c>ticket_info</c> array of 6002: the handle of the stack the client offers and how
    /// many of it. Which item resource is a ticket is not established (§7.6), so the values are carried
    /// verbatim and never interpreted.
    /// </summary>
    public readonly record struct FosterTicket(uint TicketHandle, int TicketCount);

    /// <summary>
    /// One entry of the <c>cracker_info</c> array of 6002. The 7.3 client sends 0 or 1 entry and its counter
    /// is <c>sete</c>, so the array is optional rather than mandatory.
    /// </summary>
    public readonly record struct FosterCracker(uint CrackerHandle, int CrackerCount);

    /// <summary>
    /// <c>TM_CS_FOSTER_CREATURE</c> (6002): the card the player wants to leave in the farm
    /// (<c>creature_card_handle</c>, at offset 7) plus the two stacks it consumes. The 7.3 client always sends
    /// one ticket and zero or one cracker, which makes its own frames 27 or 35 bytes; the reader accepts any
    /// <c>T</c>/<c>C</c> a well formed frame declares, because the values are the server's business to refuse,
    /// not the parser's.
    /// </summary>
    public readonly record struct FosterCreatureRequest(uint CreatureCardHandle, FosterTicket[] Tickets,
        FosterCracker[] Crackers);

    /// <summary>
    /// One <c>TS_FARM_SUMMON_INFO</c> entry of <c>TM_SC_FARM_INFO</c> (6001), 120 bytes on the wire.
    ///
    /// <c>Index</c> (<c>int32</c> at +0) has no established domain: the client copies the whole entry with a
    /// <c>memcpy</c> and never reads the field back (§7.2). <c>Duration</c>, <c>ElaspedTime</c> (the reference's
    /// own spelling) and <c>RefreshTime</c> have no established unit or origin (§7.3), <c>Exp</c> no
    /// established model (§7.4), and <c>UsingCash</c>/<c>UsingCracker</c> no established meaning (§7.6). All of
    /// them are therefore supplied by the caller and written verbatim; this type invents no default.
    ///
    /// <c>CardInfo</c> is the 75-byte base item motif of the entry. The sheet names
    /// <see cref="ItemFixedInfoWriter.FromItem"/> as the way an <c>ItemEntity</c> becomes that motif, but
    /// which object a <c>card_info</c> describes — the player's card or a catalogue row — is not established
    /// (§7.9), so the writer takes the motif from its caller instead of choosing the object itself.
    /// </summary>
    public readonly record struct FarmSummonInfo(
        int Index,
        long Exp,
        string Name,
        int Duration,
        int ElaspedTime,
        int RefreshTime,
        byte UsingCash,
        byte UsingCracker,
        ItemFixedInfo CardInfo);

    /// <summary>
    /// Total size of a <c>TM_SC_FARM_INFO</c> (6001) carrying <paramref name="summons"/> entries:
    /// <c>8 + 120 x summons</c>, i.e. 8 bytes for an empty farm.
    /// </summary>
    public static int GetFarmInfoSize(int summons) => InfoHeaderSize + SummonEntrySize * summons;

    /// <summary>Absolute offset of entry <paramref name="index"/> in a 6001 frame.</summary>
    public static int GetSummonEntryOffset(int index) => InfoHeaderSize + SummonEntrySize * index;

    /// <summary>
    /// Total size of a <c>TM_CS_FOSTER_CREATURE</c> (6002) declaring <paramref name="tickets"/> tickets and
    /// <paramref name="crackers"/> crackers: <c>19 + 8 x T + 8 x C</c>. The 7.3 client's own frames are 27
    /// (1 ticket, 0 cracker) and 35 (1 ticket, 1 cracker) bytes.
    /// </summary>
    public static int GetFosterSize(int tickets, int crackers) =>
        FosterArraysOffset + FosterStackEntrySize * tickets + FosterStackEntrySize * crackers;

    /// <summary>
    /// <c>TM_CS_REQUEST_FARM_INFO</c> (6000) and <c>TM_CS_REQUEST_FARM_MARKET</c> (6008) carry no payload: the
    /// 7.3 client writes exactly 7 bytes for both. Any other length is a malformed frame, not a shorter or
    /// richer request.
    /// </summary>
    public static bool HasNoPayload(ReadOnlySpan<byte> packet)
    {
        return packet.Length == EmptyLength;
    }

    /// <summary>
    /// Reads <c>TM_CS_RETRIEVE_CREATURE</c> (6004), the "regain" button: the handle of the card the player
    /// takes back out of the farm. Only the exact 11-byte form is accepted, as the field is not optional.
    /// </summary>
    public static bool TryReadRetrieveCreature(ReadOnlySpan<byte> packet, out uint creatureCardHandle)
    {
        return TryReadCreatureCardHandle(packet, out creatureCardHandle);
    }

    /// <summary>
    /// Reads <c>TM_CS_NURSE_CREATURE</c> (6006), the "ministration" button (one per farm slot in the client):
    /// the same 11-byte frame as 6004, carrying the handle of the card being ministered.
    /// </summary>
    public static bool TryReadNurseCreature(ReadOnlySpan<byte> packet, out uint creatureCardHandle)
    {
        return TryReadCreatureCardHandle(packet, out creatureCardHandle);
    }

    private static bool TryReadCreatureCardHandle(ReadOnlySpan<byte> packet, out uint creatureCardHandle)
    {
        if (packet.Length != CreatureCardHandleLength)
        {
            creatureCardHandle = 0;
            return false;
        }

        creatureCardHandle = BinaryPrimitives.ReadUInt32LittleEndian(packet.Slice(HeaderSize, 4));
        return true;
    }

    /// <summary>
    /// Reads <c>TM_CS_FOSTER_CREATURE</c> (6002): <c>creature_card_handle</c> at 7, the two <c>int32</c>
    /// counters at 11 and 15, then the tickets and the crackers, 8 bytes each, chained.
    ///
    /// A frame is accepted only when its length is exactly <c>19 + 8T + 8C</c>: the client writes that length
    /// in hard, so a padded or truncated frame is malformed rather than a request with a different count. A
    /// negative counter is refused too — the reference reads both as <c>int32</c> and a negative one would make
    /// the entry count meaningless. The declared size is computed in 64 bits before any allocation, so a huge
    /// counter cannot overflow into a plausible length.
    /// </summary>
    public static bool TryReadFosterCreature(ReadOnlySpan<byte> packet, out FosterCreatureRequest request)
    {
        request = default;
        if (packet.Length < FosterArraysOffset)
        {
            return false;
        }

        var tickets = BinaryPrimitives.ReadInt32LittleEndian(packet.Slice(FosterTicketCountOffset, 4));
        var crackers = BinaryPrimitives.ReadInt32LittleEndian(packet.Slice(FosterCrackerCountOffset, 4));
        if (tickets < 0 || crackers < 0)
        {
            return false;
        }

        var declaredLength = FosterArraysOffset + (long)FosterStackEntrySize * tickets +
            (long)FosterStackEntrySize * crackers;
        if (declaredLength != packet.Length)
        {
            return false;
        }

        var creatureCardHandle = BinaryPrimitives.ReadUInt32LittleEndian(packet.Slice(FosterCardHandleOffset, 4));

        var offset = FosterArraysOffset;
        var ticketEntries = new FosterTicket[tickets];
        for (var i = 0; i < tickets; i++, offset += FosterStackEntrySize)
        {
            ticketEntries[i] = new FosterTicket(
                BinaryPrimitives.ReadUInt32LittleEndian(packet.Slice(offset, 4)),
                BinaryPrimitives.ReadInt32LittleEndian(packet.Slice(offset + 4, 4)));
        }

        var crackerEntries = new FosterCracker[crackers];
        for (var i = 0; i < crackers; i++, offset += FosterStackEntrySize)
        {
            crackerEntries[i] = new FosterCracker(
                BinaryPrimitives.ReadUInt32LittleEndian(packet.Slice(offset, 4)),
                BinaryPrimitives.ReadInt32LittleEndian(packet.Slice(offset + 4, 4)));
        }

        request = new FosterCreatureRequest(creatureCardHandle, ticketEntries, crackerEntries);
        return true;
    }

    /// <summary>
    /// The only 6001 frame the server can produce today: <c>summons = 0</c>, 8 bytes. NavisLamia has no farm
    /// storage, so no entry could be filled with anything but invented values; the 7.3 client handles the empty
    /// farm cleanly (a zero counter skips the allocation, SFrame.exe 0x67219c-0x67219e). The frame is
    /// reversible the day a farm exists — no threshold, cost or duration is baked into it.
    /// </summary>
    public static byte[] BuildEmptyFarmInfo()
    {
        return BuildFarmInfo(Array.Empty<FarmSummonInfo>());
    }

    /// <summary>
    /// <c>TM_SC_FARM_INFO</c> (6001): <c>int8 summons</c> at 7, then <c>summons</c> entries of 120 bytes. The
    /// counter always equals the number of entries actually written, because the client loops on it and never
    /// reads the length field. More than <see cref="MaxSummons"/> entries would not fit the <c>int8</c> counter,
    /// so the list is capped there.
    /// </summary>
    public static byte[] BuildFarmInfo(IReadOnlyList<FarmSummonInfo> summons)
    {
        var count = summons?.Count ?? 0;
        if (count > MaxSummons)
        {
            count = MaxSummons;
        }

        var packet = new byte[GetFarmInfoSize(count)];
        BinaryPrimitives.WriteUInt32LittleEndian(packet.AsSpan(0, 4), (uint)packet.Length);
        BinaryPrimitives.WriteUInt16LittleEndian(packet.AsSpan(4, 2),
            (ushort)GamePackets.TM_SC_FARM_INFO);

        packet[SummonsOffset] = unchecked((byte)count);

        for (var i = 0; i < count; i++)
        {
            WriteSummonEntry(packet.AsSpan(GetSummonEntryOffset(i), SummonEntrySize), summons![i]);
        }

        WriteChecksum(packet);
        return packet;
    }

    /// <summary>
    /// Writes one 120-byte entry: <c>index</c> +0, <c>exp</c> +4, <c>name</c> +12 (19 bytes),
    /// <c>duration</c> +31, <c>elasped_time</c> +35, <c>refresh_time</c> +39, <c>using_cash</c> +43,
    /// <c>using_cracker</c> +44 and the 75-byte <c>card_info</c> +45.
    /// </summary>
    private static void WriteSummonEntry(Span<byte> span, in FarmSummonInfo summon)
    {
        BinaryPrimitives.WriteInt32LittleEndian(span.Slice(0, 4), summon.Index);
        BinaryPrimitives.WriteInt64LittleEndian(span.Slice(4, 8), summon.Exp);
        WriteName(span.Slice(12, SummonNameLength), summon.Name);
        BinaryPrimitives.WriteInt32LittleEndian(span.Slice(31, 4), summon.Duration);
        BinaryPrimitives.WriteInt32LittleEndian(span.Slice(35, 4), summon.ElaspedTime);
        BinaryPrimitives.WriteInt32LittleEndian(span.Slice(39, 4), summon.RefreshTime);
        span[43] = summon.UsingCash;
        span[44] = summon.UsingCracker;

        ItemFixedInfoWriter.Write(span.Slice(45, ItemFixedInfoWriter.Size), summon.CardInfo);
    }

    /// <summary>
    /// The entry name is a fixed 19-byte character array: ASCII, truncated to 18 characters and NUL padded, so
    /// the byte that follows (the <c>duration</c>) is never part of the name. Same writer as the summon name of
    /// <see cref="GameSummonPackets"/>.
    /// </summary>
    private static void WriteName(Span<byte> target, string name)
    {
        target.Clear();

        if (string.IsNullOrEmpty(name))
        {
            return;
        }

        var length = Math.Min(name.Length, SummonNameMaxLength);
        Encoding.ASCII.GetBytes(name.AsSpan(0, length), target.Slice(0, length));
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
