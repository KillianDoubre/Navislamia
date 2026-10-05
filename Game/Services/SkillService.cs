using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Navislamia.Game.Network.Clients;
using Navislamia.Game.Network.Packets;
using Navislamia.Game.Network.Packets.Enums;
using Navislamia.Game.Network.Packets.Game;
using Navislamia.Game.Services.Rates;
using Serilog;

namespace Navislamia.Game.Services;

public class SkillService : ISkillService
{
    private const ushort RequestId = (ushort)GamePackets.TM_CS_LEARN_SKILL;
    private readonly ILogger _logger = Log.ForContext<SkillService>();
    private readonly SkillCatalog _catalog;
    private readonly ICharacterService _characterService;
    private readonly IStatService _statService;
    private readonly IRateService _rates;
    private readonly Creatures.ICreatureService _creatures;

    public SkillService(SkillCatalog catalog, ICharacterService characterService, IStatService statService,
        IRateService rates, Creatures.ICreatureService creatures = null)
    {
        _creatures = creatures;
        _rates = rates;
        _catalog = catalog;
        _characterService = characterService;
        _statService = statService;

        if (_catalog.JobCount == 0)
        {
            _logger.Warning("The skill catalog is empty; skill learning will be unavailable");
        }
    }

    public async Task LearnAsync(GameClient client, GameActionPackets.LearnSkillRequest request)
    {
        var info = client.ConnectionInfo;
        if (request.Handle != info.CharacterHandle || request.Handle == 0)
        {
            client.SendResult(RequestId, (ushort)ResultCode.NotOwn, request.SkillId);
            return;
        }

        var currentLevel = info.LearnedSkills.GetValueOrDefault(request.SkillId);
        var evaluation = _catalog.EvaluateAcrossJobs(info.PreviousJobs, info.CharacterJob, info.CharacterLevel,
            info.CharacterJobLevel, request.SkillId, currentLevel, request.TargetLevel, info.LearnedSkills, info.CharacterJp,
            _rates.SkillJpCost, availableTp: info.CharacterTalentPoint);
        if (!evaluation.IsSuccess)
        {
            client.SendResult(RequestId, (ushort)evaluation.Result, request.SkillId);
            return;
        }

        var remainingJp = info.CharacterJp - evaluation.Cost;
        // A talent skill spends talent points instead (StructCreature::RegisterSkill), saved with the level.
        var remainingTp = info.CharacterTalentPoint - evaluation.TalentCost;
        try
        {
            var saved = evaluation.TalentCost > 0
                ? await _characterService.SaveLearnedSkillAsync(info.CharacterName, request.SkillId, request.TargetLevel,
                    remainingJp, remainingTp)
                : await _characterService.SaveLearnedSkillAsync(info.CharacterName, request.SkillId,
                    request.TargetLevel, remainingJp);
            if (!saved)
            {
                client.SendResult(RequestId, (ushort)ResultCode.DBError, request.SkillId);
                return;
            }
        }
        catch (Exception exception)
        {
            _logger.Error(exception, "Could not persist skill {skillId} level {level} for {clientTag}",
                request.SkillId, request.TargetLevel, client.ClientTag);
            client.SendResult(RequestId, (ushort)ResultCode.DBError, request.SkillId);
            return;
        }

        info.CharacterJp = remainingJp;
        info.LearnedSkills[request.SkillId] = request.TargetLevel;
        if (evaluation.TalentCost > 0)
        {
            // SetTalentPoint broadcasts the property.
            info.CharacterTalentPoint = remainingTp;
            client.Connection.Send(GameStatPackets.BuildProperty(info.CharacterHandle, "tp", remainingTp));
        }

        client.Connection.Send(GameCharacterPackets.BuildExpUpdate(info.CharacterHandle, info.CharacterExp,
            info.CharacterJp));
        client.Connection.Send(GameCharacterPackets.BuildSkillList(info.CharacterHandle,
            new[] { new KeyValuePair<int, byte>(request.SkillId, request.TargetLevel) }));
        client.SendResult(RequestId, (ushort)ResultCode.Success, request.SkillId);

        SendRefreshedStats(client, info);
        // setSummonUpdate: a master passive aimed at the summons (10031/10032) moves their stats too.
        _creatures?.RefreshSummonStats(client);
    }

    private void SendRefreshedStats(GameClient client, ConnectionInfo info)
    {
        _statService.RefreshPassives(info);
        var stats = _statService.Compute(info);
        var handle = info.CharacterHandle;

        client.Connection.Send(GameStatPackets.BuildStatInfo(handle, stats.Total, StatInfoType.Total));
        client.Connection.Send(GameStatPackets.BuildStatInfo(handle, stats.ByItem, StatInfoType.ByItem));
        client.Connection.Send(GameStatPackets.BuildProperty(handle, "max_hp", (int)stats.Total.MaxHp));
        client.Connection.Send(GameStatPackets.BuildProperty(handle, "max_mp", (int)stats.Total.MaxMp));
    }
}
