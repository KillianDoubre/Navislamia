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
    /// <summary>Persisted countdown for time_limit_type 1; offline time does not consume it.</summary>
    public double RemainingSeconds { get; set; }
    /// <summary>UTC deadline for time_limit_type 2.</summary>
    public System.DateTime? ExpiresAt { get; set; }
    public long CharacterId { get; set; }

    public virtual CharacterEntity Character { get; set; }

    /// <summary>Quest identifier; signed, exactly as <c>TS_CS_DROP_QUEST.code</c> carries it.</summary>
    public int Code { get; set; }

    /// <summary>NPC start text id passed as the second argument of start_quest.</summary>
    public int StartId { get; set; }

    /// <summary>Six wire slots; random contracts store three target/count pairs here.</summary>
    public int[] Value { get; set; }

    /// <summary>Six objective counters in wire order, interpreted according to the resource type.</summary>
    public int[] Status { get; set; }

    /// <summary>The single <c>progress</c> byte of the 600 element.</summary>
    public byte Progress { get; set; }

    /// <summary><c>timeLimit</c> (<c>ar_time_t</c>, a <c>uint32</c> on the wire).</summary>
    public int TimeLimit { get; set; }
}
