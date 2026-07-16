namespace Navislamia.Game.Services.Stats;

public class StatBlock
{
    public int StatId { get; set; }

    public float Strength { get; set; }
    public float Vitality { get; set; }
    public float Dexterity { get; set; }
    public float Agility { get; set; }
    public float Intelligence { get; set; }
    public float Wisdom { get; set; }
    public float Luck { get; set; }

    public float Critical { get; set; }
    public float CriticalPower { get; set; }
    public float AttackPointRight { get; set; }
    public float AttackPointLeft { get; set; }
    public float Defence { get; set; }
    public float BlockDefence { get; set; }
    public float MagicPoint { get; set; }
    public float MagicDefence { get; set; }
    public float AccuracyRight { get; set; }
    public float AccuracyLeft { get; set; }
    public float MagicAccuracy { get; set; }
    public float Avoid { get; set; }
    public float MagicAvoid { get; set; }
    public float BlockChance { get; set; }
    public float MoveSpeed { get; set; }
    public float AttackSpeed { get; set; }
    public float AttackRange { get; set; }
    public float MaxWeight { get; set; }
    public float CastingSpeed { get; set; }
    public float CoolTimeSpeed { get; set; }
    public float ItemChance { get; set; }
    public float HpRegenPercentage { get; set; }
    public float HpRegenPoint { get; set; }
    public float MpRegenPercentage { get; set; }
    public float MpRegenPoint { get; set; }
    public float PerfectBlock { get; set; }
    public float MagicalDefIgnore { get; set; }
    public float MagicalDefIgnoreRatio { get; set; }
    public float PhysicalDefIgnore { get; set; }
    public float PhysicalDefIgnoreRatio { get; set; }
    public float MagicalPenetration { get; set; }
    public float MagicalPenetrationRatio { get; set; }
    public float PhysicalPenetration { get; set; }
    public float PhysicalPenetrationRatio { get; set; }

    public float MaxHp { get; set; }
    public float MaxMp { get; set; }
    public float MaxStamina { get; set; }
    public float MaxChaos { get; set; }

    public void Add(StatTarget target, float value)
    {
        switch (target)
        {
            case StatTarget.Strength: Strength += value; break;
            case StatTarget.Vitality: Vitality += value; break;
            case StatTarget.Dexterity: Dexterity += value; break;
            case StatTarget.Agility: Agility += value; break;
            case StatTarget.Intelligence: Intelligence += value; break;
            case StatTarget.Wisdom: Wisdom += value; break;
            case StatTarget.Luck: Luck += value; break;
            case StatTarget.AttackPointRight: AttackPointRight += value; break;
            case StatTarget.AttackPointLeft: AttackPointLeft += value; break;
            case StatTarget.MagicPoint: MagicPoint += value; break;
            case StatTarget.Defence: Defence += value; break;
            case StatTarget.MagicDefence: MagicDefence += value; break;
            case StatTarget.BlockDefence: BlockDefence += value; break;
            case StatTarget.BlockChance: BlockChance += value; break;
            case StatTarget.AccuracyRight: AccuracyRight += value; break;
            case StatTarget.AccuracyLeft: AccuracyLeft += value; break;
            case StatTarget.MagicAccuracy: MagicAccuracy += value; break;
            case StatTarget.Avoid: Avoid += value; break;
            case StatTarget.MagicAvoid: MagicAvoid += value; break;
            case StatTarget.Critical: Critical += value; break;
            case StatTarget.CriticalPower: CriticalPower += value; break;
            case StatTarget.AttackSpeed: AttackSpeed += value; break;
            case StatTarget.CastingSpeed: CastingSpeed += value; break;
            case StatTarget.CoolTimeSpeed: CoolTimeSpeed += value; break;
            case StatTarget.MoveSpeed: MoveSpeed += value; break;
            case StatTarget.AttackRange: AttackRange += value; break;
            case StatTarget.MaxWeight: MaxWeight += value; break;
            case StatTarget.ItemChance: ItemChance += value; break;
            case StatTarget.HpRegenPoint: HpRegenPoint += value; break;
            case StatTarget.HpRegenPercentage: HpRegenPercentage += value; break;
            case StatTarget.MpRegenPoint: MpRegenPoint += value; break;
            case StatTarget.MpRegenPercentage: MpRegenPercentage += value; break;
            case StatTarget.MaxHp: MaxHp += value; break;
            case StatTarget.MaxMp: MaxMp += value; break;
            case StatTarget.MaxStamina: MaxStamina += value; break;
            case StatTarget.MaxChaos: MaxChaos += value; break;
        }
    }

    public void Amplify(StatTarget target, float ratio)
    {
        Add(target, Get(target) * ratio);
    }

    public float Get(StatTarget target) => target switch
    {
        StatTarget.Strength => Strength,
        StatTarget.Vitality => Vitality,
        StatTarget.Dexterity => Dexterity,
        StatTarget.Agility => Agility,
        StatTarget.Intelligence => Intelligence,
        StatTarget.Wisdom => Wisdom,
        StatTarget.Luck => Luck,
        StatTarget.AttackPointRight => AttackPointRight,
        StatTarget.AttackPointLeft => AttackPointLeft,
        StatTarget.MagicPoint => MagicPoint,
        StatTarget.Defence => Defence,
        StatTarget.MagicDefence => MagicDefence,
        StatTarget.BlockDefence => BlockDefence,
        StatTarget.BlockChance => BlockChance,
        StatTarget.AccuracyRight => AccuracyRight,
        StatTarget.AccuracyLeft => AccuracyLeft,
        StatTarget.MagicAccuracy => MagicAccuracy,
        StatTarget.Avoid => Avoid,
        StatTarget.MagicAvoid => MagicAvoid,
        StatTarget.Critical => Critical,
        StatTarget.CriticalPower => CriticalPower,
        StatTarget.AttackSpeed => AttackSpeed,
        StatTarget.CastingSpeed => CastingSpeed,
        StatTarget.CoolTimeSpeed => CoolTimeSpeed,
        StatTarget.MoveSpeed => MoveSpeed,
        StatTarget.AttackRange => AttackRange,
        StatTarget.MaxWeight => MaxWeight,
        StatTarget.ItemChance => ItemChance,
        StatTarget.HpRegenPoint => HpRegenPoint,
        StatTarget.HpRegenPercentage => HpRegenPercentage,
        StatTarget.MpRegenPoint => MpRegenPoint,
        StatTarget.MpRegenPercentage => MpRegenPercentage,
        StatTarget.MaxHp => MaxHp,
        StatTarget.MaxMp => MaxMp,
        StatTarget.MaxStamina => MaxStamina,
        StatTarget.MaxChaos => MaxChaos,
        _ => 0f
    };
}
