using System;
using System.Buffers.Binary;
using System.Text;
using Navislamia.Game.Network.Packets.Enums;

namespace Navislamia.Game.Network.Packets.Game;

public static class GameSpawnPackets
{
    private const int HeaderSize = 7;

    /// <summary>
    /// The fixed <c>name</c> buffer of a creature — 19 bytes in Epic 7.3 (<c>&gt;= EPIC_3 &amp;&amp; &lt;
    /// EPIC_9_6</c>, <c>TS_SC_ENTER.h:94-96</c>): 18 usable characters plus the terminator. It is the
    /// same width the creature window uses, so it is read from <see cref="GameSummonPackets.NameSize"/>
    /// rather than repeated.
    /// </summary>
    public const int NameSize = GameSummonPackets.NameSize;

    private const int EncodedIdOffset = 64;
    private const byte EnterTypeCreature = 1;
    private const byte EnterTypeStaticObject = 2;
    private const byte ObjectTypeNpc = 1;
    private const byte ObjectTypeItem = 2;
    private const byte ObjectTypeMonster = 3;
    private const byte ObjectTypeSummon = 4;
    private const byte ObjectTypeFieldProp = 6;
    private const byte ObjectTypePet = 7;

    public static byte[] BuildEnterNpc(uint handle, float x, float y, float z, byte layer,
        int hp, int level, byte race, int npcId)
    {
        const int length = HeaderSize + 1 + 4 + 12 + 1 + 1 + 38 + 8;
        var packet = BuildEnterCreature(length, handle, x, y, z, layer, hp, level, race, ObjectTypeNpc, 0f,
            ActorStatus.ForNpc());

        WriteEncodedInt(packet.AsSpan(EncodedIdOffset, 8), (uint)npcId);
        WriteChecksum(packet);

        return packet;
    }

    public static byte[] BuildEnterMonster(uint handle, float x, float y, float z, byte layer,
        int hp, int level, byte race, int monsterId, float faceDir)
    {
        const int length = HeaderSize + 1 + 4 + 12 + 1 + 1 + 38 + 8 + 1;
        var packet = BuildEnterCreature(length, handle, x, y, z, layer, hp, level, race,
            ObjectTypeMonster, faceDir, ActorStatus.ForMonster());

        WriteEncodedInt(packet.AsSpan(EncodedIdOffset, 8), ScrambledInt.Encode((uint)monsterId));
        packet[72] = 0;
        WriteChecksum(packet);

        return packet;
    }

