namespace Navislamia.Game.DataAccess.Entities.Telecaster;

/// <summary>
/// A skill a summon learned (<c>DB_InsertSkill</c> with the summon's sid as owner): one row per summon and skill,
/// the level raised in place.
/// </summary>
public class SummonSkillEntity : Entity
{
    public long SummonId { get; set; }
    public virtual SummonEntity Summon { get; set; }
    public int SkillId { get; set; }
    public byte Level { get; set; }
}
