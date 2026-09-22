using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using Navislamia.Game.Network.Clients;
using Navislamia.Game.Network.Packets.Enums;
using Navislamia.Game.Network.Packets.Game;
using Navislamia.Game.Services.Interfaces;
using Serilog;

namespace Navislamia.Game.Services.GmCommands;

/// <summary>
/// Runs the GM commands typed in chat. Parsing, permissions and argument bounds live in
/// <see cref="GmCommandParser"/>, <see cref="GmCommandCatalog"/> and <see cref="GmCommandRules"/>; this
/// class only reuses what the server already does — <see cref="IWarpService"/> for a warp,
/// <see cref="ICombatService"/> for a kill, <see cref="ILevelingService"/> for a level-up,
/// <see cref="ICharacterService"/> for an item — so a command can never take a path the game itself does
/// not take. Every answer is a system chat line from <c>@SYSTEM</c>, NGemity's convention.
/// See docs/gm-commands.md.
/// </summary>
public class GmCommandService : IGmCommandService
{
    public const string SystemSender = "@SYSTEM";

    /// <summary>The swing timing <c>CombatService</c> uses, so a <c>/doit</c> kill animates like a swing.</summary>
    private const ushort KillAttackDelayMs = 1200;

    private readonly ILogger _logger = Log.ForContext<GmCommandService>();
    private readonly IWarpService _warpService;
    private readonly ICombatService _combatService;
    private readonly ILevelingService _levelingService;
    private readonly IStatService _statService;
    private readonly ICharacterService _characterService;
    private readonly IItemSortCatalog _itemCatalog;
    private readonly MonsterWorldState _monsterState;

    public GmCommandService(IWarpService warpService, ICombatService combatService,
        ILevelingService levelingService, IStatService statService, ICharacterService characterService,
        IItemSortCatalog itemCatalog, MonsterWorldState monsterState)
    {
        _warpService = warpService;
        _combatService = combatService;
        _levelingService = levelingService;
        _statService = statService;
        _characterService = characterService;
        _itemCatalog = itemCatalog;
        _monsterState = monsterState;
    }

    public async Task HandleAsync(GameClient client, string message, IEnumerable<GameClient> everyone)
    {
        var info = client.ConnectionInfo;
        if (info.CharacterHandle == 0)
        {
            return;
        }

        if (!GmCommandParser.TryParse(message, out var line))
        {
            Reply(client, "Type /help for the list of commands.");
            return;
        }

        if (!GmCommandCatalog.TryResolve(line.Name, info.CharacterPermission, out var definition))
        {
            if (GmCommandCatalog.Exists(line.Name))
            {
                _logger.Warning("{clientTag} ({name}, permission {permission}) tried the privileged /{command}",
                    client.ClientTag, info.CharacterName, info.CharacterPermission, line.Name);
            }

            Reply(client, $"Unknown command: /{line.Name}. Type /help.");
            return;
        }

        if (definition.Privileged)
        {
            _logger.Information("GM {name} ({clientTag}) ran /{command} {arguments}", info.CharacterName,
                client.ClientTag, line.Name, line.Rest);
        }

        try
        {
            await RunAsync(client, definition, line, everyone);
        }
        catch (Exception exception)
        {
            _logger.Error(exception, "GM command /{command} failed for {clientTag}", line.Name, client.ClientTag);
            Reply(client, $"/{line.Name} failed, see the server log.");
        }
    }

