using System.Collections.Frozen;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Extensions.Options;
using Navislamia.Configuration.Options;
using Navislamia.Game.DataAccess.Repositories.Interfaces;
using Navislamia.Game.Services.Buffs;
using Serilog;

namespace Navislamia.Game.Services.MonsterSkills;

/// <summary>What a monster skill does, the families this server models.</summary>
public enum MonsterSkillEffect
{
    Unsupported,
    /// <summary><c>EF_PHYSICAL_SINGLE_DAMAGE_T1</c> (101): <c>attack + var0 + var1 × lvl</c>.</summary>
    PhysicalFlat,

    /// <summary><c>EF_PHYSICAL_SINGLE_DAMAGE</c> (30001), the player formula.</summary>
    PhysicalScaled,

    /// <summary><c>EF_MAGIC_SINGLE_DAMAGE_T1_OLD</c> (201): <c>magic + var0 + var1 × lvl</c>.</summary>
    MagicFlat,

    /// <summary><c>EF_MAGIC_SINGLE_DAMAGE</c> (231), the player formula.</summary>
    MagicScaled,

    /// <summary><c>EF_ADD_STATE</c> (301) and <c>EF_ADD_REGION_STATE</c> (302).</summary>
    State,

    /// <summary><c>EF_ADD_HP</c> (501), only ever cast by a monster on itself.</summary>
    Heal
}

/// <summary>
/// One skill a monster may roll: the skill's fields, the level it casts at, its probability per attack
/// opportunity and whether it lands on the monster itself rather than on its target.
/// </summary>
public sealed record MonsterSkill(CastableBuffFields Fields, MonsterSkillEffect Effect, int Level,
    double Probability, bool OnSelf);

public interface IMonsterSkillCatalog
{
    /// <summary>The skills of <c>monster_skill_link_id</c>, in the order the AI rolls them; empty if none.</summary>
    IReadOnlyList<MonsterSkill> Get(int linkId);

    int Count { get; }
    IReadOnlyList<MonsterTriggerOptions> GetTriggers(int linkId) => System.Array.Empty<MonsterTriggerOptions>();
}

/// <summary>
/// <c>MonsterSkillResource</c> (<c>monster-skills.73.json</c>) joined to the <c>SkillResources</c> rows it
/// names, frozen at startup. Unsupported entries keep a placeholder so Lua indices stay stable.
/// </summary>
public class MonsterSkillCatalog : IMonsterSkillCatalog
{
    private const int PhysicalSingleDamageT1 = 101;
    private const int MagicSingleDamageT1Old = 201;
    private const int MagicSingleDamage = 231;
    private const int AddState = 301;
    private const int AddRegionState = 302;
    private const int AddHp = 501;
    private const int PhysicalSingleDamage = 30001;

    private static readonly IReadOnlyList<MonsterSkill> None = System.Array.Empty<MonsterSkill>();

    private readonly ILogger _logger = Log.ForContext<MonsterSkillCatalog>();
    private readonly FrozenDictionary<int, MonsterSkill[]> _links;
    private readonly FrozenDictionary<int, MonsterTriggerOptions[]> _triggers;

    public MonsterSkillCatalog(IOptions<MonsterSkillOptions> options, ISkillResourceRepository skills)
    {
        var links = options.Value.Links ?? new Dictionary<int, List<MonsterSkillEntryOptions>>();
        _triggers = (options.Value.Triggers ?? new()).ToFrozenDictionary(p => p.Key, p => p.Value.ToArray());
        var ids = links.Values.SelectMany(entries => entries).Select(entry => entry.SkillId).ToHashSet();
        var rows = new Dictionary<int, CastableSkillRow>();
        foreach (var row in skills.GetSkillRows(ids))
        {
            rows[row.SkillId] = row;
        }

        var built = new Dictionary<int, MonsterSkill[]>();
        var dropped = 0;
        var total = 0;
        foreach (var (linkId, entries) in links)
        {
            var resolved = new List<MonsterSkill>();
            foreach (var entry in entries)
            {
                total++;
                if (rows.TryGetValue(entry.SkillId, out var row)
                    && TryClassify(row, entry.Level, entry.Probability, out var skill))
                {
                    resolved.Add(skill);
                }
                else
                {
                    dropped++;
                    // Lua indexes the original list. Unsupported entries must not shift later slots.
                    resolved.Add(new MonsterSkill(new CastableBuffFields { SkillId = entry.SkillId },
                        MonsterSkillEffect.Unsupported, entry.Level, entry.Probability, false));
                }
            }

            if (resolved.Count > 0)
            {
                built[linkId] = resolved.ToArray();
            }
        }

        _links = built.ToFrozenDictionary();
        _logger.Information(
            "Loaded monster skills for {links} links ({kept} of {total} entries; {dropped} not modelled)",
            _links.Count, total - dropped, total, dropped);
    }

