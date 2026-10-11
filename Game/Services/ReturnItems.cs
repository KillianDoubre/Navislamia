namespace Navislamia.Game.Services;

/// <summary>
/// The return items of <c>StructPlayer::UseItem</c> (StructPlayer.cpp:3836-3852): the return feathers are not erased by
/// the use but by their spell, once it fires (<c>StructSkill::RETURN_FEATHER</c>, <c>RETURN_BACK_FEATHER</c>), so a cast
/// that is cancelled or interrupted keeps the feather. The Return Scroll (603001) is not in the list: it is spent at the
/// use, like any consumable. <c>ItemBase.h:48-53</c>.
/// </summary>
public static class ReturnItems
{
    public const int ReturnFeather = 910006;
    public const int ReturnFeatherSecretDungeon = 910080;
    public const int ReturnBackFeather = 910009;
    public const int EventReturnFeather = 2902104;
    public const int EventReturnFeather2 = 2012099;
    public const int RuppiReturnFeather = 9000098;

    public static bool IsSpentWhenCast(int itemCode) => itemCode is ReturnFeather or ReturnFeatherSecretDungeon
        or ReturnBackFeather or EventReturnFeather or EventReturnFeather2 or RuppiReturnFeather;
}
