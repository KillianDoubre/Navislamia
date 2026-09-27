using System;
using System.Buffers.Binary;

namespace Navislamia.Game.Network.Packets.Game;

/// <summary>
/// The client to server half of the HuntaHolic lobby family, <c>TM_CS_HUNTAHOLIC_JOIN_INSTANCE</c> (4004).
///
/// The id is unconditional in rzu (<c>X(4004, true)</c>, which expands to <c>if (true) id = id_;</c>): the file
/// holds that single <c>X(...)</c> entry, so 4004 is the same in Epic 7.3 as in every other version, and neither
/// payload field carries an <c>#if EPIC_*</c> guard — no gated field exists and no id variant has to be named.
/// The frame is <b>28 bytes</b>: the 7-byte header, <c>instance_no</c> (<c>int32_t</c>) at offset 7 and a fixed
/// 17-byte password buffer at 11 (7 + 4 + 17).
///
/// The three sources agree exactly: rzu's <c>_(simple)(int32_t, instance_no)</c> then
/// <c>_(string)(password, 17)</c>, NGemity's identical <c>_DEF</c> (<c>CREATE_PACKET(..., 4004)</c>), and the
/// 7.3 client's own frame constructor: its single <c>mov ecx,0xfa4</c> in the whole binary is immediately
/// followed by <c>mov word [eax+4],cx</c> (the ID) and <c>mov dword [eax],0x1c</c> (the length, 28). The client
/// sends the frame from exactly two sites: the public-room path (password buffer fully zeroed) and the
/// <c>onConfirmPassword</c> window callback, which copies the typed text into the same 17-byte field.
///
/// Only the reading half lives here. No server to client packet answers a 4004: neither rzu nor NGemity
/// declares one, and the only client reaction identified — a result message whose <c>request_msg_id</c> is 4004
/// and whose 16-bit <c>result</c> is <c>0x20</c>, which prints the daily-quota notice — is not emitted here:
/// without lobby state the server cannot know whether a quota is exhausted, and the text the client prints is
/// the room <i>creation</i> one, so any refusal would state a false reason.
/// See docs/packet-specs/4004-huntaholic-join-instance.md §3, §5.2, §5.3.
/// </summary>
public static class GameHuntaholicPackets
{
    private const int HeaderSize = 7;

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
