namespace Navislamia.Game.DataAccess.Entities.Arcadia;

/// <summary>
/// Imported quest definition. The schema mirror is extended with the imported Gold column;
/// QuestRules and QuestService interpret native objectives and the six reward choices.
/// The Lua script columns are preserved but are not executed.
/// </summary>
public class QuestResourceEntity
{
    public int Id { get; set; }

    public int TextIdQuest { get; set; }

    public int TextIdSummary { get; set; }

    public int TextIdStatus { get; set; }

    public int LimitBeginTime { get; set; }

    public int LimitEndTime { get; set; }

    public int LimitLevel { get; set; }

    public int LimitJobLevel { get; set; }

    public int LimitMaxLevel { get; set; }

    public int LimitMaxJobLevel { get; set; }

    /// <summary>One of the seven race/class `char` flags of the schema. Read as the imported character flag, "1" for allowed.</summary>
    public string LimitDeva { get; set; }

    /// <summary>One of the seven race/class `char` flags of the schema. Read as the imported character flag, "1" for allowed.</summary>
    public string LimitAsura { get; set; }

    /// <summary>One of the seven race/class `char` flags of the schema. Read as the imported character flag, "1" for allowed.</summary>
    public string LimitGaia { get; set; }

    /// <summary>One of the seven race/class `char` flags of the schema. Read as the imported character flag, "1" for allowed.</summary>
    public string LimitFighter { get; set; }

    /// <summary>One of the seven race/class `char` flags of the schema. Read as the imported character flag, "1" for allowed.</summary>
    public string LimitHunter { get; set; }

    /// <summary>One of the seven race/class `char` flags of the schema. Read as the imported character flag, "1" for allowed.</summary>
    public string LimitMagician { get; set; }

    /// <summary>One of the seven race/class `char` flags of the schema. Read as the imported character flag, "1" for allowed.</summary>
    public string LimitSummoner { get; set; }

    public int LimitJob { get; set; }

    /// <summary>The column the 4.1.1 database calls <c>limit_quest_indication</c>; a different type and name in 7.3.</summary>
    public short LimitJobDepth { get; set; }

    public int LimitFavorGroupId { get; set; }

    public int LimitFavor { get; set; }

    /// <summary>
    /// Schema <c>char</c>. The reference folds it with <c>GetUInt8() == 1</c> (ObjectMgr.cpp:325), which is
    /// its own 4.1.1 read and not a 7.3 rule.
    /// </summary>
    public string Repeatable { get; set; }

    public int InvokeCondition { get; set; }

    public int InvokeValue { get; set; }

    /// <summary>Schema <c>char(10)</c>: a ten-character code, kept at its declared width.</summary>
    public string TimeLimitType { get; set; }

    public int TimeLimit { get; set; }

    /// <summary>The quest type; NGemity enumerates fourteen values in 4.1.1 (<c>QuestBase.h:29-45</c>), none of them proven for 7.3.</summary>
    public int Type { get; set; }

    public int Value1 { get; set; }

    public int Value2 { get; set; }

    public int Value3 { get; set; }

    public int Value4 { get; set; }

    public int Value5 { get; set; }

    public int Value6 { get; set; }

    public int Value7 { get; set; }

    public int Value8 { get; set; }

    public int Value9 { get; set; }

    public int Value10 { get; set; }

    public int Value11 { get; set; }

    public int Value12 { get; set; }

    public int DropGroupId { get; set; }

    public int QuestDifficulty { get; set; }

    public int FavorGroupId { get; set; }

    public int HateGroupId { get; set; }

    public int Favor { get; set; }

    /// <summary>Schema <c>bigint</c>, unlike every other numeric column of the table.</summary>
    public long Exp { get; set; }

    public int Jp { get; set; }

    /// <summary>Huntaholic point reward, separate from Gold.</summary>
    public int HolicPoint { get; set; }

