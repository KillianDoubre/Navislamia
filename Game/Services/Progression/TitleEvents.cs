using System;
using System.Collections.Generic;
using System.Linq;

namespace Navislamia.Game.Services.Progression;

/// <summary>
/// The title condition updates of the official <c>StructTitleManager</c> that come from an event, as a function
/// from a condition type to its increment (null: the event does not touch it). <c>UpdateRelatedTitleCondition</c>
/// matches the three values exactly; a set type takes the increment as its value
/// (docs/packet-specs/socle-titres-secondaires-evenements.md).
/// </summary>
public static class TitleEvents
{
    public const int SummonTameByCode = 3001;
    public const int SummonTameByRate = 3002;
    public const int SummonEquipByCode = 3101;
    public const int SummonEquipByRate = 3102;
    public const int SummonMountByCode = 3301;
    public const int SummonCardGetByCode = 3901;
    public const int SummonCardGetByRate = 3902;
    public const int ItemUseByCode = 2201;
    public const int ItemMixByCode = 2401;

    private static bool Exactly(TitleConditionType type, int category, int value0, int value1 = 0, int value2 = 0) =>
        type.Category == category && type.Values.Length >= 3 && type.Values[0] == value0 && type.Values[1] == value1
        && type.Values[2] == value2;

    /// <summary><c>UpdateTitleConditionBySummonTame</c>: one taming attempt, its summon code, rate and outcome.</summary>
    public static Func<TitleConditionType, long?> SummonTame(int summonCode, int rate, bool success) => type =>
        Exactly(type, SummonTameByCode, summonCode, success ? 1 : 0) || Exactly(type, SummonTameByRate, rate, success ? 1 : 0)
            ? 1 : null;

    /// <summary><c>UpdateTitleConditionByItemUse</c>.</summary>
    public static Func<TitleConditionType, long?> ItemUsed(int itemCode) => type =>
        Exactly(type, ItemUseByCode, itemCode) ? 1 : null;

    /// <summary><c>UpdateTitleConditionByItemCreateByMixing</c>: each item a mix made, with its count.</summary>
    public static Func<TitleConditionType, long?> ItemsMixed(IReadOnlyList<(int Code, long Count)> made) => type =>
    {
        long? total = null;
        foreach (var (code, count) in made)
        {
            if (Exactly(type, ItemMixByCode, code))
            {
                total = (total ?? 0) + count;
            }
        }

        return total;
    };

    /// <summary>One creature card of the session, as the state-driven conditions read it.</summary>
    public readonly record struct Card(int SummonCode, int Rate, int Enhance, bool Tamed, bool Formed, bool Ridden);

    /// <summary>
    /// The summon conditions the official keeps in step with the state (all <c>skip_db_update</c>): its formation
    /// counts (<c>UpdateTitleConditionBySummonEquip</c>, +1 per formed card at least at the enhancement asked), the
    /// ridden summon (<c>…BySummonMount</c>) and the cards held (<c>…BySummonCardGet</c>). Null for any other type.
    /// </summary>
    public static long? FromCards(TitleConditionType type, IReadOnlyCollection<Card> cards)
    {
        var values = type.Values;
        if (values is null || values.Length < 3)
        {
            return null;
        }

        return type.Category switch
        {
            SummonEquipByCode => cards.Count(card => card.Formed && card.SummonCode == values[0] && values[1] <= card.Enhance),
            SummonEquipByRate => cards.Count(card => card.Formed && card.Rate == values[0] && values[1] <= card.Enhance),
            SummonMountByCode => cards.Any(card => card.Ridden && card.SummonCode == values[0]) == (values[1] != 0) && values[2] == 0
                ? 1 : 0,
            SummonCardGetByCode => cards.Any(card => card.SummonCode == values[0] && card.Tamed == (values[1] != 0)) ? 1 : 0,
            SummonCardGetByRate => cards.Any(card => card.SummonCode != 0 && card.Rate == values[0] && card.Tamed == (values[1] != 0))
                ? 1 : 0,
            _ => null
        };
    }
}
