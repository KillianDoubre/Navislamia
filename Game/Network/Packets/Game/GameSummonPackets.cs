using System;
using System.Buffers.Binary;
using System.Text;
using Navislamia.Game.DataAccess.Entities.Telecaster;
using Navislamia.Game.Network.Packets.Enums;

namespace Navislamia.Game.Network.Packets.Game;

/// <summary>
/// The Epic 7.3 summon packets the server emits (S→C), i.e. the part of the summon socle whose wire
/// layout is fully determined by its references. Every size, field order and version decision comes
/// from <c>docs/packet-specs/socle-invocations.md</c> — read it before changing anything here.
/// </summary>
/// <remarks>
/// Nothing in this class decides <em>when</em> a packet leaves the server: a summon existing in the
/// world, its lifetime, its evolution and its mounting are gameplay policies that are not settled yet.
/// The layouts below are pure encoders, so they carry no threshold, no cost and no default of their own.
/// </remarks>
public static class GameSummonPackets
{
    private const int HeaderSize = 7;

    /// <summary>
    /// Fixed <c>name</c> buffer. rzu declares <c>_(def)(string)(name, 20)</c> with an
    /// <c>_(impl)(string)(name, 19, version &lt; EPIC_9_6)</c> override, so Epic 7.3 carries 19 bytes:
    /// 18 usable characters plus the nul terminator. <c>MessageBuffer::writeString</c> copies a fixed
    /// buffer of <c>maxSize</c> bytes, truncates the value at <c>maxSize - 1</c> and zero-fills the rest.
    /// </summary>
    public const int NameSize = 19;

    /// <summary>
    /// <c>TS_SC_ADD_SUMMON_INFO</c> (301). 39 payload bytes, 46 with the header.
    /// <c>card_handle</c> @0, <c>summon_handle</c> @4, <c>name</c> @8 (19), <c>code</c> @27,
    /// <c>level</c> @31, <c>sp</c> @35 — every one of them <c>int32_t</c>/<c>ar_handle_t</c> in 7.3,
    /// no version gate but the id and the string width (<c>TS_SC_ADD_SUMMON_INFO.h:7-19</c>).
    /// </summary>
    /// <param name="code">
    /// Caller-supplied on purpose: the source of this field is not established
    /// (fiche <c>NON ÉTABLI</c> 4). Do not substitute a guess.
    /// </param>
    public static byte[] BuildAddSummonInfo(uint cardHandle, uint summonHandle, string name, int code,
        int level, int sp)
    {
        var packet = CreatePacket(GamePackets.TM_SC_ADD_SUMMON_INFO, HeaderSize + 39);
        var span = packet.AsSpan();

        BinaryPrimitives.WriteUInt32LittleEndian(span.Slice(HeaderSize, 4), cardHandle);
        BinaryPrimitives.WriteUInt32LittleEndian(span.Slice(HeaderSize + 4, 4), summonHandle);
        WriteName(span.Slice(HeaderSize + 8, NameSize), name);
        BinaryPrimitives.WriteInt32LittleEndian(span.Slice(HeaderSize + 27, 4), code);
        BinaryPrimitives.WriteInt32LittleEndian(span.Slice(HeaderSize + 31, 4), level);
        BinaryPrimitives.WriteInt32LittleEndian(span.Slice(HeaderSize + 35, 4), sp);

        WriteChecksum(packet);
        return packet;
    }

    /// <summary>
    /// <c>TS_SC_ADD_SUMMON_INFO</c> (301) for a registered summon. Field provenance:
    /// <c>card_handle</c> ← <see cref="SummonEntity.CardItemId"/>, <c>name</c> ← <see cref="SummonEntity.Name"/>,
    /// <c>level</c> ← <see cref="SummonEntity.Lv"/>, <c>sp</c> ← <see cref="SummonEntity.Sp"/>.
    /// </summary>
    /// <param name="summonHandle">
    /// Caller-supplied: a handle identifies a summon inside the world, and no summon enters the world yet
    /// (fiche <c>NON ÉTABLI</c> 8).
    /// </param>
    /// <param name="code">
    /// Caller-supplied: the source of this field is not established
    /// (fiche <c>NON ÉTABLI</c> 4). Do not substitute a guess.
    /// </param>
    public static byte[] BuildAddSummonInfo(SummonEntity summon, uint summonHandle, int code)
    {
        return BuildAddSummonInfo((uint)summon.CardItemId, summonHandle, summon.Name, code, summon.Lv,
            summon.Sp);
    }

    /// <summary>
    /// <c>TS_SC_REMOVE_SUMMON_INFO</c> (302). 4 payload bytes, 11 with the header:
    /// <c>card_handle</c> @0 (<c>TS_SC_REMOVE_SUMMON_INFO.h:7-8</c>).
    /// </summary>
    public static byte[] BuildRemoveSummonInfo(uint cardHandle)
    {
        var packet = CreatePacket(GamePackets.TM_SC_REMOVE_SUMMON_INFO, HeaderSize + 4);
        BinaryPrimitives.WriteUInt32LittleEndian(packet.AsSpan(HeaderSize, 4), cardHandle);
        WriteChecksum(packet);
        return packet;
    }

    /// <summary>
    /// <c>TS_SC_UNSUMMON</c> (305). 4 payload bytes, 11 with the header:
    /// <c>summon_handle</c> @0 (<c>TS_SC_UNSUMMON.h:7-8</c>).
    /// </summary>
    public static byte[] BuildUnsummon(uint summonHandle)
    {
        var packet = CreatePacket(GamePackets.TM_SC_UNSUMMON, HeaderSize + 4);
        BinaryPrimitives.WriteUInt32LittleEndian(packet.AsSpan(HeaderSize, 4), summonHandle);
        WriteChecksum(packet);
        return packet;
    }

