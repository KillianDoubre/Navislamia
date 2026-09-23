using System;
using System.Text;
using Navislamia.Game.Network.Packets.Enums;

namespace Navislamia.Game.Network.Packets.Game;

/// <summary>
/// The client to server half of the player competition family, <c>TM_CS_COMPETE_REQUEST</c> (4500) and
/// <c>TM_CS_COMPETE_ANSWER</c> (4502).
///
/// Both ids are unconditional in rzu (<c>X(&lt;id&gt;, true)</c>, which expands to <c>if(true) id = id_;</c>),
/// so the seven ids of the family are the same in Epic 7.3 as in every other version and no field is gated.
/// 4500 is 39 bytes (header 7 + <c>compete_type</c> at 7 + a fixed 31-byte name at 8) and 4502 is 9 bytes
/// (header 7 + <c>compete_type</c> at 7 + <c>answer_type</c> at 8). The 31-byte width is confirmed twice by the
/// client itself: the 4-byte handle of 4504 is read at offset 39 and the second name of 4506 at offset 40.
///
/// Only these two frames are built here: the five remaining ids (4501, 4503, 4504, 4505, 4506) travel server
/// to client and belong to later lots. See docs/packet-specs/socle-competition-joueurs.md.
/// </summary>
public static class GameCompetePackets
{
    private const int HeaderSize = 7;

    /// <summary>Offset of <c>compete_type</c> (<c>int8</c>) in both client frames.</summary>
    public const int CompeteTypeOffset = HeaderSize;

    /// <summary>Offset of <c>answer_type</c> (<c>int8</c>) in <c>TM_CS_COMPETE_ANSWER</c>.</summary>
    public const int AnswerTypeOffset = HeaderSize + 1;

    /// <summary>Offset of the <c>requestee</c> name in <c>TM_CS_COMPETE_REQUEST</c>.</summary>
    public const int RequesteeOffset = HeaderSize + 1;

    /// <summary>
    /// Width of every name in this family: 30 characters plus the NUL. The client reads and writes them as C
    /// strings inside a fixed 31-byte buffer, and its own readers place the following field right after them.
    /// </summary>
    public const int NameLength = 31;

    /// <summary>Total size of TM_CS_COMPETE_REQUEST (4500), header and NUL included: 7 + 1 + 31.</summary>
    public const int RequestLength = HeaderSize + 1 + NameLength;

    /// <summary>Total size of TM_CS_COMPETE_ANSWER (4502): 7 + 1 + 1.</summary>
    public const int AnswerLength = HeaderSize + 2;

    /// <summary>
    /// Refusal code sent for a well formed 4500. The client 7.3 knows a message box for exactly these codes on
    /// a 4500: 65 (box 1636) and 26, 61, 63, 64, 67, 68, 1, 2 (box 1633). Every other code is displayed
    /// silently, so a code outside that list would leave the player with nothing at all.
    ///
    /// <c>NotInCompetablePlace</c> (64, box 1633) is the one that presupposes nothing about the target: this
    /// socle recognises no competable place and no duel at all, hence no invitation can be accepted. The exact
    /// code is Killian's arbitration (NON ÉTABLI (h) of the sheet); 66, which has no box for a 4500, is
    /// deliberately avoided.
    /// </summary>
    public const ResultCode RequestRefusalCode = ResultCode.NotInCompetablePlace;

    /// <summary>
    /// Refusal code sent for a well formed 4502. The client 7.3 displays 61 and 65 (box 1636) and 62, 64, 68
    /// and 1 (box 1633) for a 4502.
    ///
    /// <c>NotInCompete</c> (62, box 1633) states exactly what is true here: the answering player takes part in
    /// no competition. It is used for every well formed answer, including one whose <c>answer_type</c> is
    /// outside the three observed values, so that no code of unknown meaning (1, 2, 26) is ever sent.
    /// </summary>
    public const ResultCode AnswerRefusalCode = ResultCode.NotInCompete;

    /// <summary>
    /// TM_CS_COMPETE_REQUEST (4500): <c>compete_type</c> at 7, the target's name at 8. The client designates
    /// its target by the name displayed in its window, never by a handle.
    /// </summary>
    public readonly record struct CompeteRequest(sbyte CompeteType, string Requestee);

    /// <summary>
    /// TM_CS_COMPETE_ANSWER (4502): <c>compete_type</c> at 7, <c>answer_type</c> at 8. The observed values of
    /// <c>answer_type</c> are 0 (the <c>battle_start</c> control), 1 (the <c>battle_reject</c> control) and 2
    /// (the default branch of the message dispatch); their meaning is not established.
    /// </summary>
    public readonly record struct CompeteAnswer(sbyte CompeteType, sbyte AnswerType);

    /// <summary>
    /// The three values of <c>answer_type</c> the 7.3 client is known to emit. This is the observed set, not a
    /// declared enumeration: nothing in rzu, NGemity or the client defines the domain of the field, so a value
    /// outside it is only logged and never interpreted.
    /// </summary>
    public static bool IsObservedAnswerType(sbyte answerType)
    {
        return answerType is 0 or 1 or 2;
    }

    /// <summary>
    /// Reads TM_CS_COMPETE_REQUEST (4500). Only the exact 39-byte form is accepted, and the name must be
    /// terminated by a NUL inside its 31 bytes: a 30-character name without one would spill the client's
    /// fixed-size copy over the byte that follows, so the request is refused rather than guessed.
    /// <c>compete_type</c> is not validated — the client's own reader copies it without testing it.
    /// </summary>
    public static bool TryReadRequest(ReadOnlySpan<byte> packet, out CompeteRequest request)
    {
        request = default;
        if (packet.Length != RequestLength)
        {
            return false;
        }

        var nameField = packet.Slice(RequesteeOffset, NameLength);
        var terminator = nameField.IndexOf((byte)0);
        if (terminator < 0)
        {
            return false;
        }

        request = new CompeteRequest((sbyte)packet[CompeteTypeOffset],
            Encoding.ASCII.GetString(nameField.Slice(0, terminator)));
        return true;
    }

    /// <summary>
    /// Reads TM_CS_COMPETE_ANSWER (4502). Only the exact 9-byte form is accepted: the client writes the length
    /// in hard, so a shorter or padded frame is malformed rather than a shorter request.
    /// </summary>
    public static bool TryReadAnswer(ReadOnlySpan<byte> packet, out CompeteAnswer answer)
    {
        if (packet.Length != AnswerLength)
        {
            answer = default;
            return false;
        }

        answer = new CompeteAnswer((sbyte)packet[CompeteTypeOffset], (sbyte)packet[AnswerTypeOffset]);
        return true;
    }
}
