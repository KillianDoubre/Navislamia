namespace Navislamia.Game.Services;

/// <summary>
/// The official hate amounts (docs/packet-specs/socle-haine.md): a swing is worth its damage
/// (<c>StructCreature::Attack</c>, modifiers at 1), a skill its <c>SkillBase::GetHatePoint</c>
/// (2012-11 <c>0x140202f70</c>).
/// </summary>
public static class HateRules
{
    /// <summary>
    /// <c>GetHatePoint(level, amount)</c>: <c>hate_mod = 0</c> gives nothing, a negative <c>hate_mod</c>
    /// <c>hate_basic + hate_per_skl × level</c>, a positive one <c>amount × hate_mod + hate_basic</c>, where the
    /// amount is the damage dealt or the HP healed. The enhancement term is zero.
    /// </summary>
    public static int SkillHate(decimal hateMod, int hateBasic, decimal hatePerLevel, int skillLevel, int amount)
    {
        if (hateMod == 0m)
        {
            return 0;
        }

        return hateMod < 0m
            ? (int)(hateBasic + hatePerLevel * skillLevel)
            : (int)(amount * hateMod + hateBasic);
    }
}