    private async Task RunAsync(GameClient client, GmCommandDefinition definition, GmCommandLine line,
        IEnumerable<GameClient> everyone)
    {
        var info = client.ConnectionInfo;
        switch (definition.Command)
        {
            case GmCommand.Help:
                foreach (var available in GmCommandCatalog.AvailableTo(info.CharacterPermission))
                {
                    Reply(client, available.Usage);
                }

                break;

            case GmCommand.Position:
                Reply(client, GmCommandRules.FormatPosition(info.X, info.Y, info.Z, info.Layer));
                break;

            case GmCommand.Sitdown:
                if (!IsAlive(info))
                {
                    Reply(client, "You cannot sit down while dead.");
                    break;
                }

                // NGemity cancels the attack before sitting down (onCheatSitdown): a seated character does
                // not keep swinging.
                _combatService.StopAttack(client);
                info.IsSitting = true;
                SendStatus(client);
                break;

            case GmCommand.Standup:
                info.IsSitting = false;
                SendStatus(client);
                break;

            case GmCommand.Battle:
                if (!GmCommandRules.TryParseSwitch(line.Args, true, out var battle))
                {
                    Usage(client, definition);
                    break;
                }

                info.IsBattleMode = battle;
                SendStatus(client);
                break;

            case GmCommand.Walk:
                if (!GmCommandRules.TryParseSwitch(line.Args, !info.IsWalking, out var walking))
                {
                    Usage(client, definition);
                    break;
                }

                info.IsWalking = walking;
                SendStatus(client);
                break;

            case GmCommand.KillAll:
                Reply(client, $"{KillVisibleMonsters(client)} monster(s) killed.");
                break;

            case GmCommand.Notice:
                if (line.Rest.Length == 0)
                {
                    Usage(client, definition);
                    break;
                }

                SendNotice(info.CharacterName, line.Rest, everyone);
                break;

            case GmCommand.Warp:
                if (!GmCommandRules.TryParseWarp(line.Args, out var x, out var y))
                {
                    Usage(client, definition);
                    break;
                }

                info.IsSitting = false;
                _warpService.Warp(client, x, y);
                Reply(client, string.Create(CultureInfo.InvariantCulture, $"Warped to {x:0.##} {y:0.##}."));
                break;

            case GmCommand.Item:
                await GiveItemAsync(client, definition, line);
                break;

            case GmCommand.Gold:
                if (!GmCommandRules.TryParseGold(line.Args, out var delta))
                {
                    Usage(client, definition);
                    break;
                }

                info.CharacterGold = GmCommandRules.ApplyGold(info.CharacterGold, delta);
                client.Connection.Send(GameCharacterPackets.BuildGoldUpdate(info.CharacterGold, info.CharacterChaos));
                Reply(client, $"Gold: {info.CharacterGold}.");
                break;

            case GmCommand.Level:
                RaiseLevel(client, definition, line);
                break;

            case GmCommand.Heal:
                if (!IsAlive(info))
                {
                    Reply(client, "You are dead: resurrect first.");
                    break;
                }

                RestoreVitals(client);
                Reply(client, "HP and MP restored.");
                break;

            case GmCommand.Die:
                if (!IsAlive(info))
                {
                    Reply(client, "You are already dead.");
                    break;
                }

                // Dead is HP 0 and nothing else in this version: the client learns it from the hp property,
                // exactly as from a killing swing (docs/packet-specs/socle-mort-respawn.md).
                _combatService.StopAttack(client);
                info.IsSitting = false;
                info.CharacterHp = 0;
                client.Connection.Send(GameStatPackets.BuildProperty(info.CharacterHandle, "hp", 0));
                break;

            default:
                _logger.Error("GM command {command} has no handler", definition.Command);
                break;
        }
    }

    /// <summary>
    /// <c>/doit</c>: NGemity force-kills every monster in the visible regions (onCheatKillAll). Here the
    /// set is the monsters this client has been streamed, which is the same view. Each kill goes through
    /// <see cref="ICombatService.ApplyDamage"/> with the monster's remaining HP, so the corpse, the drops,
    /// the reward and the respawn are the ordinary ones; the attack event that follows is what plays the
    /// death on the client, exactly as after a killing swing.
    /// </summary>
    private int KillVisibleMonsters(GameClient client)
    {
        var info = client.ConnectionInfo;
        List<(long InstanceId, uint Handle)> visible;
        lock (info.MonsterVisibilityLock)
        {
            visible = info.SpawnedMonsters.Select(pair => (pair.Key, pair.Value)).ToList();
        }

        var killed = 0;
        foreach (var (instanceId, handle) in visible)
        {
            if (!_monsterState.IsAlive(instanceId))
            {
                continue;
            }

            var hp = _monsterState.GetHp(instanceId);
            if (hp <= 0)
            {
                continue;
            }

            var targetHp = _combatService.ApplyDamage(client, instanceId, handle, hp);
            client.Connection.Send(GameAttackPackets.BuildAttackEvent(info.CharacterHandle, handle,
                KillAttackDelayMs, KillAttackDelayMs, GameAttackPackets.ActionAttack, hp, targetHp,
                info.CharacterHp));
            killed++;
        }

        return killed;
    }

