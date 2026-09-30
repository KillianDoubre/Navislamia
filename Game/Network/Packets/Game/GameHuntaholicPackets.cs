using System;
using System.Buffers.Binary;

namespace Navislamia.Game.Network.Packets.Game;

/// <summary>
/// The client to server half of the HuntaHolic lobby family, <c>TM_CS_HUNTAHOLIC_INSTANCE_LIST</c> (4000).
///
/// The id is unconditional in rzu (<c>X(4000, true)</c>, which expands to <c>if(true) id = id_;</c>): 4000 is
/// the same in Epic 7.3 as in every other version, and the single payload field carries no <c>#if EPIC_*</c>
/// guard, so no field is gated and no version variant exists. The frame is <b>11 bytes</b>: the 7-byte header
/// and one signed <c>int32</c> <c>page</c> at offset 7.
///
/// The three sources agree without reserve: rzu's <c>_(simple)(int32_t, page)</c>, NGemity's identical
/// declaration, and the 7.3 client's own frame constructor at <c>0x4c91f0</c> (<c>Length = 0xb</c> written in
/// hard at <c>0x4c920e</c>, <c>ID = 0xfa0</c> at <c>0x4c9205</c>, the <c>int32</c> written at <c>0x4c9237</c>,
/// checksum over the first six bytes at <c>0x4c9220</c>).
///
/// Only the reading half lives here. No server to client packet answers a 4000 in this lot: the client's
/// lobby is fed by <c>TM_SC_HUNTAHOLIC_INSTANCE_LIST</c> (4001), whose content, pagination and
/// <c>huntaholic_id</c> have no source in the repository, so no answer is invented and no catalogue is built.
/// See docs/packet-specs/4000-huntaholic-instance-list.md §5.3, §7a, §7b.
/// </summary>
public static class GameHuntaholicPackets
{
    private const int HeaderSize = 7;

    /// <summary>Offset of <c>page</c>: right after the 7-byte header.</summary>
    public const int PageOffset = HeaderSize;

    /// <summary>
    /// Width of <c>page</c>: the single <c>int32_t</c> rzu declares, 4 bytes, which is also what the 7.3
    /// client writes (<c>mov DWORD PTR [ebp-0x5],edx</c>).
    /// </summary>
    public const int PageFieldLength = 4;

    /// <summary>Payload size of the frame: one <c>int32</c>, nothing else.</summary>
    public const int PayloadLength = PageFieldLength;

    /// <summary>Total size of TM_CS_HUNTAHOLIC_INSTANCE_LIST (4000): 11 bytes.</summary>
    public const int InstanceListLength = HeaderSize + PayloadLength;

    /// <summary>
    /// Reads TM_CS_HUNTAHOLIC_INSTANCE_LIST (4000). Only the exact 11-byte form is accepted: the client writes
    /// that length in hard at <c>0x4c920e</c> and the frame is of fixed size — no array, no padding — so a
    /// shorter frame is a truncated request and a longer one carries bytes no field accounts for.
    ///
    /// <c>page</c> is read <b>signed</b>, exactly as rzu declares it (<c>int32_t</c>): the value is journalled
    /// raw and is never validated against a range. The client only ever sends a 1-based page it believes valid
    /// (opening the lobby asks for 1, refreshing replays the page the 4001 answer clamped to), but what a
    /// server should do with a zero, a negative or an out-of-range page is not established by any reference
    /// (spec §7c), so nothing is clamped, refused or defaulted here.
    /// </summary>
    public static bool TryReadHuntaholicInstanceList(ReadOnlySpan<byte> packet, out int page)
    {
        if (packet.Length != InstanceListLength)
        {
            page = 0;
            return false;
        }

        page = BinaryPrimitives.ReadInt32LittleEndian(packet.Slice(PageOffset, PageFieldLength));
        return true;
    }

    /// <summary>
    /// Total size of <c>TM_CS_HUNTAHOLIC_BEGIN_HUNTING</c> (4011): the 7-byte header, no payload at all. The
    /// 7.3 client writes that length in hard (<c>mov DWORD PTR [ebp-0x7],0x7</c> at <c>0x4c93eb</c>) and never
    /// writes a byte past offset 6, so the header is the entire frame.
    /// </summary>
    public const int BeginHuntingLength = HeaderSize;

    /// <summary>
    /// <c>TM_CS_HUNTAHOLIC_BEGIN_HUNTING</c> (4011) is the "start the hunt" gesture of the HuntaHolic instance
    /// window and carries no payload: there is nothing to read and no field to extract. Only the exact 7-byte
    /// form is accepted — a shorter frame is truncated, and a longer one carries bytes no field accounts for
    /// (an 11-byte frame is the <c>4009</c> shape and 6 bytes is a cut header), so the sender is not the 7.3
    /// client. The frame's checksum byte at offset 6 is not verified here, as in every other packet of the
    /// family (an assumed gap, stated in the sheet §3 rather than papered over); the receive loop does check it
    /// before dispatching.
    /// </summary>
    public static bool IsBeginHunting(ReadOnlySpan<byte> packet)
    {
        return packet.Length == BeginHuntingLength;
    }