    /// <summary>
    /// <c>TM_SC_ENTER</c> (3) for a summon — <b>96 bytes</b>: the 26-byte creature prefix, the 38-byte
    /// shared creature block, then <c>master_handle</c> @64, the 8-byte randomized <c>summon_code</c> @68,
    /// the 19-byte <c>name</c> @76 (18 usable characters, zero filled) and <c>enhance</c> @95.
    /// <c>type</c> is <c>ET_NPC</c> (1) and <c>objType</c> is <c>EOT_Summon</c> (4); every size, offset
    /// and version decision comes from <c>docs/packet-specs/socle-invocation-monde.md</c> §3.1 and §4.
    /// </summary>
    /// <param name="maxHp">
    /// Caller-supplied: no reference settles a summon's maximum health (§7, <c>NON ÉTABLI</c> 5). Do not
    /// substitute <paramref name="hp"/>.
    /// </param>
    /// <param name="maxMp">Caller-supplied for the same reason as <paramref name="maxHp"/>.</param>
    /// <param name="z">
    /// Caller-supplied: NGemity sends the summon's own <c>z</c>, which its placement never sets (§5.2,
    /// <c>NON ÉTABLI</c> 6). Do not assume the master's <c>z</c>.
    /// </param>
    /// <param name="isFirstEnter">
    /// 1 on the first entry into the world, 0 on a re-entry (§7, <c>NON ÉTABLI</c> 8).
    /// </param>
    /// <param name="enhance">
    /// Caller-supplied: the field exists in 7.3 (<c>&gt;= EPIC_7_1</c>) but nothing fills it yet
    /// (§7, <c>NON ÉTABLI</c> 7). Pass 0 while no source exists.
    /// </param>
    /// <param name="summonCode">
    /// <c>SummonEntity.SummonResourceId</c> per the reference — the same value as <c>code</c> in
    /// <c>TS_SC_ADD_SUMMON_INFO</c> (§3.1, §6) — but the caller supplies it: no foreign key guarantees
    /// the column holds a <c>SummonResource.id</c> (<c>NON ÉTABLI</c> 3).
    /// </param>
    public static byte[] BuildEnterSummon(uint handle, float x, float y, float z, byte layer,
        int hp, int maxHp, int mp, int maxMp, int level, float faceDir, bool isFirstEnter,
        uint masterHandle, uint summonCode, string name, byte enhance)
    {
        const int length = HeaderSize + 1 + 4 + 12 + 1 + 1 + 38 + 4 + 8 + NameSize + 1;
        var packet = BuildEnterCreature(length, handle, x, y, z, layer, hp, level, 0, ObjectTypeSummon,
            faceDir, ActorStatus.ForSummon(), maxHp: maxHp, mp: mp, maxMp: maxMp, isFirstEnter: isFirstEnter);

        BinaryPrimitives.WriteUInt32LittleEndian(packet.AsSpan(64, 4), masterHandle);
        WriteEncodedInt(packet.AsSpan(68, 8), summonCode);
        WriteName(packet.AsSpan(76, NameSize), name);
        packet[95] = enhance;
        WriteChecksum(packet);

        return packet;
    }

    /// <summary>
    /// <c>TM_SC_ENTER</c> (3) for a familier (pet) — <b>95 bytes</b>: the 26-byte creature prefix, the
    /// 38-byte shared creature block, then <c>master_handle</c> @64, the 8-byte randomized <c>pet_code</c>
    /// @68 and the 19-byte <c>name</c> @76 (18 usable characters, zero filled). <c>type</c> is
    /// <c>ET_NPC</c> (1) and <c>objType</c> is <c>EOT_Pet</c> (7), the only entry tram the 7.3 client
    /// executes for a pet (<c>docs/packet-specs/socle-familier-pet.md</c> §5, §5.1). It is the summon
    /// tram <b>minus</b> the <c>enhance</c> byte @95: <c>TS_SC_ENTER__PET_INFO</c> does not declare it
    /// (<c>TS_SC_ENTER.h:132-143</c>), so nothing is written at offset 95.
    /// </summary>
    /// <param name="race">
    /// The <c>race</c> of the creature block. The pet tram declares the field in 7.3
    /// (<c>&lt; EPIC_9_6_7</c>) and no server table carries a value for a pet: caller-supplied
    /// (fiche §11.7).
    /// </param>
    /// <param name="maxHp">
    /// Caller-supplied: neither <c>PetEntity</c> nor <c>PetResource</c> has a health column, so no
    /// reference settles a pet's maximum (fiche §9.2, §11.3). Do not substitute <paramref name="hp"/>.
    /// </param>
    /// <param name="maxMp">Caller-supplied for the same reason as <paramref name="maxHp"/>.</param>
    /// <param name="z">
    /// Caller-supplied: no reference places a pet in the world at all, so this one carries the whole
    /// placement (fiche §11.7). Do not assume the master's <c>z</c>.
    /// </param>
    /// <param name="isFirstEnter">
    /// 1 on the first entry into the world, 0 on a re-entry (fiche §11.7).
    /// </param>
    /// <param name="petCode">
    /// <c>pet_code</c> — the reference's <c>SummonEntity.SummonResourceId</c> read is the only symmetry
    /// this field has: <c>PetEntity.PetResourceId</c> is a rapprochement, not a proven key (fiche §11.1).
    /// The caller supplies it.
    /// </param>
    public static byte[] BuildEnterPet(uint handle, float x, float y, float z, byte layer,
        int hp, int maxHp, int mp, int maxMp, int level, byte race, float faceDir, bool isFirstEnter,
        uint masterHandle, uint petCode, string name)
    {
        const int length = HeaderSize + 1 + 4 + 12 + 1 + 1 + 38 + 4 + 8 + NameSize;
        var packet = BuildEnterCreature(length, handle, x, y, z, layer, hp, level, race, ObjectTypePet,
            faceDir, ActorStatus.ForPet(), maxHp: maxHp, mp: mp, maxMp: maxMp, isFirstEnter: isFirstEnter);

        BinaryPrimitives.WriteUInt32LittleEndian(packet.AsSpan(64, 4), masterHandle);
        WriteEncodedInt(packet.AsSpan(68, 8), petCode);
        WriteName(packet.AsSpan(76, NameSize), name);
        WriteChecksum(packet);

        return packet;
    }