    public int Count => _links.Count;

    public IReadOnlyList<MonsterSkill> Get(int linkId) =>
        linkId != 0 && _links.TryGetValue(linkId, out var skills) ? skills : None;

    public IReadOnlyList<MonsterTriggerOptions> GetTriggers(int linkId) =>
        _triggers.TryGetValue(linkId, out var triggers) ? triggers : System.Array.Empty<MonsterTriggerOptions>();

    /// <summary>
    /// Sorts a skill into a modelled family. The official AI casts a skill on the monster itself when one
    /// flag of the skill is clear (<c>AI_processAttack</c> tests <c>SkillBase+0xc</c>), read here as
    /// <c>is_harmful</c>: the only column whose values fit the data (every heal and self-buff is clear,
    /// every attack set).
    /// </summary>
    public static bool TryClassify(CastableSkillRow row, int level, double probability, out MonsterSkill skill)
    {
        skill = null;
        if (probability < 0 || !double.IsFinite(probability) || level <= 0)
        {
            return false;
        }

        var onSelf = !row.IsHarmful;
        MonsterSkillEffect effect;
        SkillCastKind kind;
        switch (row.EffectType)
        {
            case PhysicalSingleDamageT1 when !onSelf:
                (effect, kind) = (MonsterSkillEffect.PhysicalFlat, SkillCastKind.PhysicalAttack);
                break;
            case PhysicalSingleDamage when !onSelf:
                (effect, kind) = (MonsterSkillEffect.PhysicalScaled, SkillCastKind.PhysicalAttack);
                break;
            case MagicSingleDamageT1Old when !onSelf:
                (effect, kind) = (MonsterSkillEffect.MagicFlat, SkillCastKind.MagicAttack);
                break;
            case MagicSingleDamage when !onSelf:
                (effect, kind) = (MonsterSkillEffect.MagicScaled, SkillCastKind.MagicAttack);
                break;
            case AddState or AddRegionState when row.StateId is > 0:
                (effect, kind) = (MonsterSkillEffect.State, onSelf ? SkillCastKind.Buff : SkillCastKind.Debuff);
                break;
            case AddHp when onSelf:
                (effect, kind) = (MonsterSkillEffect.Heal, SkillCastKind.Heal);
                break;
            default:
                if (onSelf || !SkillAreaRules.IsSupportedDamage(row.EffectType)) return false;
                (effect, kind) = SkillAreaRules.IsMagical(row.EffectType)
                    ? (MonsterSkillEffect.MagicScaled, SkillCastKind.MagicAttack)
                    : (MonsterSkillEffect.PhysicalScaled, SkillCastKind.PhysicalAttack);
                break;
        }

        var fields = new CastableBuffFields(row.SkillId, kind, row.StateId ?? 0, row.ToggleGroup, row.Vars,
            row.StateSecond, row.StateSecondPerLevel, row.StateLevelBase, row.StateLevelPerSkill, row.CostMp,
            row.CostMpPerSkl, row.DelayCast, row.DelayCastPerSkl, row.DelayCommon, row.DelayCooltime,
            row.DelayCooltimePerSkl, row.RequiredLevel, row.HitBonus, row.Percentage, row.CriticalBonus,
            row.CriticalBonusPerSkl, row.EffectType, row.Target, row.RequiredTarget, row.CastRange,
            row.ProbabilityOnHit, row.ProbabilityIncBySlv, IsHarmful: row.IsHarmful, HateMod: row.HateMod,
            HateBasic: row.HateBasic, HatePerSkl: row.HatePerSkl);
        skill = new MonsterSkill(fields, effect, level, probability, onSelf);
        return true;
    }
}
