namespace Navislamia.Game.DataAccess.Entities.Arcadia;

public class JobLevelBonusEntity : Entity
{
    public decimal[] Strength { get; set; }
    public decimal[] Vitality { get; set; }
    public decimal[] Dexterity { get; set; }
    public decimal[] Agility { get; set; }
    public decimal[] Intelligence { get; set; }
    public decimal[] Wisdom { get; set; }
    public decimal[] Luck { get; set; }

    public decimal DefaultStrength { get; set; }
    public decimal DefaultVitality { get; set; }
    public decimal DefaultDexterity { get; set; }
    public decimal DefaultAgility { get; set; }
    public decimal DefaultIntelligence { get; set; }
    public decimal DefaultWisdom { get; set; }
    public decimal DefaultLuck { get; set; }
}
