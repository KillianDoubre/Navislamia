using System.Collections.Generic;
using Navislamia.Game.DataAccess.Entities.Enums;
using Navislamia.Game.DataAccess.Repositories.Interfaces;
using Serilog;

namespace Navislamia.Game.Services;

public class StatService : IStatService
{
    private readonly ILogger _logger = Log.ForContext<StatService>();
    private readonly IStatResourceRepository _statResources;

    private static readonly Dictionary<int, int> RaceStatId = new()
    {
        { (int)Race.Deva, 1 },
        { (int)Race.Gaia, 2 },
        { (int)Race.Asura, 3 },
    };

    public StatService(IStatResourceRepository statResources)
    {
        _statResources = statResources;
    }

    public CharacterStats Compute(int race, int level)
    {
        if (level < 1) level = 1;

        var statId = RaceStatId.TryGetValue(race, out var id) ? id : 0;
        var row = statId > 0 ? _statResources.GetById(statId) : null;

        short str, vit, dex, agi, intel, men, luk;
        if (row != null)
        {
            str = (short)row.Strength; vit = (short)row.Vitality; dex = (short)row.Dexterity;
            agi = (short)row.Agility; intel = (short)row.Intelligence; men = (short)row.Wisdom;
            luk = (short)row.Luck;
            _logger.Debug("StatService: race {race} lv {lv} using StatResource row {id}", race, level, statId);
        }
        else
        {
            (str, vit, dex, agi, intel, men, luk) = StaticBaseline(race);
            _logger.Warning("StatService: race {race} lv {lv} StatResource row {id} missing, using static baseline",
                race, level, statId);
        }

        var maxHp = 50 + vit * 10 + level * 20;
        var maxMp = 30 + men * 8 + level * 10;
        return new CharacterStats(statId, str, vit, dex, agi, intel, men, luk, maxHp, maxMp);
    }

    private static (short, short, short, short, short, short, short) StaticBaseline(int race) => race switch
    {
        (int)Race.Deva => (10, 11, 11, 12, 13, 13, 10),
        (int)Race.Gaia => (13, 13, 11, 11, 10, 11, 10),
        (int)Race.Asura => (12, 11, 13, 13, 10, 10, 10),
        _ => (11, 11, 11, 11, 11, 11, 10),
    };
}