    private void SendNotice(string sender, string text, IEnumerable<GameClient> everyone)
    {
        var packet = GameChatPackets.BuildChat(sender, (byte)ChatType.Notice, text);
        foreach (var recipient in everyone ?? Enumerable.Empty<GameClient>())
        {
            if (recipient?.ConnectionInfo.CharacterHandle is > 0)
            {
                recipient.Connection.Send(packet);
            }
        }
    }

    private async Task GiveItemAsync(GameClient client, GmCommandDefinition definition, GmCommandLine line)
    {
        if (!GmCommandRules.TryParseItem(line.Args, out var code, out var count))
        {
            Usage(client, definition);
            return;
        }

        // An unknown code would persist a row the client cannot render and the stat catalogue cannot read.
        if (!_itemCatalog.Contains(code))
        {
            Reply(client, $"Unknown item code {code}.");
            return;
        }

        var added = await _characterService.AddItemAsync(client.ConnectionInfo.CharacterName, code, count);
        if (added is null)
        {
            Reply(client, "The item could not be added.");
            return;
        }

        foreach (var packet in GameCharacterPackets.BuildInventory(new[] { added }))
        {
            client.Connection.Send(packet);
        }

        Reply(client, $"Item {code} x{added.Amount} added.");
    }

    /// <summary>
    /// <c>/level</c>: raises the cumulative experience to the target's threshold and lets
    /// <see cref="ILevelingService.ApplyExperience"/> run the ordinary level-up — the same packets, stats
    /// and HP refill as a level earned by killing. The level persists through the session save.
    /// </summary>
    private void RaiseLevel(GameClient client, GmCommandDefinition definition, GmCommandLine line)
    {
        var info = client.ConnectionInfo;
        var maxLevel = _levelingService.MaxLevel;
        if (maxLevel == 0)
        {
            Reply(client, "Leveling is disabled: no level threshold is loaded.");
            return;
        }

        if (!GmCommandRules.TryParseLevel(line.Args, info.CharacterLevel, maxLevel, out var target))
        {
            Reply(client, $"Usage: {definition.Usage}, above {info.CharacterLevel} and at most {maxLevel}.");
            return;
        }

        if (!_levelingService.TryGetExperienceFor(target, out var exp))
        {
            Reply(client, $"No experience threshold is loaded for level {target}.");
            return;
        }

        info.CharacterExp = Math.Max(info.CharacterExp, exp);
        client.Connection.Send(GameCharacterPackets.BuildExpUpdate(info.CharacterHandle, info.CharacterExp,
            info.CharacterJp));
        _levelingService.ApplyExperience(client);
        Reply(client, $"Level {info.CharacterLevel}.");
    }

    private void RestoreVitals(GameClient client)
    {
        var info = client.ConnectionInfo;
        var stats = _statService.Compute(info).Total;
        var maxHp = (int)stats.MaxHp;
        var maxMp = (int)stats.MaxMp;
        info.CharacterHp = maxHp;
        info.CharacterMaxHp = maxHp;
        info.CharacterMp = maxMp;

        var handle = info.CharacterHandle;
        client.Connection.Send(GameStatPackets.BuildProperty(handle, "max_hp", maxHp));
        client.Connection.Send(GameStatPackets.BuildProperty(handle, "hp", maxHp));
        client.Connection.Send(GameStatPackets.BuildProperty(handle, "max_mp", maxMp));
        client.Connection.Send(GameStatPackets.BuildProperty(handle, "mp", maxMp));
    }

    private static void SendStatus(GameClient client)
    {
        var info = client.ConnectionInfo;
        client.Connection.Send(GameCharacterPackets.BuildStatusChange(info.CharacterHandle,
            ActorStatus.ForPlayer(info.PkMode, info.IsSitting, info.IsBattleMode, info.IsWalking)));
    }

    private static bool IsAlive(ConnectionInfo info) => MonsterAiRules.IsAlive(info.CharacterHp);

    private static void Usage(GameClient client, GmCommandDefinition definition) =>
        Reply(client, $"Usage: {definition.Usage}");

    private static void Reply(GameClient client, string text) =>
        client.Connection.Send(GameChatPackets.BuildChat(SystemSender, (byte)ChatType.System, text));
}
