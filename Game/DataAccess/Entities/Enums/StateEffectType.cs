namespace Navislamia.Game.DataAccess.Entities.Enums;

/// <summary>
/// <c>StateResource.effect_type</c>.
/// </summary>
/// <remarks>
/// This is NOT <see cref="SkillEffectType"/>. The two columns share a name and nothing else:
/// <c>SkillResource.effect_type</c> is the 301/701/10001 space, while this one starts at 0 and describes
/// how a state's <c>value_*</c> triplets apply. Reading one against the other is a documented trap; this
/// property used to be typed <c>SkillEffectType</c>, which is what made it look wrong.
/// <para>Names and numbers come from the reference emulator's <c>StateBaseEffect</c> enum.</para>
/// </remarks>
public enum StateEffectType
{
    Misc = 0,
    ParameterInc = 1,
    ParameterAmp = 2,
    ParameterIncWhenEquipShield = 3,
    ParameterAmpWhenEquipShield = 4,
    ParameterIncWhenEquip = 5,
    ParameterAmpWhenEquip = 6,
    DoubleAttack = 21,
    AdditionalDamageOnAttack = 22,
    AmpAdditionalDamageOnAttack = 23
}
