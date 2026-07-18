using System;
using System.Linq;
using Navislamia.Game.DataAccess.Repositories.Interfaces;
using Navislamia.Game.Network.Clients;
using Navislamia.Game.Network.Packets;
using Navislamia.Game.Network.Packets.Enums;
using Navislamia.Game.Network.Packets.Game;
using Serilog;

namespace Navislamia.Game.Services;

public class LevelingService : ILevelingService
{
    private readonly ILogger _logger = Log.ForContext<LevelingService>();
    private readonly ILevelResourceRepository _repository;
    private readonly IStatService _statService;

    private long[] _cumulativeExp;
    private int[] _jobJpCost;
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
        var result = _statService.Compute(info);
        var stats = result.Total;
        var maxHp = (int)stats.MaxHp;
        var maxMp = (int)stats.MaxMp;
        info.CharacterHp = maxHp;
        info.CharacterMaxHp = maxHp;
        info.CharacterMp = maxMp;

        var handle = info.CharacterHandle;
        client.Connection.Send(GameCharacterPackets.BuildLevelUpdate(handle, newLevel, info.CharacterJobLevel));
        client.Connection.Send(GameStatPackets.BuildStatInfo(handle, stats, StatInfoType.Total));
        client.Connection.Send(GameStatPackets.BuildStatInfo(handle, result.ByItem, StatInfoType.ByItem));
        client.Connection.Send(GameStatPackets.BuildProperty(handle, "max_hp", maxHp));
        client.Connection.Send(GameStatPackets.BuildProperty(handle, "hp", maxHp));
        client.Connection.Send(GameStatPackets.BuildProperty(handle, "max_mp", maxMp));
        client.Connection.Send(GameStatPackets.BuildProperty(handle, "mp", maxMp));
    }

    public void ApplyJobLevelUp(GameClient client, uint targetHandle)
    {
        const ushort requestId = (ushort)GamePackets.TM_CS_JOB_LEVEL_UP;
        var target = unchecked((int)targetHandle);

        if (_jobJpCost == null)
        {
            client.SendResult(requestId, (ushort)ResultCode.NotActable, target);
            return;
        }

        var info = client.ConnectionInfo;
        var current = info.CharacterJobLevel < 1 ? 1 : info.CharacterJobLevel;
        var cost = JobLevelCurve.NextCost(_jobJpCost, current);

        if (cost <= 0)
        {
            client.SendResult(requestId, (ushort)ResultCode.LimitMax, target);
            return;
        }

        if (info.CharacterJp < cost)
        {
            client.SendResult(requestId, (ushort)ResultCode.NotEnoughJP, target);
            return;
        }

        info.CharacterJp -= cost;
        info.CharacterJobLevel = current + 1;

        var handle = info.CharacterHandle;
        client.Connection.Send(GameCharacterPackets.BuildExpUpdate(handle, info.CharacterExp, info.CharacterJp));
        client.Connection.Send(GameStatPackets.BuildProperty(handle, "job_level", info.CharacterJobLevel));
        client.SendResult(requestId, (ushort)ResultCode.Success, target);

        SendStatRefresh(client, info, handle);
    }

    private void SendStatRefresh(GameClient client, ConnectionInfo info, uint handle)
    {
        var result = _statService.Compute(info);
        var maxHp = (int)result.Total.MaxHp;
        var maxMp = (int)result.Total.MaxMp;

        client.Connection.Send(GameStatPackets.BuildStatInfo(handle, result.Total, StatInfoType.Total));
        client.Connection.Send(GameStatPackets.BuildStatInfo(handle, result.ByItem, StatInfoType.ByItem));
        client.Connection.Send(GameStatPackets.BuildProperty(handle, "max_hp", maxHp));
        client.Connection.Send(GameStatPackets.BuildProperty(handle, "max_mp", maxMp));
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
            var jobJpCost = new int[maxLevel + 1];
            Array.Fill(cumulativeExp, long.MaxValue);

            foreach (var level in levels)
            {
                if (level.Level < 1 || level.Level > maxLevel)
                {
                    continue;
                }

                cumulativeExp[level.Level] = level.NormalExp;
                jobJpCost[level.Level] = level.JLvs is { Length: > 0 } ? level.JLvs[0] : 0;
            }

            _cumulativeExp = cumulativeExp;
            _jobJpCost = jobJpCost;
            _maxLevel = maxLevel;
            _logger.Information("Loaded {count} level thresholds (max level {max})", levels.Count, maxLevel);
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to load level thresholds; character leveling disabled");
        }
    }
}
