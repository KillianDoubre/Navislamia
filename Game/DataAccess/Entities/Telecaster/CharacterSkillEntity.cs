namespace Navislamia.Game.DataAccess.Entities.Telecaster;

public class CharacterSkillEntity : Entity
{
    public long CharacterId { get; set; }
    public virtual CharacterEntity Character { get; set; }
    public int SkillId { get; set; }
    public byte Level { get; set; }
}
