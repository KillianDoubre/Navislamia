using System;
using System.Linq;
using Navislamia.Game.DataAccess.Repositories.Interfaces;
using Navislamia.Game.Network.Clients;
using Navislamia.Game.Network.Packets;
using Navislamia.Game.Network.Packets.Enums;
using Navislamia.Game.Network.Packets.Game;
using Navislamia.Game.Services.Rates;
using Serilog;

namespace Navislamia.Game.Services;

public class LevelingService : ILevelingService
{
    private readonly ILogger _logger = Log.ForContext<LevelingService>();
    private readonly ILevelResourceRepository _repository;
    private readonly IStatService _statService;
    private readonly IRateService _rates;

    private long[] _cumulativeExp;
    private int[] _jobJpCost;

    /// <summary>The four job depths' costs from the catalogue; null when it is absent (depth 0 then reads the database).</summary>
    private readonly long[][] _jobJpCostByDepth;
    private int _maxLevel;

    public LevelingService(ILevelResourceRepository repository, IStatService statService, IRateService rates,
        Microsoft.Extensions.Options.IOptionsMonitor<Navislamia.Configuration.Options.GameRuleOptions> rules = null,
        Microsoft.Extensions.Options.IOptions<Navislamia.Configuration.Options.JobLevelCostOptions> jobLevelCosts = null,
        ReturnPoints.IReturnPointService returnPoints = null)
    {
        _returnPoints = returnPoints;
        _rules = rules;
        var depths = jobLevelCosts?.Value?.Depths;
        _jobJpCostByDepth = depths is { Length: > 0 } ? depths : null;
        _rates = rates;
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
        // The reference broadcasts the new level to the region (NGemity Player.cpp:1459, BroadcastLevelMsg).
        client.SendToSelfAndObservers(GameCharacterPackets.BuildLevelUpdate(handle, newLevel, info.CharacterJobLevel));
        client.Connection.Send(GameStatPackets.BuildStatInfo(handle, stats, StatInfoType.Total));
        client.Connection.Send(GameStatPackets.BuildStatInfo(handle, result.ByItem, StatInfoType.ByItem));
        client.SendVitalProperty(GameStatPackets.BuildProperty(handle, "max_hp", maxHp));
        client.SendVitalProperty(GameStatPackets.BuildProperty(handle, "hp", maxHp));
        client.SendVitalProperty(GameStatPackets.BuildProperty(handle, "max_mp", maxMp));
        client.SendVitalProperty(GameStatPackets.BuildProperty(handle, "mp", maxMp));

        // StructPlayer::onExpChange runs on_player_level_up once, with the level reached.
        _returnPoints?.OnLevelUp(client, newLevel);
    }

    private readonly ReturnPoints.IReturnPointService _returnPoints;

    public long ApplyDeathPenalty(GameClient client)
    {
        // The kill rewards credit experience under the same lock: a penalty must not interleave with them.
        lock (client.ConnectionInfo.ProgressLock)
        {
            return DeathPenalty(client);
        }
    }

