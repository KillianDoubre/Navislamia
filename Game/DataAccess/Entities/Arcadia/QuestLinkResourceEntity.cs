namespace Navislamia.Game.DataAccess.Entities.Arcadia;

/// <summary>
/// One NPC ↔ quest link, mirroring the Arcadia table <c>QuestLinkResource</c>
/// (<c>ArcadiaSchemaPSQL.sql:906-916</c>). This is the table that makes a quest reachable from a contact:
/// the reference installs the link on the NPC with <c>NPC::LinkQuest</c> (Chihiro/src/Entities/NPC/NPC.cpp:63)
/// and reads the whole table in <c>ObjectMgr::LoadQuestLinkResource</c> (ObjectMgr.cpp:454-483), which walks
/// the eight columns in exactly the schema's order.
/// <para>
/// The three <c>char</c> flags are declared <c>character varying(1)</c> and are carried as characters: the retail
/// dump writes <c>'1'</c>/<c>'0'</c> there (<c>Arcadia.sql:37098</c>,
/// <c>flag_start char(1) NOT NULL DEFAULT '0'</c>), while the reference folds them with
/// <c>GetUInt8() == 1</c> — an unsatisfiable test against a <c>'1'</c> character, and its own 4.1.1 read.
/// Which of the three flags a 7.3 server must honour, and what each <c>text_id_*</c> indexes, is not
/// settled here: the choice of the quests offered is game policy (docs/packet-specs/socle-cycle-quete.md
/// §5.5, lot b3).
/// </para>
/// <para>
/// The table declares no primary key (<c>ArcadiaSchemaPSQL.sql:906-916</c>), so <see cref="NpcId"/> and
/// <see cref="QuestId"/> together are declared as the key — the pair the schema's own data uses once per
/// row — because EF requires one. That uniqueness is a modelling choice, not a schema guarantee.
/// </para>
/// </summary>
public class QuestLinkResourceEntity
{
    public int NpcId { get; set; }

    public int QuestId { get; set; }

    /// <summary>Schema <c>char</c>; the quest may be started from this NPC.</summary>
    public string FlagStart { get; set; }

    /// <summary>Schema <c>char</c>; the NPC carries the in-progress text of this quest.</summary>
    public string FlagProgress { get; set; }

    /// <summary>Schema <c>char</c>; the quest may be ended from this NPC.</summary>
    public string FlagEnd { get; set; }

    public int TextIdStart { get; set; }

    public int TextIdInProgress { get; set; }

    public int TextIdEnd { get; set; }
}
