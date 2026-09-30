using Navislamia.Game.DataAccess.Entities.Enums;
using Navislamia.Game.Network.Packets;

namespace Navislamia.Game.Services;

/// <summary>
/// The rules an attempt to tame a monster is judged by. Ported from NGemity's <c>Skill::PrepareTaming</c>
/// (<c>Chihiro/src/Skills/Skill.cpp:1611-1638</c>), whose order is kept: a living monster, a monster that
/// can be tamed at all, one that nobody is already taming, full health, a free card of the required code,
/// then a tamer who is not already busy.
/// </summary>
/// <remarks>
/// <para>
/// Nothing here touches the database, the world or the wire: the caller supplies what it observed. That is
/// why the class has no caller yet — the monster's tamer and the player's taming target are étape 1 of the
/// socle (<c>docs/packet-specs/socle-apprivoisement-invocation.md</c> §11), and the repository cannot yet
/// look a card up by code (its storage repository loads an item by handle or by character only, §5.4).
/// </para>
/// <para>
/// The refusals are the ones the reference answers <c>TS_RESULT_NOT_ACTABLE</c> with, except for the health
/// check; <see cref="ResultCodeOf"/> is the precise code table the socle decided
/// (fiche §6). <b>étape 0 does not emit those codes</b>: whether the 7.3 client renders 90-93 at all is
/// <c>NON ÉTABLI</c> (fiche §7 point 3), and the cast path still answers <c>NOT_ACTABLE</c> (5), exactly
/// like the reference does for those four cases.
/// </para>
/// </remarks>
public static class TamingRules
{
    /// <summary>
    /// NGemity's <c>FlagBits::ITEM_FLAG_SUMMON = 0x80000000</c> (<c>ItemTemplate.hpp:176</c>) — the bit a
    /// card carries once it is bound to a creature. The same mask the inventory record's <c>flag</c> field
    /// holds, so the shared constant is the one of the drop path.
    /// </summary>
    public const uint SummonCardMask = GroundItemDropRules.SummonFlagMask;

    /// <summary>
    /// NGemity's <c>FlagBits::ITEM_FLAG_TAMING = 0x20000000</c> (<c>ItemTemplate.hpp:174</c>) — the bit a
    /// card carries while a taming attempt it was picked for is running.
    /// </summary>
    /// <remarks>
    /// Careful: the repository's <see cref="ItemFlag.Taming"/> member holds the bit <em>index</em> 29, not
    /// that mask. A stored flag is read as the retail bitset, so write <c>(ItemFlag)0x20000000u</c> and
    /// never the bare enum member — the same trap <see cref="GroundItemDropRules.IsBoundSummonCard"/>
    /// documents for <see cref="ItemFlag.Summon"/>.
    /// </remarks>
    public const uint TamingCardMask = 0x20000000u;

    /// <summary>
    /// Whether a monster can be tamed at all: <c>tf_monster_resource.taming_id</c> is the card the
    /// attempt needs, and zero means there is none (<c>Monster::GetTameItemCode</c>).
    /// </summary>
    public static bool IsTamable(int tamingId)
    {
        return tamingId != 0;
    }

    /// <summary>
    /// Whether the monster is at full health. <c>Skill::PrepareTaming</c> refuses when
    /// <c>GetHealth() != GetMaxHealth()</c>: a wounded monster cannot be tamed at all, which is why the
    /// check cannot be skipped.
    /// </summary>
    public static bool HasFullHp(int currentHp, int maxHp)
    {
        return currentHp == maxHp;
    }

    /// <summary>
    /// Whether a card is bound to a creature, i.e. whether it carries
    /// <see cref="SummonCardMask"/>. A bound card can neither be dropped nor used for a new attempt.
    /// </summary>
    public static bool IsBoundSummonCard(ItemFlag flag)
    {
        return flag != ItemFlag.None && (unchecked((uint)flag) & SummonCardMask) != 0;
    }

    /// <summary>
    /// Whether a card is being used by a running attempt, i.e. whether it carries
    /// <see cref="TamingCardMask"/>.
    /// </summary>
    public static bool IsTamingCard(ItemFlag flag)
    {
        return flag != ItemFlag.None && (unchecked((uint)flag) & TamingCardMask) != 0;
    }

