using System;
using System.Buffers.Binary;
using System.Text;
using Navislamia.Game.Network.Packets.Enums;

namespace Navislamia.Game.Network.Packets.Game;

/// <summary>
/// The Epic 7.3 familier (pet) packets the server emits (S→C), i.e. the part of the pet socle whose wire
/// layout is fixed by its references. Every size, field order and version decision comes from
/// <c>docs/packet-specs/socle-familier-pet.md</c> — read it before changing anything here.
/// </summary>
/// <remarks>
/// Nothing in this class decides <em>when</em> a frame leaves the server: the trigger of a pet's entry,
/// what the pet collects and what its filter allows are gameplay policies that are not settled yet. The
/// layouts below are pure encoders, so they carry no threshold, no cost and no default of their own. The
/// three ids are server-to-client only and are dropped with a log line by <c>GameClient</c> when a client
/// sends one (§6.2).
/// </remarks>
public static class GamePetPackets
{
    private const int HeaderSize = 7;

    /// <summary>
    /// Fixed <c>name</c> buffer of <c>TS_SC_ADD_PET_INFO</c>. The same 19 bytes (18 usable characters plus
    /// the nul terminator) as the creature window of the summon: rzu declares
    /// <c>_(def)(string)(name, 20)</c> with a <c>_(impl)(string)(name, 19, version &lt; EPIC_9_6)</c>
    /// override, and Epic 7.3 is below 9.6 (<c>TS_SC_ADD_PET_INFO.h:10-12</c>).
    /// </summary>
    public const int NameSize = 19;

    /// <summary>
    /// <c>TM_SC_ADD_PET_INFO</c> (351). <b>35 payload bytes, 42 with the header</b>: <c>cage_handle</c> @0,
    /// <c>pet_handle</c> @4, <c>name</c> @8 (19), <c>code</c> @27, and a fifth <c>int32</c> @31 that
    /// <b>neither rzu nor NGemity declares</b> (both stop at <c>code</c>, i.e. 38 bytes total).
    /// <para>
    /// The 42 come from the client: the 7.3 handler reads the frame from <c>paquet+7</c> to
    /// <c>paquet+0x29</c> inclusive (<c>SFrame.exe</c> @<c>0x66f600</c>, instructions
    /// <c>0x66f651</c>-<c>0x66f692</c>), and the method is calibrated on the sister frame <c>301</c>, whose
    /// window read is exactly the 39 payload bytes rzu declares. Do not shorten this frame to 38.
    /// </para>
    /// </summary>
    /// <param name="code">
    /// The fourth <c>int32</c>, the field both references call <c>code</c>. Its source is not established
    /// (fiche <c>NON ÉTABLI</c> 2). Do not substitute a guess.
    /// </param>
    /// <param name="unknown">
    /// The <b>fifth</b> <c>int32</c>. No source names it — rzu and NGemity never declared it and the client
    /// copies it without a label (fiche <c>NON ÉTABLI</c> 2) — so it is named <c>unknown</c> on purpose and
    /// must not receive an invented constant. The caller supplies it.
    /// </param>
    public static byte[] BuildAddPetInfo(uint cageHandle, uint petHandle, string name, int code,
        int unknown)
    {
        const int payload = 35;
        var packet = CreatePacket(GamePackets.TM_SC_ADD_PET_INFO, HeaderSize + payload);
        var span = packet.AsSpan();

        BinaryPrimitives.WriteUInt32LittleEndian(span.Slice(HeaderSize, 4), cageHandle);
        BinaryPrimitives.WriteUInt32LittleEndian(span.Slice(HeaderSize + 4, 4), petHandle);
        WriteName(span.Slice(HeaderSize + 8, NameSize), name);
        BinaryPrimitives.WriteInt32LittleEndian(span.Slice(HeaderSize + 27, 4), code);
        BinaryPrimitives.WriteInt32LittleEndian(span.Slice(HeaderSize + 31, 4), unknown);

        WriteChecksum(packet);
        return packet;
    }

