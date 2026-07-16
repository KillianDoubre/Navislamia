using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Navislamia.Game.Network.Clients;
using Navislamia.Game.Network.Packets;
using Navislamia.Game.Network.Packets.Enums;
using Navislamia.Game.Network.Packets.Game;
using Serilog;

namespace Navislamia.Game.Services;

public class SkillService : ISkillService
{
    private const ushort RequestId = (ushort)GamePackets.TM_CS_LEARN_SKILL;
    private readonly ILogger _logger = Log.ForContext<SkillService>();
    private readonly SkillCatalog _catalog;
    private readonly ICharacterService _characterService;
    private readonly IStatService _statService;

    public SkillService(SkillCatalog catalog, ICharacterService characterService, IStatService statService)
    {
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
        var evaluation = _catalog.Evaluate(info.CharacterJob, info.CharacterLevel, info.CharacterJobLevel,
            request.SkillId, currentLevel, request.TargetLevel, info.LearnedSkills, info.CharacterJp);
        if (!evaluation.IsSuccess)
        {
            client.SendResult(RequestId, (ushort)evaluation.Result, request.SkillId);
            return;
        }

        var remainingJp = info.CharacterJp - evaluation.Cost;
        try
        {
            if (!await _characterService.SaveLearnedSkillAsync(info.CharacterName, request.SkillId,
                    request.TargetLevel, remainingJp))
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

        client.Connection.Send(GameCharacterPackets.BuildExpUpdate(info.CharacterHandle, info.CharacterExp,
            info.CharacterJp));
        client.Connection.Send(GameCharacterPackets.BuildSkillList(info.CharacterHandle,
            new[] { new KeyValuePair<int, byte>(request.SkillId, request.TargetLevel) }));
        client.SendResult(RequestId, (ushort)ResultCode.Success, request.SkillId);

        SendRefreshedStats(client, info);
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