    /// <summary>
    /// The refusal of an attempt, or <see cref="TamingRefusal.None"/> when the attempt may run. The order
    /// of the checks is the reference's (<c>Skill.cpp:1611-1638</c>); the card is looked at before the
    /// tamer's own state, so a missing card wins over an already busy tamer.
    /// </summary>
    public static TamingRefusal Resolve(in TamingAttempt attempt)
    {
        if (!attempt.TargetIsLivingMonster)
        {
            return TamingRefusal.NotActable;
        }

        if (!IsTamable(attempt.MonsterTamingId))
        {
            return TamingRefusal.NotTamable;
        }

        if (attempt.MonsterHasTamer)
        {
            return TamingRefusal.TargetAlreadyBeingTamed;
        }

        if (!HasFullHp(attempt.MonsterHp, attempt.MonsterMaxHp))
        {
            return TamingRefusal.NotEnoughTargetHp;
        }

        if (!attempt.HasFreeCard)
        {
            return TamingRefusal.NotEnoughSummonCard;
        }

        return attempt.TamerBusy ? TamingRefusal.AlreadyTaming : TamingRefusal.None;
    }

    /// <summary>
    /// The <see cref="ResultCode"/> each refusal was decided to carry (fiche §6). The codes exist in
    /// <c>ResultCode.cs:104-113</c> and this table is what joins them to a decision; it is deliberately
    /// not what the cast path sends yet (see the class remarks).
    /// </summary>
    public static ResultCode ResultCodeOf(TamingRefusal refusal)
    {
        return refusal switch
        {
            TamingRefusal.None => ResultCode.Success,
            TamingRefusal.NotActable => ResultCode.NotActable,
            TamingRefusal.NotTamable => ResultCode.NotTamable,
            TamingRefusal.TargetAlreadyBeingTamed => ResultCode.TargetAlreadyBeingTamed,
            TamingRefusal.NotEnoughTargetHp => ResultCode.NotEnoughTargetHP,
            TamingRefusal.NotEnoughSummonCard => ResultCode.NotEnoughSummonCard,
            _ => ResultCode.AlreadyTaming
        };
    }
}

/// <summary>
/// Why a taming attempt was refused. The names follow the reference's own checks
/// (<c>Skill::PrepareTaming</c>) rather than the result codes, because four of them share
/// <c>TS_RESULT_NOT_ACTABLE</c> in NGemity while the socle gave each its own code (fiche §6).
/// </summary>
public enum TamingRefusal
{
    /// <summary>The attempt may run.</summary>
    None = 0,

    /// <summary>
    /// No living monster at the handle — which the cast path answers <c>NotExist</c> or
    /// <c>NotActable</c> with before this class is reached. The reference's generic refusal.
    /// </summary>
    NotActable,

    /// <summary>The monster cannot be tamed at all: <c>taming_id == 0</c> (<c>ResultCode.NotTamable</c>, 90).</summary>
    NotTamable,

    /// <summary>Somebody is already taming that monster (<c>ResultCode.TargetAlreadyBeingTamed</c>, 91).</summary>
    TargetAlreadyBeingTamed,

    /// <summary>The monster is wounded: only a monster at full health can be tamed (<c>ResultCode.NotEnoughTargetHP</c>, 92).</summary>
    NotEnoughTargetHp,

    /// <summary>No free card of the monster's <c>taming_id</c> sits in the bag (<c>ResultCode.NotEnoughSummonCard</c>, 93).</summary>
    NotEnoughSummonCard,

    /// <summary>The player is already taming another monster (<c>ResultCode.AlreadyTaming</c>, 70).</summary>
    AlreadyTaming
}

/// <summary>
/// What a taming attempt observed, so that <see cref="TamingRules.Resolve"/> can judge it without touching
/// the world.
/// </summary>
/// <param name="TargetIsLivingMonster">
/// The resolved target: a monster that is alive. A player or a prop is not a taming target.
/// </param>
/// <param name="MonsterTamingId">
/// The monster's <c>tf_monster_resource.taming_id</c>, i.e. the card the attempt needs; 0 means the monster
/// cannot be tamed. Carried by <see cref="MonsterInstance.TamingId"/>.
/// </param>
/// <param name="MonsterHasTamer">Whether the monster already remembers a tamer (<c>Monster::GetTamer</c>).</param>
/// <param name="MonsterHp">The monster's current health, <see cref="MonsterWorldState.GetHp"/>.</param>
/// <param name="MonsterMaxHp">The monster's maximum health, <see cref="MonsterInstance.Hp"/>.</param>
/// <param name="HasFreeCard">
/// Whether the bag holds the required card <em>without</em> the bound bit: <c>FindItem(code,
/// ITEM_FLAG_SUMMON, false)</c> returns the first card whose <c>flag</c> carries no
/// <see cref="TamingRules.SummonCardMask"/>.
/// </param>
/// <param name="TamerBusy">Whether the caster is already taming another monster.</param>
public readonly record struct TamingAttempt(
    bool TargetIsLivingMonster,
    int MonsterTamingId,
    bool MonsterHasTamer,
    int MonsterHp,
    int MonsterMaxHp,
    bool HasFreeCard,
    bool TamerBusy);