    public static byte[] BuildEnterItem(uint handle, float x, float y, float z, byte layer,
        int itemCode, long count, uint dropTime, uint ownerHandle)
    {
        const int length = HeaderSize + 1 + 4 + 12 + 1 + 1 + 8 + 8 + 4 + 12 + 12;
        var packet = new byte[length];
        var span = packet.AsSpan();

        WriteHeader(span, length, GamePackets.TM_SC_ENTER);
        packet[7] = EnterTypeStaticObject;
        BinaryPrimitives.WriteUInt32LittleEndian(span.Slice(8, 4), handle);
        BinaryPrimitives.WriteSingleLittleEndian(span.Slice(12, 4), x);
        BinaryPrimitives.WriteSingleLittleEndian(span.Slice(16, 4), y);
        BinaryPrimitives.WriteSingleLittleEndian(span.Slice(20, 4), z);
        packet[24] = layer;
        packet[25] = ObjectTypeItem;

        WriteEncodedInt(span.Slice(26, 8), (uint)itemCode);
        BinaryPrimitives.WriteUInt64LittleEndian(span.Slice(34, 8), (ulong)Math.Max(1, count));
        BinaryPrimitives.WriteUInt32LittleEndian(span.Slice(42, 4), dropTime);
        BinaryPrimitives.WriteUInt32LittleEndian(span.Slice(46, 4), ownerHandle);

        WriteChecksum(packet);
        return packet;
    }

    public static byte[] BuildEnterFieldProp(uint handle, float x, float y, float z, byte layer,
        int propId, float zOffset, float rotateX, float rotateY, float rotateZ,
        float scaleX, float scaleY, float scaleZ, bool lockHeight, float lockHeightValue)
    {
        const int length = HeaderSize + 1 + 4 + 12 + 1 + 1 + 4 + 4 + 12 + 12 + 1 + 4;
        var packet = new byte[length];
        var span = packet.AsSpan();

        WriteHeader(span, length, GamePackets.TM_SC_ENTER);
        packet[7] = EnterTypeStaticObject;
        BinaryPrimitives.WriteUInt32LittleEndian(span.Slice(8, 4), handle);
        BinaryPrimitives.WriteSingleLittleEndian(span.Slice(12, 4), x);
        BinaryPrimitives.WriteSingleLittleEndian(span.Slice(16, 4), y);
        BinaryPrimitives.WriteSingleLittleEndian(span.Slice(20, 4), z);
        packet[24] = layer;
        packet[25] = ObjectTypeFieldProp;

        BinaryPrimitives.WriteUInt32LittleEndian(span.Slice(26, 4), (uint)propId);
        BinaryPrimitives.WriteSingleLittleEndian(span.Slice(30, 4), zOffset);
        BinaryPrimitives.WriteSingleLittleEndian(span.Slice(34, 4), rotateX);
        BinaryPrimitives.WriteSingleLittleEndian(span.Slice(38, 4), rotateY);
        BinaryPrimitives.WriteSingleLittleEndian(span.Slice(42, 4), rotateZ);
        BinaryPrimitives.WriteSingleLittleEndian(span.Slice(46, 4), scaleX);
        BinaryPrimitives.WriteSingleLittleEndian(span.Slice(50, 4), scaleY);
        BinaryPrimitives.WriteSingleLittleEndian(span.Slice(54, 4), scaleZ);
        packet[58] = (byte)(lockHeight ? 1 : 0);
        BinaryPrimitives.WriteSingleLittleEndian(span.Slice(59, 4), lockHeightValue);

        WriteChecksum(packet);
        return packet;
    }

