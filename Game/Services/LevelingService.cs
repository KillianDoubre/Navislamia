using System;
using System.Linq;
using Navislamia.Game.DataAccess.Repositories.Interfaces;
using Navislamia.Game.Network.Clients;
using Navislamia.Game.Network.Packets.Game;
using Serilog;

namespace Navislamia.Game.Services;

public class LevelingService : ILevelingService
{
    private readonly ILogger _logger = Log.ForContext<LevelingService>();
    private readonly ILevelResourceRepository _repository;
    private readonly IStatService _statService;

    private long[] _cumulativeExp;
    private int _maxLevel;

    public LevelingService(ILevelResourceRepository repository, IStatService statService)
    {
        _repository = repository;
        _statService = statService;
        Load();
    }

    public void ApplyExperience(GameClient client)
    {
        if (_cumulativeExp == null)
        {
            return;
        }

        var info = client.ConnectionInfo;
        var newLevel = LevelCurve.Resolve(_cumulativeExp, _maxLevel, info.CharacterExp, info.CharacterLevel);
        if (newLevel <= info.CharacterLevel)
        {
            return;
        }

        info.CharacterLevel = newLevel;
        var stats = _statService.Compute(info.CharacterRace, newLevel);
        info.CharacterHp = stats.MaxHp;

        var handle = info.CharacterHandle;
        client.Connection.Send(GameCharacterPackets.BuildLevelUpdate(handle, newLevel, info.CharacterJobLevel));
        client.Connection.Send(GameStatPackets.BuildStatInfo(handle, stats));
        client.Connection.Send(GameStatPackets.BuildProperty(handle, "max_hp", stats.MaxHp));
        client.Connection.Send(GameStatPackets.BuildProperty(handle, "hp", stats.MaxHp));
        client.Connection.Send(GameStatPackets.BuildProperty(handle, "max_mp", stats.MaxMp));
        client.Connection.Send(GameStatPackets.BuildProperty(handle, "mp", stats.MaxMp));
    }

    private void Load()
    {
        try
        {
            var levels = _repository.GetAll();
            if (levels.Count == 0)
            {
                _logger.Warning("No level thresholds loaded; character leveling disabled");
                return;
            }

            var maxLevel = levels.Max(level => level.Level);
            var cumulativeExp = new long[maxLevel + 1];
            Array.Fill(cumulativeExp, long.MaxValue);

            foreach (var level in levels)
            {
                if (level.Level >= 1 && level.Level <= maxLevel)
                {
                    cumulativeExp[level.Level] = level.NormalExp;
                }
            }

            _cumulativeExp = cumulativeExp;
            _maxLevel = maxLevel;
            _logger.Information("Loaded {count} level thresholds (max level {max})", levels.Count, maxLevel);
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to load level thresholds; character leveling disabled");
        }
    }
}