    /// <summary>
    /// <c>TM_SC_REMOVE_PET_INFO</c> (352). 4 payload bytes, 11 with the header: <c>handle</c> @0
    /// (<c>TS_SC_REMOVE_PET_INFO.h:6</c>), read by the client at <c>paquet+7</c> into <c>msg+0x13</c>
    /// (<c>SFrame.exe</c> @<c>0x66f6b0</c>).
    /// </summary>
    public static byte[] BuildRemovePetInfo(uint handle) => BuildHandlePacket(GamePackets.TM_SC_REMOVE_PET_INFO, handle);

    /// <summary>
    /// <c>TM_SC_UNSUMMON_PET</c> (350). 4 payload bytes, 11 with the header: <c>handle</c> @0
    /// (<c>TS_SC_UNSUMMON_PET.h:8</c>). The client, on receiving it, finds the actor by that handle,
    /// checks that it really is an <c>EOT_Pet</c> (7) and removes it from the world itself
    /// (<c>SFrame.exe</c> @<c>0x66f5a0</c>, <c>SCommandSystem</c> case <c>MSG_UNSUMMON_PET</c>), so this
    /// frame is the whole retrait on the wire.
    /// </summary>
    public static byte[] BuildUnsummonPet(uint handle) => BuildHandlePacket(GamePackets.TM_SC_UNSUMMON_PET, handle);

    /// <summary>
    /// <c>TM_SC_SHOW_SET_PET_NAME</c> (353). 4 payload bytes, 11 with the header: <c>handle</c> @0
    /// (<c>TS_SC_SHOW_SET_PET_NAME.h:5-6</c>). The client resolves the handle to a live creature and, only
    /// then, opens its name box; the 354 that follows carries the same handle back
    /// (docs/packet-specs/354-set-pet-name.md §2).
    /// </summary>
    public static byte[] BuildShowSetPetName(uint handle) =>
        BuildHandlePacket(GamePackets.TM_SC_SHOW_SET_PET_NAME, handle);

    /// <summary><c>TM_CS_SET_PET_FILTER</c> (355): exactly 15 bytes, <c>handle</c> @7 and the filter value @11.</summary>
    public const int SetPetFilterPacketSize = HeaderSize + 8;

    /// <summary>
    /// Reads <c>TM_CS_SET_PET_FILTER</c> (355), whose frame the client builds at <c>SFrame.exe</c>
    /// @<c>0x48e1f0</c> from its <c>PET_PICKUP_FILTER</c> option. Only the exact 15-byte form is read; the
    /// value is returned raw, its meaning being <c>NON ÉTABLI</c> (socle-familier-pet.md §11.4).
    /// </summary>
    public static bool TryReadSetPetFilter(ReadOnlySpan<byte> packet, out uint handle, out uint filter)
    {
        handle = 0;
        filter = 0;
        if (packet.Length != SetPetFilterPacketSize)
        {
            return false;
        }

        handle = BinaryPrimitives.ReadUInt32LittleEndian(packet.Slice(HeaderSize, 4));
        filter = BinaryPrimitives.ReadUInt32LittleEndian(packet.Slice(HeaderSize + 4, 4));
        return true;
    }

    private static byte[] BuildHandlePacket(GamePackets id, uint handle)
    {
        var packet = CreatePacket(id, HeaderSize + 4);
        BinaryPrimitives.WriteUInt32LittleEndian(packet.AsSpan(HeaderSize, 4), handle);
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

    /// <summary>
    /// Writes a pet's <c>name</c> into its 19-byte slot: at most 18 bytes, the rest left zero —
    /// <c>0x00</c>-terminated on the wire (<c>MessageBuffer::writeString</c>,
    /// <c>reference/rzu/librzu/src/lib/Packet/MessageBuffer.cpp:87-94</c>). Byte-for-byte the writer the
    /// entry tram uses, so the name announced in the creature window reads back identically in the world.
    /// </summary>
    private static void WriteName(Span<byte> target, string name)
    {
        var bytes = Encoding.ASCII.GetBytes(name ?? string.Empty);
        var length = Math.Min(bytes.Length, target.Length - 1);
        bytes.AsSpan(0, length).CopyTo(target);
    }
}
