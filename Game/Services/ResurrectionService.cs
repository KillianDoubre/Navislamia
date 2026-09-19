using System;
using Navislamia.Game.Network.Clients;
using Navislamia.Game.Network.Packets;
using Navislamia.Game.Network.Packets.Enums;
using Navislamia.Game.Network.Packets.Game;
using Navislamia.Game.Services.Interfaces;
using Serilog;

namespace Navislamia.Game.Services;

/// <summary>
/// Handles <c>TM_CS_RESURRECTION</c> (513): a player character at 0 HP is brought back at its return
/// point. The death itself is carried by the hit that lands (<c>target_hp = 0</c>) and by the
/// <c>hp</c> property — this version has no server-side death packet (§3.2 of the specification).
/// </summary>
/// <remarks>
/// The return point is the position persisted with the character at world entry, kept in
/// <see cref="ConnectionInfo"/> by <c>GameActions.OnLogin</c>: the specification's option (a) of §16.1,
/// which needs neither a new column nor a migration. A character's persisted position is only written
/// when progress is saved, so the in-memory value is the stored one for the whole session.
/// </remarks>
public class ResurrectionService : IResurrectionService
{
    private readonly ILogger _logger = Log.ForContext<ResurrectionService>();
    private readonly IWarpService _warpService;
    private readonly IStatService _statService;

    public ResurrectionService(IWarpService warpService, IStatService statService)
    {
        _warpService = warpService;
        _statService = statService;
    }

    public void Resurrect(GameClient client, GameActionPackets.ResurrectionRequest request)
    {
        const ushort requestId = (ushort)GamePackets.TM_CS_RESURRECTION;
        var info = client.ConnectionInfo;

        var result = ResurrectionRules.CheckRequest(request.Handle, request.Type, info.CharacterHandle,
            info.CharacterHp);
        if (result != ResultCode.Success)
        {
            client.SendResult(requestId, (ushort)result);
            return;
        }

        try
        {
            var stats = _statService.Compute(info).Total;
            var (hp, mp) = ResurrectionRules.RestoredVitals(stats.MaxHp, stats.MaxMp);

            // Restore the vitals before moving: hp > 0 is the whole of the alive state, and the monster
            // AI drops a target at 0 HP, so nothing can hit the character on the way.
            info.CharacterHp = hp;
            info.CharacterMp = mp;

            // The return point carries its own layer: the character may have died on another layer of
            // the same map. Warp stops the attack, drops the aggro, leaves every visible object and
            // re-streams the surroundings around the new position.
            info.Layer = info.RespawnLayer;
            _warpService.Warp(client, info.RespawnX, info.RespawnY);

            client.Connection.Send(GameStatPackets.BuildProperty(info.CharacterHandle, "hp", hp));
            client.Connection.Send(GameStatPackets.BuildProperty(info.CharacterHandle, "mp", mp));

            // Which of these three frames closes the client's death window is not established
            // (§15.2): the recommendation is to send the result, the warp and the vitals together.
            client.SendResult(requestId, (ushort)ResultCode.Success);

            _logger.Debug("{clientTag} resurrected at ({x}, {y}) with {hp} hp", client.ClientTag,
                info.RespawnX, info.RespawnY, hp);
        }
        catch (Exception exception)
        {
            _logger.Error(exception, "Could not resurrect {clientTag}", client.ClientTag);
        }
    }
}
