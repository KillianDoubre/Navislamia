namespace Navislamia.Game.Services;

/// <summary>
/// The two gold ceilings of the official server (<c>CaptainHerlockServer.exe</c> 2012-11, see
/// docs/packet-specs/socle-entrepot-or.md §2). <c>StructPlayer::ChangeGold</c> refuses a carried balance
/// above <see cref="MaxCarried"/> and <c>StructPlayer::ChangeStorageGold</c> a stored balance above
/// <see cref="MaxStored"/>. NGemity's <c>MAX_GOLD_FOR_INVENTORY</c> (100 000 000 000) is the stored ceiling
/// applied to the carried gold too: the official server keeps the bag ten times lower.
/// </summary>
public static class GoldRules
{
    public const long MaxCarried = 10_000_000_000;

    public const long MaxStored = 100_000_000_000;

    /// <summary>Whether adding <paramref name="amount"/> to <paramref name="balance"/> stays within <paramref name="max"/>.</summary>
    public static bool Fits(long balance, long amount, long max) => amount >= 0 && balance <= max - amount;
}