    public static byte[] BuildWarp(float x, float y, float z, sbyte layer)
    {
        const int length = HeaderSize + 12 + 1;
        var packet = new byte[length];
        var span = packet.AsSpan();

        WriteHeader(span, length, GamePackets.TM_SC_WARP);
        BinaryPrimitives.WriteSingleLittleEndian(span.Slice(7, 4), x);
        BinaryPrimitives.WriteSingleLittleEndian(span.Slice(11, 4), y);
        BinaryPrimitives.WriteSingleLittleEndian(span.Slice(15, 4), z);
        packet[19] = (byte)layer;

        WriteChecksum(packet);
        return packet;
    }

    public static byte[] BuildTakeItemResult(uint itemHandle, uint takerHandle)
    {
        const int length = HeaderSize + 8;
        var packet = new byte[length];
        var span = packet.AsSpan();

        WriteHeader(span, length, GamePackets.TM_SC_TAKE_ITEM_RESULT);
        BinaryPrimitives.WriteUInt32LittleEndian(span.Slice(HeaderSize, 4), itemHandle);
        BinaryPrimitives.WriteUInt32LittleEndian(span.Slice(HeaderSize + 4, 4), takerHandle);

        WriteChecksum(packet);
        return packet;
    }

    public static byte[] BuildLeave(uint handle)
    {
        const int length = HeaderSize + 4;
        var packet = new byte[length];
        var span = packet.AsSpan();

        WriteHeader(span, length, GamePackets.TM_SC_LEAVE);
        BinaryPrimitives.WriteUInt32LittleEndian(span.Slice(HeaderSize, 4), handle);
        WriteChecksum(packet);

        return packet;
    }

    /// <param name="maxHp">
    /// The <c>max_hp</c> of the creature block. Defaults to <paramref name="hp"/>, which is what the NPC
    /// and monster callers have always sent (no maximum is modelled for them); a caller that knows its own
    /// maximum — a summon does, its row carries <c>Hp</c> and <c>Mp</c> — passes it.
    /// </param>
    /// <param name="mp">The <c>mp</c> of the creature block. NPCs and monsters send 0.</param>
    /// <param name="maxMp">The <c>max_mp</c> of the creature block. Defaults to 0, as before.</param>
    /// <param name="isFirstEnter">
    /// The <c>is_first_enter</c> flag of the creature block. NPCs and monsters send 0.
    /// </param>
    private static byte[] BuildEnterCreature(int length, uint handle, float x, float y, float z,
        byte layer, int hp, int level, byte race, byte objectType, float faceDir, uint status,
        int? maxHp = null, int mp = 0, int? maxMp = null, bool isFirstEnter = false)
    {
        var packet = new byte[length];
        var span = packet.AsSpan();

        WriteHeader(span, length, GamePackets.TM_SC_ENTER);
        packet[7] = EnterTypeCreature;
        BinaryPrimitives.WriteUInt32LittleEndian(span.Slice(8, 4), handle);
        BinaryPrimitives.WriteSingleLittleEndian(span.Slice(12, 4), x);
        BinaryPrimitives.WriteSingleLittleEndian(span.Slice(16, 4), y);
        BinaryPrimitives.WriteSingleLittleEndian(span.Slice(20, 4), z);
        packet[24] = layer;
        packet[25] = objectType;
        BinaryPrimitives.WriteUInt32LittleEndian(span.Slice(26, 4), status);
        BinaryPrimitives.WriteSingleLittleEndian(span.Slice(30, 4), faceDir);
        BinaryPrimitives.WriteInt32LittleEndian(span.Slice(34, 4), hp);
        BinaryPrimitives.WriteInt32LittleEndian(span.Slice(38, 4), maxHp ?? hp);
        BinaryPrimitives.WriteInt32LittleEndian(span.Slice(42, 4), mp);
        BinaryPrimitives.WriteInt32LittleEndian(span.Slice(46, 4), maxMp ?? 0);
        BinaryPrimitives.WriteInt32LittleEndian(span.Slice(50, 4), level);
        packet[54] = race;
        BinaryPrimitives.WriteUInt32LittleEndian(span.Slice(55, 4), 0);
        packet[59] = isFirstEnter ? (byte)1 : (byte)0;
        BinaryPrimitives.WriteInt32LittleEndian(span.Slice(60, 4), 0);

        return packet;
    }

