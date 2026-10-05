using System;
using System.Collections.Frozen;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Microsoft.Extensions.Options;
using Navislamia.Configuration.Options;
using Navislamia.Game.Services.Stats;

namespace Navislamia.Game.Services.Creatures;

/// <summary>What the official <c>GameContent</c> knows of a summon resource (<c>SummonResource</c> + its stat row).</summary>
public sealed record SummonResourceInfo(int Id, string Name, int Type, int Rate, int Form, int CardId, int RunSpeed,
    float AttackRange, float Size, float Scale, StatBaseStats? BaseStats, int EvolveTarget = 0,
    float[] LevelBonus = null, int RidingSpeed = 0, bool IsRidingOnly = false, int RidingKind = 0, int NameId = 0);

/// <summary>A <c>CreatureEnhance</c> row: what a card's enhance level gives its summon.</summary>
public sealed record CreatureEnhanceInfo(int Level, float StatAmplify, int CardDurability, int SlotAmount, int JpAddition);

public interface ICreatureCatalog
{
    bool TryGetSummon(int summonId, out SummonResourceInfo summon);

    /// <summary>Whether an item resource is a creature card (the <c>card_id</c> of some summon).</summary>
    bool IsCard(int itemResourceId);

    IReadOnlyCollection<int> CardIds { get; }

    /// <summary>The server-side name of a tamable monster, quoted in the <c>TAMING_*|name|</c> party lines.</summary>
    string MonsterName(int monsterId);

    /// <summary>The lowest form among the summons a card holds (three forms share one card), null for none.</summary>
    SummonResourceInfo FirstSummonForCard(int cardCode);

    /// <summary><c>GameContent::GetSummonName()</c>: one prefix and one postfix, drawn at random.</summary>
    string RandomName(Func<int, int> next);

    /// <summary>
    /// <c>GameContent::GetNeedSummonExp(level)</c>: the cumulative exp a summon of <paramref name="level"/> must reach to
    /// pass it (the <c>normal_exp</c> column, read for every form); 0 past the table, which stops the level loop.
    /// </summary>
    long NeedExp(int level);

    /// <summary><c>GameContent::GetCreatureEnhanceInfo</c>: the row of a card enhance level (the last one past the table).</summary>
    CreatureEnhanceInfo Enhance(int level);
}

/// <summary>The creature catalogue, frozen at startup like every other catalogue.</summary>
public sealed class CreatureCatalog : ICreatureCatalog
{
    private readonly FrozenDictionary<int, SummonResourceInfo> _summons;
    private readonly FrozenSet<int> _cards;
    private readonly FrozenDictionary<int, string> _monsterNames;
    private readonly string[] _prefixes;
    private readonly string[] _postfixes;
    private readonly long[] _summonExp;
    private readonly CreatureEnhanceInfo[] _enhance;

    public CreatureCatalog(IOptions<CreatureCatalogOptions> options)
    {
        var value = options?.Value ?? new CreatureCatalogOptions();
        _summons = value.Summons.ToFrozenDictionary(s => s.Id, s => new SummonResourceInfo(s.Id, s.Name ?? string.Empty,
            s.Type, s.Rate, s.Form, s.CardId, s.RunSpeed, s.AttackRange, s.Size, s.Scale,
            s.Stats is { Length: 7 } st
                ? new StatBaseStats(s.StatId, st[0], st[1], st[2], st[3], st[4], st[5], st[6])
                : null, s.EvolveTarget, s.LevelBonus is { Length: 7 } ? s.LevelBonus : null, s.RidingSpeed,
            s.IsRidingOnly, s.RidingKind, s.NameId));
        _cards = value.Summons.Where(s => s.CardId != 0).Select(s => s.CardId).ToFrozenSet();
        _monsterNames = value.TamableMonsterNames
            .Where(pair => int.TryParse(pair.Key, NumberStyles.Integer, CultureInfo.InvariantCulture, out _))
            .ToFrozenDictionary(pair => int.Parse(pair.Key, CultureInfo.InvariantCulture), pair => pair.Value ?? string.Empty);
        _prefixes = value.NamePrefixes.ToArray();
        _postfixes = value.NamePostfixes.ToArray();
        _summonExp = value.SummonExp.ToArray();
        _enhance = value.Enhance.OrderBy(e => e.Level)
            .Select(e => new CreatureEnhanceInfo(e.Level, e.StatAmplify, e.CardDurability, e.SlotAmount, e.JpAddition))
            .ToArray();
    }

    public long NeedExp(int level) => level >= 1 && level <= _summonExp.Length ? _summonExp[level - 1] : 0;

    public CreatureEnhanceInfo Enhance(int level) => _enhance.Length == 0
        ? new CreatureEnhanceInfo(0, 0, 0, 2, 0)
        : _enhance[Math.Clamp(level, 0, _enhance.Length - 1)];

    public IReadOnlyCollection<int> CardIds => _cards;

    public bool TryGetSummon(int summonId, out SummonResourceInfo summon) => _summons.TryGetValue(summonId, out summon);

    public bool IsCard(int itemResourceId) => _cards.Contains(itemResourceId);

    public string MonsterName(int monsterId) => _monsterNames.GetValueOrDefault(monsterId, string.Empty);

    public SummonResourceInfo FirstSummonForCard(int cardCode) =>
        _summons.Values.Where(s => s.CardId == cardCode && cardCode != 0).OrderBy(s => s.Form).ThenBy(s => s.Id)
            .FirstOrDefault();

    public string RandomName(Func<int, int> next)
    {
        if (_prefixes.Length == 0 || _postfixes.Length == 0)
        {
            return "Creature";
        }

        return CreatureRules.TrimName(_prefixes[next(_prefixes.Length)] + _postfixes[next(_postfixes.Length)]);
    }
}