    private long DeathPenalty(GameClient client)
    {
        var info = client.ConnectionInfo;
        var penalty = Math.Min(MoralityRules.DeathExperience(LevelCurve.DeathPenalty(_cumulativeExp, _maxLevel, info.CharacterLevel,
            _rules?.CurrentValue?.PkServer == true), info.ImmoralPoint), info.CharacterExp);
        if (penalty <= 0)
        {
            return 0;
        }

        info.CharacterExp -= penalty;
        info.DeathExpLoss = penalty;
        var handle = info.CharacterHandle;
        client.Connection.Send(GameCharacterPackets.BuildExpUpdate(handle, info.CharacterExp, info.CharacterJp));

        var level = LevelCurve.Resolve(_cumulativeExp, _maxLevel, info.CharacterExp, 1);
        if (level < info.CharacterLevel)
        {
            // A level lost: the stats follow it down. The character stays dead (0 HP); its maxima shrink.
            info.CharacterLevel = level;
            var result = _statService.Compute(info);
            var maxHp = (int)result.Total.MaxHp;
            var maxMp = (int)result.Total.MaxMp;
            info.CharacterMaxHp = maxHp;
            info.CharacterMp = Math.Min(info.CharacterMp, maxMp);

            client.SendToSelfAndObservers(GameCharacterPackets.BuildLevelUpdate(handle, level, info.CharacterJobLevel));
            client.Connection.Send(GameStatPackets.BuildStatInfo(handle, result.Total, StatInfoType.Total));
            client.Connection.Send(GameStatPackets.BuildStatInfo(handle, result.ByItem, StatInfoType.ByItem));
            client.SendVitalProperty(GameStatPackets.BuildProperty(handle, "max_hp", maxHp));
            client.SendVitalProperty(GameStatPackets.BuildProperty(handle, "max_mp", maxMp));
            client.SendVitalProperty(GameStatPackets.BuildProperty(handle, "mp", info.CharacterMp));
        }

        _logger.Debug("{clientTag} lost {penalty} exp on death (level {level})", client.ClientTag, penalty,
            info.CharacterLevel);
        return penalty;
    }

    private readonly Microsoft.Extensions.Options.IOptionsMonitor<Navislamia.Configuration.Options.GameRuleOptions> _rules;

    public long RestoreDeathExperience(GameClient client, decimal ratio)
    {
        lock (client.ConnectionInfo.ProgressLock)
        {
            return RestoreExperience(client, ratio);
        }
    }

    private long RestoreExperience(GameClient client, decimal ratio)
    {
        var info = client.ConnectionInfo;
        var amount = ratio <= 0m ? 0L : (long)Math.Floor(info.DeathExpLoss * ratio);
        info.DeathExpLoss = 0;
        if (amount <= 0)
        {
            return 0;
        }

        info.CharacterExp += amount;
        ApplyExperience(client);
        client.Connection.Send(GameCharacterPackets.BuildExpUpdate(info.CharacterHandle, info.CharacterExp,
            info.CharacterJp));
        return amount;
    }

    public int MaxLevel => _cumulativeExp == null ? 0 : _maxLevel;

    public bool TryGetExperienceFor(int level, out long exp) =>
        LevelCurve.TryGetExperienceFor(_cumulativeExp, _maxLevel, level, out exp);

    public bool TryGetNextJobLevelCost(int currentJobLevel, out long cost) =>
        TryGetNextJobLevelCost(0, currentJobLevel, out cost);

    public bool TryGetNextJobLevelCost(int jobDepth, int currentJobLevel, out long cost)
    {
        cost = 0;
        long baseCost;
        if (_jobJpCostByDepth != null)
        {
            baseCost = jobDepth >= 0 && jobDepth < _jobJpCostByDepth.Length
                ? JobLevelCurve.NextCost(_jobJpCostByDepth[jobDepth], currentJobLevel)
                : 0;
        }
        else
        {
            baseCost = jobDepth == 0 && _jobJpCost != null ? JobLevelCurve.NextCost(_jobJpCost, currentJobLevel) : 0;
        }

        if (baseCost <= 0)
        {
            return false;
        }

        cost = RateMath.ScaleCost(baseCost, _rates.JobLevelJpCost);
        return true;
    }

    public void ApplyJobLevelUp(GameClient client, uint targetHandle)
    {
        const ushort requestId = (ushort)GamePackets.TM_CS_JOB_LEVEL_UP;
        var target = unchecked((int)targetHandle);

        if (_jobJpCost == null && _jobJpCostByDepth == null)
        {
            client.SendResult(requestId, (ushort)ResultCode.NotActable, target);
            return;
        }

        var info = client.ConnectionInfo;
        var current = info.CharacterJobLevel < 1 ? 1 : info.CharacterJobLevel;
        // A base cost of 0 is the capped tier; the rate is applied after, so a rate of 0 is free, not capped.
        // The tier is the job depth: a first job levels on jp_1, not on the base job's jp_0.
        if (!TryGetNextJobLevelCost(info.PreviousJobs.Count, current, out var cost))
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