    /// <summary>
    /// <c>TS_SC_UNSUMMON_NOTICE</c> (306). 8 payload bytes, 15 with the header:
    /// <c>summon_handle</c> @0, <c>unsummon_duration</c> @4 as <c>ar_time_t</c> — a <c>uint32_t</c>
    /// count of 10 ms ticks (<c>TS_SC_UNSUMMON_NOTICE.h:5-7</c>, <c>ServerClock.TicksPerSecond</c>).
    /// The duration itself is a gameplay policy: this builder only carries the caller's value.
    /// </summary>
    public static byte[] BuildUnsummonNotice(uint summonHandle, uint unsummonDurationTicks)
    {
        var packet = CreatePacket(GamePackets.TM_SC_UNSUMMON_NOTICE, HeaderSize + 8);
        var span = packet.AsSpan();

        BinaryPrimitives.WriteUInt32LittleEndian(span.Slice(HeaderSize, 4), summonHandle);
        BinaryPrimitives.WriteUInt32LittleEndian(span.Slice(HeaderSize + 4, 4), unsummonDurationTicks);

        WriteChecksum(packet);
        return packet;
    }

    /// <summary>
    /// <c>TS_SC_SUMMON_EVOLUTION</c> (307). 31 payload bytes, 38 with the header:
    /// <c>card_handle</c> @0, <c>summon_handle</c> @4, <c>name</c> @8 (19), <c>code</c> @27
    /// (<c>TS_SC_SUMMON_EVOLUTION.h:5-15</c>). Whether an evolution happens, and against which
    /// <c>SummonResourceEntity.EvolveTarget</c>, is not decided here.
    /// </summary>
    /// <param name="code">
    /// Caller-supplied: <c>code</c> shares the unresolved source of the one in packet 301.
    /// </param>
    public static byte[] BuildSummonEvolution(uint cardHandle, uint summonHandle, string name, int code)
    {
        var packet = CreatePacket(GamePackets.TM_SC_SUMMON_EVOLUTION, HeaderSize + 31);
        var span = packet.AsSpan();

        BinaryPrimitives.WriteUInt32LittleEndian(span.Slice(HeaderSize, 4), cardHandle);
        BinaryPrimitives.WriteUInt32LittleEndian(span.Slice(HeaderSize + 4, 4), summonHandle);
        WriteName(span.Slice(HeaderSize + 8, NameSize), name);
        BinaryPrimitives.WriteInt32LittleEndian(span.Slice(HeaderSize + 27, 4), code);

        WriteChecksum(packet);
        return packet;
    }

    /// <summary>
    /// <c>TS_SC_MOUNT_SUMMON</c> (320). 17 payload bytes, 24 with the header: <c>handle</c> @0,
    /// <c>summon_handle</c> @4, <c>x</c> @8 (float), <c>y</c> @12 (float), <c>success</c> @16 (1 byte —
    /// <c>bool</c> is a primitive and serializes as <c>sizeof(bool)</c>, <c>TS_SC_MOUNT_SUMMON.h:5-10</c>).
    /// </summary>
    public static byte[] BuildMountSummon(uint handle, uint summonHandle, float x, float y, bool success)
    {
        var packet = CreatePacket(GamePackets.TM_SC_MOUNT_SUMMON, HeaderSize + 17);
        var span = packet.AsSpan();

        BinaryPrimitives.WriteUInt32LittleEndian(span.Slice(HeaderSize, 4), handle);
        BinaryPrimitives.WriteUInt32LittleEndian(span.Slice(HeaderSize + 4, 4), summonHandle);
        BinaryPrimitives.WriteSingleLittleEndian(span.Slice(HeaderSize + 8, 4), x);
        BinaryPrimitives.WriteSingleLittleEndian(span.Slice(HeaderSize + 12, 4), y);
        span[HeaderSize + 16] = success ? (byte)1 : (byte)0;

        WriteChecksum(packet);
        return packet;
    }

    /// <summary>
    /// <c>TS_SC_UNMOUNT_SUMMON</c> (321). 9 payload bytes, 16 with the header: <c>handle</c> @0,
    /// <c>summon_handle</c> @4, <c>flag</c> @8 as <c>int8_t</c> (<c>TS_SC_UNMOUNT_SUMMON.h:5-8</c>).
    /// </summary>
    public static byte[] BuildUnmountSummon(uint handle, uint summonHandle, sbyte flag)
    {
        var packet = CreatePacket(GamePackets.TM_SC_UNMOUNT_SUMMON, HeaderSize + 9);
        var span = packet.AsSpan();

        BinaryPrimitives.WriteUInt32LittleEndian(span.Slice(HeaderSize, 4), handle);
        BinaryPrimitives.WriteUInt32LittleEndian(span.Slice(HeaderSize + 4, 4), summonHandle);
        span[HeaderSize + 8] = unchecked((byte)flag);

        WriteChecksum(packet);
        return packet;
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

    private static void WriteName(Span<byte> target, string name)
    {
        var bytes = Encoding.ASCII.GetBytes(name ?? string.Empty);
        var length = Math.Min(bytes.Length, target.Length - 1);
        bytes.AsSpan(0, length).CopyTo(target);
    }
}