    private static void WriteHeader(Span<byte> packet, int length, GamePackets id)
    {
        BinaryPrimitives.WriteUInt32LittleEndian(packet.Slice(0, 4), (uint)length);
        BinaryPrimitives.WriteUInt16LittleEndian(packet.Slice(4, 2), (ushort)id);
    }

    /// <summary>
    /// Writes an <c>EncodedInt&lt;EncodingRandomized&gt;</c>: the 8-byte layout that encoding gives
    /// (<c>reference/rzu/librzu/src/lib/Packet/EncodingRandomized.h:16-32</c>) — two zero words, the value's
    /// high 16 bits at +2 and its low 16 bits at +6. <b>The value goes in untouched</b>: the other two words
    /// are the randomized part, and both references serialize them as zero.
    /// <para>
    /// <see cref="ScrambledInt"/> is a <b>different</b> encoding — <c>EncodingScrambled</c> permutes the bits
    /// and then reuses this very layout (<c>EncodingScrambled.h:11-16</c>) — and rzu declares it field by
    /// field: <c>monster_id</c> is the only scrambled id this repository emits (<c>TS_SC_ENTER.h:85</c>), while
    /// <c>npc_id</c> (<c>:105</c>), an item's <c>code</c> (<c>:38</c>) and a summon's <c>summon_code</c>
    /// (<c>:93</c>) are all randomized. Passing <c>ScrambledInt.Encode(...)</c> to this writer for a
    /// randomized field permutes an id the client reads straight.
    /// </para>
    /// </summary>
    private static void WriteEncodedInt(Span<byte> target, uint value)
    {
        BinaryPrimitives.WriteUInt16LittleEndian(target.Slice(0, 2), 0);
        BinaryPrimitives.WriteUInt16LittleEndian(target.Slice(2, 2), (ushort)(value >> 16));
        BinaryPrimitives.WriteUInt16LittleEndian(target.Slice(4, 2), 0);
        BinaryPrimitives.WriteUInt16LittleEndian(target.Slice(6, 2), (ushort)value);
    }

    /// <summary>
    /// Writes a creature's <c>name</c> into its 19-byte slot: at most 18 bytes, the rest left zero —
    /// <c>0x00</c>-terminated on the wire (<c>MessageBuffer::writeString</c>,
    /// <c>reference/rzu/librzu/src/lib/Packet/MessageBuffer.cpp:87-94</c>). It is byte-for-byte the writer
    /// <c>GameSummonPackets.WriteName</c> uses for the creature window, so a name <c>TS_SC_ADD_SUMMON_INFO</c>
    /// announced reads back identically in the world.
    /// </summary>
    private static void WriteName(Span<byte> target, string name)
    {
        var bytes = Encoding.ASCII.GetBytes(name ?? string.Empty);
        var length = Math.Min(bytes.Length, target.Length - 1);
        bytes.AsSpan(0, length).CopyTo(target);
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
