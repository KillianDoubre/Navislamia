using System;
using System.Threading.Tasks;
using Navislamia.Game.Network.Clients;
using Navislamia.Game.Network.Packets.Game;
using Serilog;

namespace Navislamia.Game.Services.ReturnPoints;

public interface IReturnPointService
{
    /// <summary>Sets the session's return point and persists it.</summary>
    void Set(GameClient client, ReturnPoint point);

    /// <summary>
    /// A teleporter's <c>Binding_*</c> action: the return point it names, then the line its Lua's <c>message()</c>
    /// sends. False for any other function.
    /// </summary>
    bool TryBind(GameClient client, string function);

    /// <summary><c>on_player_level_up</c>, run once per level gain with the level reached.</summary>
    void OnLevelUp(GameClient client, int level);
}

/// <summary>
/// Keeps the return point of a session (<see cref="ConnectionInfo.RespawnX"/>/<see cref="ConnectionInfo.RespawnY"/>,
/// where <c>TM_CS_RESURRECTION</c> type 0 brings the character back) and writes it to the character's flags the
/// moment it changes. See docs/packet-specs/socle-point-de-retour.md.
/// </summary>
public class ReturnPointService : IReturnPointService
{
    /// <summary><c>CHAT_NPC</c>, what the Lua's <c>message()</c> sends, from <c>@SCRIPT</c>.</summary>
    private const byte ChatNpc = 40;

    private readonly ILogger _logger = Log.ForContext<ReturnPointService>();
    private readonly ICharacterService _characterService;
    private readonly Random _random;

    public ReturnPointService(ICharacterService characterService) : this(characterService, Random.Shared)
    {
    }

    public ReturnPointService(ICharacterService characterService, Random random)
    {
        _characterService = characterService;
        _random = random;
    }

    public void Set(GameClient client, ReturnPoint point)
    {
        var info = client.ConnectionInfo;
        info.RespawnX = point.X;
        info.RespawnY = point.Y;
        info.RespawnLayer = 0;
        _ = PersistAsync(client, info.CharacterName, point);
    }

    public bool TryBind(GameClient client, string function)
    {
        if (!ReturnPointRules.TryGetBinding(function, out var binding)) return false;

        Set(client, ReturnPointRules.Bind(binding, _random));
        client.Connection.Send(GameChatPackets.BuildChat("@SCRIPT", ChatNpc, binding.Message));
        return true;
    }

    public void OnLevelUp(GameClient client, int level)
    {
        if (level == ReturnPointRules.CampLevel)
        {
            Set(client, ReturnPointRules.Camp(_random));
        }
    }

    private async Task PersistAsync(GameClient client, string characterName, ReturnPoint point)
    {
        try
        {
            await _characterService.SaveReturnPointAsync(characterName, point);
        }
        catch (Exception exception)
        {
            _logger.Error(exception, "Could not save the return point of {clientTag}", client.ClientTag);
        }
    }
}