    /// <summary>
    /// Total size of TM_CS_HUNTAHOLIC_LEAVE_INSTANCE (4005): the 7-byte header, no payload at all. The 7.3
    /// client writes that length in hard at <c>0x4c939b</c> and never writes a byte past offset 6, so the
    /// header is the entire frame.
    /// </summary>
    public const int LeaveInstanceLength = HeaderSize;

    /// <summary>
    /// TM_CS_HUNTAHOLIC_LEAVE_INSTANCE (4005) is the "leave the instance" gesture of the HuntaHolic family and
    /// carries no payload: there is nothing to read and no field to extract. Only the exact 7-byte form is
    /// accepted — a shorter frame is truncated, and a longer one carries bytes no field accounts for, so the
    /// sender is not the 7.3 client. The frame's checksum byte at offset 6 is not verified, here as in every
    /// other packet of the family (an assumed gap, stated in the sheet §3 rather than papered over).
    /// </summary>
    public static bool IsLeaveInstance(ReadOnlySpan<byte> packet)
    {
        return packet.Length == LeaveInstanceLength;
    }

    /// <summary>Offset of <c>instance_no</c> (<c>int32_t</c>): right after the 7-byte header.</summary>
    public const int JoinInstanceNoOffset = HeaderSize;

    /// <summary>Width of <c>instance_no</c>: rzu's <c>_(simple)(int32_t, instance_no)</c>.</summary>
    public const int JoinInstanceNoFieldLength = 4;

    /// <summary>Offset of the fixed password buffer: 7 + 4.</summary>
    public const int JoinInstancePasswordOffset = JoinInstanceNoOffset + JoinInstanceNoFieldLength;

    /// <summary>
    /// Width of the password field: 16 usable characters plus the NUL. rzu writes it with
    /// <c>_(string)(password, 17)</c>, that is at most 16 characters zero padded up to 17 bytes, and the client's
    /// bounded copy is capped at 17 bytes too, so the field is always part of the frame and its last byte is
    /// always the frame's last byte. An empty password — the public-room path — leaves all 17 bytes at zero,
    /// which is what <see cref="HuntaholicJoinInstanceRequest.HasPassword"/> reads.
    /// </summary>
    public const int JoinInstancePasswordFieldLength = 17;

    /// <summary>Total size of TM_CS_HUNTAHOLIC_JOIN_INSTANCE (4004): 7 + 4 + 17 = 28.</summary>
    public const int JoinInstanceLength = JoinInstancePasswordOffset + JoinInstancePasswordFieldLength;

    /// <summary>
    /// A well formed TM_CS_HUNTAHOLIC_JOIN_INSTANCE (4004). <c>instance_no</c> is read as the signed
    /// <c>int32_t</c> rzu declares — it is the first <c>dword</c> of the 38-byte room entry the list answer
    /// (4001) carries, so its domain belongs to the answer that assigns it, and the reader neither validates
    /// nor translates it.
    ///
    /// The password itself is deliberately <b>not</b> carried out of the reader: a room password is a secret
    /// that does not belong in a log line, and nothing in this lot authenticates against it. Only its length is
    /// kept, which is what the log needs to state whether a password was supplied.
    /// </summary>
    public readonly record struct HuntaholicJoinInstanceRequest(int InstanceNo, int PasswordLength)
    {
        /// <summary>True when the frame carried a non-empty password, i.e. an attempt to enter a locked room.</summary>
        public bool HasPassword => PasswordLength > 0;
    }

    /// <summary>
    /// Reads TM_CS_HUNTAHOLIC_JOIN_INSTANCE (4004). Only the exact 28-byte form is accepted: the 7.3 client
    /// writes that length in hard, so a shorter or padded frame is malformed rather than a shorter request.
    ///
    /// The password field must be terminated by a NUL <b>inside</b> its own 17 bytes. The 7.3 client cannot
    /// produce the opposite — both of its send paths leave the frame's 17-byte field at zero or copy into it
    /// with a bound of 17, NUL included — so a field with no NUL means the sender is not the 7.3 client, and
    /// refusing it keeps the reader from ever looking past the field. Same rule as the sibling readers of the
    /// 4000-4012 family (<c>TryReadHuntaholicInstanceList</c>, <c>TryReadCreateInstance</c>) and of the player
    /// competition socle (<c>GameCompetePackets.TryReadRequest</c>).
    /// </summary>
    public static bool TryReadJoinInstance(ReadOnlySpan<byte> packet, out HuntaholicJoinInstanceRequest request)
    {
        request = default;
        if (packet.Length != JoinInstanceLength)
        {
            return false;
        }

        var passwordField = packet.Slice(JoinInstancePasswordOffset, JoinInstancePasswordFieldLength);
        var passwordLength = passwordField.IndexOf((byte)0);
        if (passwordLength < 0)
        {
            return false;
        }

        request = new HuntaholicJoinInstanceRequest(
            BinaryPrimitives.ReadInt32LittleEndian(packet.Slice(JoinInstanceNoOffset, JoinInstanceNoFieldLength)),
            passwordLength);
        return true;
    }
}
