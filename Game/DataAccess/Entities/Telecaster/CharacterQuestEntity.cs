namespace Navislamia.Game.DataAccess.Entities.Telecaster;

/// <summary>
/// One quest a character is currently carrying, as the two 7.3 frames impose it: the 61-byte
/// <c>TS_QUEST_INFO</c> element of <c>TM_SC_QUEST_LIST</c> (600) plus the code <c>TM_CS_DROP_QUEST</c>
/// (603) erases.
/// <para>
/// The wire fields are <c>uint32</c>; they are stored as signed columns holding the same bits (the
/// packet layer reinterprets them with <c>unchecked</c>). <see cref="Code"/> is signed on purpose: it
/// is the <c>int32_t</c> the client sends, and a negative one is refused rather than folded into a
/// large unsigned code. The six <see cref="Status"/> slots are opaque — the 7.3 client copies them
/// without a readable semantic, and only the first three exist in the NGemity model.
/// See <c>docs/packet-specs/socle-quetes.md</c> §3.4, §4 and §5.6.
/// </para>
/// </summary>
public class CharacterQuestEntity : Entity
{
    public long CharacterId { get; set; }

    public virtual CharacterEntity Character { get; set; }

    /// <summary>Quest identifier; signed, exactly as <c>TS_CS_DROP_QUEST.code</c> carries it.</summary>
    public int Code { get; set; }

    /// <summary><c>startID</c> of the quest instance.</summary>
    public int StartId { get; set; }

    /// <summary>Six <c>uint32</c> values, in wire order.</summary>
    public int[] Value { get; set; }

    /// <summary>Six <c>uint32</c> status words, in wire order; opaque in 7.3.</summary>
    public int[] Status { get; set; }

    /// <summary>The single <c>progress</c> byte of the 600 element.</summary>
    public byte Progress { get; set; }

    /// <summary><c>timeLimit</c> (<c>ar_time_t</c>, a <c>uint32</c> on the wire).</summary>
    public int TimeLimit { get; set; }
}