    /// <summary>Gold reward in the imported 9.4 QuestResource catalogue.</summary>
    public long Gold { get; set; }

    /// <summary>Two-letter schema name kept as is; no reference reads this column.</summary>
    public int Ld { get; set; }

    public int DefaultRewardId { get; set; }

    public int DefaultRewardLevel { get; set; }

    public int DefaultRewardQuantity { get; set; }

    public int OptionalRewardId1 { get; set; }

    public int OptionalRewardLevel1 { get; set; }

    public int OptionalRewardQuantity1 { get; set; }

    public int OptionalRewardId2 { get; set; }

    public int OptionalRewardLevel2 { get; set; }

    public int OptionalRewardQuantity2 { get; set; }

    public int OptionalRewardId3 { get; set; }

    public int OptionalRewardLevel3 { get; set; }

    public int OptionalRewardQuantity3 { get; set; }

    /// <summary>Fourth optional reward slot; only 7.3 has it, NGemity stops at three (<c>QuestBase.h:21</c>).</summary>
    public int OptionalRewardId4 { get; set; }

    /// <summary>Fourth optional reward slot; only 7.3 has it, NGemity stops at three (<c>QuestBase.h:21</c>).</summary>
    public int OptionalRewardLevel4 { get; set; }

    /// <summary>Fourth optional reward slot; only 7.3 has it, NGemity stops at three (<c>QuestBase.h:21</c>).</summary>
    public int OptionalRewardQuantity4 { get; set; }

    /// <summary>Fifth optional reward slot; only 7.3 has it, NGemity stops at three (<c>QuestBase.h:21</c>).</summary>
    public int OptionalRewardId5 { get; set; }

    /// <summary>Fifth optional reward slot; only 7.3 has it, NGemity stops at three (<c>QuestBase.h:21</c>).</summary>
    public int OptionalRewardLevel5 { get; set; }

    /// <summary>Fifth optional reward slot; only 7.3 has it, NGemity stops at three (<c>QuestBase.h:21</c>).</summary>
    public int OptionalRewardQuantity5 { get; set; }

    /// <summary>Sixth optional reward slot; only 7.3 has it, NGemity stops at three (<c>QuestBase.h:21</c>).</summary>
    public int OptionalRewardId6 { get; set; }

    /// <summary>Sixth optional reward slot; only 7.3 has it, NGemity stops at three (<c>QuestBase.h:21</c>).</summary>
    public int OptionalRewardLevel6 { get; set; }

    /// <summary>Sixth optional reward slot; only 7.3 has it, NGemity stops at three (<c>QuestBase.h:21</c>).</summary>
    public int OptionalRewardQuantity6 { get; set; }

    public int ForeQuest1 { get; set; }

    public int ForeQuest2 { get; set; }

    public int ForeQuest3 { get; set; }

    /// <summary>
    /// Schema <c>char</c>. The reference reads it as <c>GetUInt8() != 0</c> (ObjectMgr.cpp:364) — a second,
    /// inconsistent reading of the same kind of column — so it says nothing about the 7.3 domain.
    /// </summary>
    public string OrFlag { get; set; }

    /// <summary>Schema <c>char</c>, defaulted to <c>0</c> by the table. No reference reads it.</summary>
    public string IsAutoQuest { get; set; }

    /// <summary>The accept script of the schema; never executed here — NavisLamia runs no Lua.</summary>
    public string? ScriptStartText { get; set; }

    /// <summary>The clear script of the schema; never executed here — NavisLamia runs no Lua.</summary>
    public string? ScriptEndText { get; set; }

    /// <summary>The drop script of the schema; never executed here — NavisLamia runs no Lua.</summary>
    public string? ScriptDropText { get; set; }

    public string? ShowTargetType { get; set; }

    public int? ShowTargetId { get; set; }

    public string? MarkHide { get; set; }

    public int CoolTime { get; set; }

    public int AcceptCoolTime { get; set; }
}
