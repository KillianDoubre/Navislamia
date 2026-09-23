using System;
using System.Buffers.Binary;
using System.Text;
using Navislamia.Game.DataAccess.Entities.Telecaster;
using Navislamia.Game.Network.Packets.Enums;

namespace Navislamia.Game.Network.Packets.Game;

/// <summary>
/// The Epic 7.3 summon packets whose wire layout is fully determined by its references: the part of the
/// summon socle the server emits (S→C), plus the pet rename request it receives (C→S), 354. Every size,
/// field order and version decision comes from <c>docs/packet-specs/socle-invocations.md</c> and
/// <c>docs/packet-specs/354-set-pet-name.md</c> — read them before changing anything here.
/// </summary>
/// <remarks>
/// Nothing in this class decides <em>when</em> a packet leaves the server: a summon existing in the
/// world, its lifetime, its evolution, its mounting and a pet being renamed are gameplay policies that
/// are not settled yet. The builders below are pure encoders, and the one reader only takes a frame
/// apart, so they carry no threshold, no cost and no default of their own.
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

    /// <summary>
    /// Total size of <c>TM_CS_SET_PET_NAME</c> (354) on the wire, 7-byte header included: 30 bytes. The
    /// 7.3 client writes that length in hard (<c>0x1e</c>, <c>SFrame.exe</c> 0x48c6d0) from its only frame
    /// builder, so any other length is a malformed frame rather than a shorter or padded variant, and a
    /// longer one would push the <c>name</c> past the end of the field.
    /// </summary>
    public const int SetPetNamePacketSize = HeaderSize + 4 + NameSize;

    /// <summary>
    /// Offset of the <c>handle</c> field of the 354: 7, i.e. 0 seen from the payload
    /// (<c>_(simple)(ar_handle_t, handle)</c>). A <c>ar_handle_t</c> is a strong typedef of a
    /// <c>uint32_t</c>: it changes no width and the client reads and writes 4 bytes.
    /// </summary>
    public const int SetPetNameHandleOffset = HeaderSize;

    /// <summary>
    /// Offset of the <c>name</c> field of the 354: 11, i.e. 4 seen from the payload, right after the
    /// 4-byte handle.
    /// </summary>
    public const int SetPetNameNameOffset = SetPetNameHandleOffset + 4;

    /// <summary>
    /// Usable characters of the <c>name</c> field when the client terminates it: 18, i.e.
    /// <see cref="NameSize"/> minus the NUL. This is the width the protocol reserves, <b>not</b> a rule
    /// the server enforces: no minimum length, no allowed character set and no uniqueness is established
    /// for a pet name (fiche §7.4), so nothing here rejects, truncates or normalises a name.
    /// </summary>
    public const int SetPetNameMaxLength = NameSize - 1;

    /// <summary>
    /// <c>TM_CS_SET_PET_NAME</c> (354), the pet (familier) rename request: the 7-byte header, then a
    /// <c>handle</c> at offsets 7-10 and a <c>char[19] name</c> at offsets 11-29 — 30 bytes in all.
    /// <para>
    /// The <c>handle</c> is an <b>echo</b>: the server chose it when it sent
    /// <c>TM_SC_SHOW_SET_PET_NAME</c> (353), the client copied it into its box request and the frame
    /// carries it straight back (<c>SFrame.exe</c> 0x66f751 → 0x63c4c7 → 0x48e184). It is returned as it
    /// stands: which object it designates, and therefore how to resolve it, is not established (fiche
    /// §7.2), so no lookup happens here.
    /// </para>
    /// <para>
    /// Only the exact 30-byte form is accepted. The <c>name</c> is read <b>bounded to its field</b>: the
    /// bytes before the first NUL, or all 19 bytes of the field when the client sent none. Unlike 323 —
    /// whose frame builder zeroes the field and forces a NUL into its last byte — the client's 354 sender
    /// is a raw <c>memcpy</c> of 19 bytes out of a <c>std::string</c> (0x48e19c), so a name longer than
    /// the field arrives with no NUL at all; refusing such a frame would refuse a name the client really
    /// typed, and reading past the field would spill the bytes that follow. What follows the first NUL
    /// inside the field is ignored.
    /// </para>
    /// <para>
    /// The value crosses the server <strong>unapplied</strong>: this reader decides nothing about which
    /// pet is renamed, what a name may contain, whether it is unique, where it is persisted or what it
    /// costs (fiche §5.2, §7.3-4). See docs/packet-specs/354-set-pet-name.md.
    /// </para>
    /// </summary>
    public static bool TryReadSetPetName(ReadOnlySpan<byte> packet, out uint handle, out string name)
    {
        handle = 0;
        name = null;

        if (packet.Length != SetPetNamePacketSize)
        {
            return false;
        }

        handle = BinaryPrimitives.ReadUInt32LittleEndian(packet.Slice(SetPetNameHandleOffset, 4));

        var field = packet.Slice(SetPetNameNameOffset, NameSize);
        var terminator = field.IndexOf((byte)0);
        name = Encoding.ASCII.GetString(terminator < 0 ? field : field.Slice(0, terminator));

        return true;
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
