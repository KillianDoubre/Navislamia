using System;
using System.Text;

namespace Navislamia.Game.Network.Packets.Game;

/// <summary>
/// The client to server half of the HuntaHolic lobby family, <c>TM_CS_HUNTAHOLIC_CREATE_INSTANCE</c> (4003).
///
/// The id is unconditional in rzu (<c>X(4003, true)</c>, which expands to <c>if(true) id = id_;</c>): 4003 is
/// the same in Epic 7.3 as in every other version and the three payload fields carry no <c>#if EPIC_*</c>
/// guard, so no field is gated and no version variant exists. The frame is <b>56 bytes</b>: the 7-byte header,
/// a fixed 31-byte room name at offset 7, <c>max_member_count</c> (<c>int8</c>) at 38 and a fixed 17-byte
/// password at 39 (7 + 31 + 1 + 17).
///
/// The three sources agree exactly: rzu's <c>_(string)(name, 31)</c> + <c>_(simple)(int8_t,
/// max_member_count)</c> + <c>_(string)(password, 17)</c>, NGemity's identical <c>_DEF</c>, and the 7.3 client's
/// own frame constructor at <c>0x563c20</c> (<c>memset(frame, 0, 0x38)</c>, <c>Length = 0x38</c>,
/// <c>ID = 0xfa3</c>, names copied bounded to 31 and 17 bytes).
///
/// Only the reading half lives here. No server to client packet answers a 4003 — neither rzu nor NGemity
/// declares one, and the sole client reaction identified (the daily quota refusal, carried by a 16-bit field
/// worth <c>0x20</c>) has no established wire form — so nothing is built for it.
/// See docs/packet-specs/4003-huntaholic-create-instance.md.
/// </summary>
public static class GameHuntaholicPackets
{
    private const int HeaderSize = 7;

    /// <summary>Offset of the fixed room name buffer: right after the 7-byte header.</summary>
    public const int NameOffset = HeaderSize;

    /// <summary>
    /// Width of the room name field: 30 usable characters plus the NUL, which the client's bounded copy leaves
    /// inside the buffer. rzu writes it with <c>_(string)(name, 31)</c>, that is at most 30 characters padded
    /// with NUL up to 31 bytes.
    /// </summary>
    public const int NameFieldLength = 31;

    /// <summary>Offset of <c>max_member_count</c> (<c>int8</c>): 7 + 31.</summary>
    public const int MaxMemberCountOffset = NameOffset + NameFieldLength;

    /// <summary>Offset of the fixed password buffer: 7 + 31 + 1.</summary>
    public const int PasswordOffset = MaxMemberCountOffset + 1;

    /// <summary>
    /// Width of the password field: 16 usable characters plus the NUL. rzu writes it with
    /// <c>_(string)(password, 17)</c>; the client's empty password leaves the whole field at zero, because the
    /// frame is memset before the bounded copy and that copy is skipped when the field is empty.
    /// </summary>
    public const int PasswordFieldLength = 17;

    /// <summary>Payload size, name and password buffers included: 31 + 1 + 17 = 49.</summary>
    public const int PayloadLength = NameFieldLength + 1 + PasswordFieldLength;

    /// <summary>Total size of TM_CS_HUNTAHOLIC_CREATE_INSTANCE (4003): 56 bytes.</summary>
    public const int CreateInstanceLength = HeaderSize + PayloadLength;

    /// <summary>
    /// A well formed TM_CS_HUNTAHOLIC_CREATE_INSTANCE (4003). <c>max_member_count</c> is read as the
    /// <c>int8_t</c> rzu declares — the client copies the byte out of its window with a plain byte load, so
    /// both readings agree on the byte and the signed one is the declared type.
    ///
    /// The password itself is deliberately <b>not</b> carried out of the reader: the create request is logged
    /// when it arrives, and a room password is a secret that does not belong in a log line. Only its length is
    /// kept, which is what the log needs to state whether a password was supplied.
    /// </summary>
    public readonly record struct HuntaholicCreateInstanceRequest(
        string Name,
        sbyte MaxMemberCount,
        int PasswordLength)
    {
        /// <summary>True when the frame carried a non-empty password, i.e. a room the client will lock.</summary>
        public bool HasPassword => PasswordLength > 0;
    }

    /// <summary>
    /// Reads TM_CS_HUNTAHOLIC_CREATE_INSTANCE (4003). Only the exact 56-byte form is accepted: the client
    /// writes that length in hard (<c>mov DWORD PTR [esi],0x38</c> at <c>0x563c56</c>), so a shorter or padded
    /// frame is malformed rather than a shorter request.
    ///
    /// Both fixed-size string fields must be terminated by a NUL <b>inside</b> their own width. The 7.3 client
    /// cannot produce a 31-byte name without one — its window accepts 30 characters and the bounded copy pads
    /// what is left of the memset frame — so a field with no NUL means the sender is not the 7.3 client, and
    /// refusing it keeps the reader from ever looking past the field. This is the same rule as the sibling
    /// reader <see cref="GameCompetePackets.TryReadRequest"/>.
    /// </summary>
    public static bool TryReadCreateInstance(ReadOnlySpan<byte> packet,
        out HuntaholicCreateInstanceRequest request)
    {
        request = default;
        if (packet.Length != CreateInstanceLength)
        {
            return false;
        }

        var nameField = packet.Slice(NameOffset, NameFieldLength);
        if (nameField.IndexOf((byte)0) < 0)
        {
            return false;
        }

        var passwordField = packet.Slice(PasswordOffset, PasswordFieldLength);
        var passwordLength = passwordField.IndexOf((byte)0);
        if (passwordLength < 0)
        {
            return false;
        }

        request = new HuntaholicCreateInstanceRequest(ReadFixedString(nameField),
            (sbyte)packet[MaxMemberCountOffset], passwordLength);
        return true;
    }

    /// <summary>
    /// Decodes a fixed-size NUL padded string field. Only the bytes before the first NUL are read, so the
    /// padding — and anything a caller left after it — never reaches the string.
    /// </summary>
    private static string ReadFixedString(ReadOnlySpan<byte> field)
    {
        var terminator = field.IndexOf((byte)0);
        return Encoding.ASCII.GetString(terminator < 0 ? field : field.Slice(0, terminator));
    }
}
