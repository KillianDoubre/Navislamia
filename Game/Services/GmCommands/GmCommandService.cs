using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using Navislamia.Game.Network.Clients;
using Navislamia.Game.Network.Packets.Enums;
using Navislamia.Game.Network.Packets.Game;
using Navislamia.Game.Services.Interfaces;
using Navislamia.Game.Services.Stats;
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
    private readonly SkillCatalog _skillCatalog;
    private readonly ISkillCastService _skillCastService;
    private readonly IStateCatalog _stateCatalog;

    public GmCommandService(IWarpService warpService, ICombatService combatService,
        ILevelingService levelingService, IStatService statService, ICharacterService characterService,
        IItemSortCatalog itemCatalog, MonsterWorldState monsterState, SkillCatalog skillCatalog,
        ISkillCastService skillCastService, IStateCatalog stateCatalog)
    {
        _warpService = warpService;
        _combatService = combatService;
        _levelingService = levelingService;
        _statService = statService;
        _characterService = characterService;
        _itemCatalog = itemCatalog;
        _monsterState = monsterState;
        _skillCatalog = skillCatalog;
        _skillCastService = skillCastService;
        _stateCatalog = stateCatalog;
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

            case GmCommand.Exp:
                if (!GmCommandRules.TryParseAmount(line.Args, true, out var exp))
                {
                    Usage(client, definition);
                    break;
                }

                // The ordinary kill path: add the experience, publish it, and let the leveling service
                // resolve however many levels it is worth (CombatService.AwardKill does the same).
                info.CharacterExp = GmCommandRules.AddClamped(info.CharacterExp, exp);
                client.Connection.Send(GameCharacterPackets.BuildExpUpdate(info.CharacterHandle,
                    info.CharacterExp, info.CharacterJp));
                _levelingService.ApplyExperience(client);
                Reply(client, $"Exp: {info.CharacterExp} (level {info.CharacterLevel}).");
                break;

            case GmCommand.Jp:
                if (!GmCommandRules.TryParseAmount(line.Args, false, out var jp))
                {
                    Usage(client, definition);
                    break;
                }

                info.CharacterJp = GmCommandRules.AddClamped(info.CharacterJp, jp);
                client.Connection.Send(GameCharacterPackets.BuildExpUpdate(info.CharacterHandle,
                    info.CharacterExp, info.CharacterJp));
                Reply(client, $"JP: {info.CharacterJp}.");
                break;

            case GmCommand.JobLevel:
                RaiseJobLevel(client, definition, line);
                break;

            case GmCommand.Learn:
                await LearnSkillAsync(client, definition, line);
                break;

            case GmCommand.Buff:
                ApplyBuff(client, definition, line);
                break;

            case GmCommand.Immortal:
                if (!GmCommandRules.TryParseSwitch(line.Args, !info.IsImmortal, out var immortal))
                {
                    Usage(client, definition);
                    break;
                }

                info.IsImmortal = immortal;
                Reply(client, immortal ? "Immortal: monsters deal no damage." : "Mortal again.");
                break;

            case GmCommand.Pk:
                if (!GmCommandRules.TryParseSwitch(line.Args, !info.PkMode, out var pk))
                {
                    Usage(client, definition);
                    break;
                }

                // PkMode reaches the client through the status mask only, and the session save persists it
                // (docs/packet-specs/socle-mode-pk.md): this is what 800/801 will do once they exist.
                info.PkMode = pk;
                SendStatus(client);
                Reply(client, pk ? "PK mode on." : "PK mode off.");
                break;

            case GmCommand.Home:
                if (info.RespawnX == 0 && info.RespawnY == 0)
                {
                    Reply(client, "No return point is known for this session.");
                    break;
                }

                // The resurrection return point: the position the character entered the world at, on its
                // own layer (ResurrectionService does the same before warping).
                info.IsSitting = false;
                info.Layer = info.RespawnLayer;
                _warpService.Warp(client, info.RespawnX, info.RespawnY);
                Reply(client, "Back at the return point.");
                break;

            case GmCommand.Target:
                DescribeTarget(client);
                break;

            case GmCommand.Save:
                await _characterService.SaveProgressAsync(info.CharacterName, info.CharacterLevel,
                    info.CharacterJobLevel, info.CharacterExp, info.CharacterJp, info.CharacterGold,
                    info.CharacterChaos, info.X, info.Y, info.PkMode);
                Reply(client, "Progress saved.");
                break;

            case GmCommand.Chaos:
                if (!GmCommandRules.TryParseAmount(line.Args, false, out var chaos))
                {
                    Usage(client, definition);
                    break;
                }

                info.CharacterChaos = GmCommandRules.ApplyChaos(info.CharacterChaos, chaos);
                client.Connection.Send(GameCharacterPackets.BuildGoldUpdate(info.CharacterGold, info.CharacterChaos));
                Reply(client, $"Chaos: {info.CharacterChaos}.");
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

    /// <summary>
    /// <c>/joblevel</c>: each step goes through <see cref="ILevelingService.ApplyJobLevelUp"/>, the path of
    /// the JLv-up button, after crediting exactly the JP that step costs, so the JP balance is unchanged
    /// and the client receives the very sequence it knows (exp update, <c>job_level</c> property, result
    /// 410, stat refresh). The climb stops where the JP curve caps the tier.
    /// </summary>
    private void RaiseJobLevel(GameClient client, GmCommandDefinition definition, GmCommandLine line)
    {
        var info = client.ConnectionInfo;
        var current = Math.Max(1, info.CharacterJobLevel);
        if (!GmCommandRules.TryParseJobLevel(line.Args, current, out var target))
        {
            Reply(client, $"Usage: {definition.Usage}, above {current}.");
            return;
        }

        while (info.CharacterJobLevel < target)
        {
            var cost = _levelingService.NextJobLevelCost(Math.Max(1, info.CharacterJobLevel));
            if (cost <= 0)
            {
                break;
            }

            var before = info.CharacterJobLevel;
            info.CharacterJp = GmCommandRules.AddClamped(info.CharacterJp, cost);
            _levelingService.ApplyJobLevelUp(client, info.CharacterHandle);
            if (info.CharacterJobLevel <= before)
            {
                // Refused for a reason the cost did not cover: give the credited JP back and stop.
                info.CharacterJp = GmCommandRules.AddClamped(info.CharacterJp, -cost);
                break;
            }
        }

        Reply(client, info.CharacterJobLevel >= target
            ? $"Job level {info.CharacterJobLevel}."
            : $"Job level {info.CharacterJobLevel}: the JP curve caps the tier here.");
    }

    /// <summary>
    /// <c>/learn</c>: writes the skill level through <see cref="ICharacterService.SaveLearnedSkillAsync"/>,
    /// the persistence of the learning window, with the JP left untouched, then sends the one-record
    /// <c>TS_SC_SKILL_LIST</c> and the refreshed stats a learnt passive needs. The job restriction is
    /// ignored on purpose: a GM may learn any skill the catalogue knows.
    /// </summary>
    private async Task LearnSkillAsync(GameClient client, GmCommandDefinition definition, GmCommandLine line)
    {
        var info = client.ConnectionInfo;
        if (!GmCommandRules.TryParseLearn(line.Args, out var skillId, out var level))
        {
            Usage(client, definition);
            return;
        }

        if (!_skillCatalog.TryGetMaxLevel(skillId, out var maxLevel))
        {
            Reply(client, $"Unknown skill {skillId}.");
            return;
        }

        if (level == 0)
        {
            level = maxLevel;
        }

        if (level > maxLevel)
        {
            Reply(client, $"Skill {skillId} stops at level {maxLevel}.");
            return;
        }

        if (!await _characterService.SaveLearnedSkillAsync(info.CharacterName, skillId, level, info.CharacterJp))
        {
            Reply(client, "The skill could not be saved.");
            return;
        }

        info.LearnedSkills[skillId] = level;
        var handle = info.CharacterHandle;
        client.Connection.Send(GameCharacterPackets.BuildSkillList(handle,
            new[] { new KeyValuePair<int, byte>(skillId, level) }));

        _statService.RefreshPassives(info);
        var stats = _statService.Compute(info);
        client.Connection.Send(GameStatPackets.BuildStatInfo(handle, stats.Total, StatInfoType.Total));
        client.Connection.Send(GameStatPackets.BuildStatInfo(handle, stats.ByItem, StatInfoType.ByItem));
        client.Connection.Send(GameStatPackets.BuildProperty(handle, "max_hp", (int)stats.Total.MaxHp));
        client.Connection.Send(GameStatPackets.BuildProperty(handle, "max_mp", (int)stats.Total.MaxMp));
        Reply(client, $"Skill {skillId} level {level} learnt.");
    }

    /// <summary>
    /// <c>/buff</c>: the state goes through <see cref="ISkillCastService.ApplyState"/>, the same path as a
    /// cast buff, so its icon, countdown, expiry and stat contribution are the ordinary ones. An id outside
    /// <c>StateResource</c> is refused: the client would receive a state code nothing describes.
    /// </summary>
    private void ApplyBuff(GameClient client, GmCommandDefinition definition, GmCommandLine line)
    {
        if (!GmCommandRules.TryParseBuff(line.Args, out var stateId, out var level, out var seconds))
        {
            Usage(client, definition);
            return;
        }

        if (!_stateCatalog.Exists(stateId))
        {
            Reply(client, $"Unknown state {stateId}.");
            return;
        }

        _skillCastService.ApplyState(client, stateId, level, (uint)(seconds * ServerClock.TicksPerSecond));
        Reply(client, $"State {stateId} level {level} for {seconds} s.");
    }

    /// <summary><c>/target</c>: what the server knows of the current target, read without touching it.</summary>
    private void DescribeTarget(GameClient client)
    {
        var info = client.ConnectionInfo;
        var handle = info.TargetHandle;
        if (handle == 0)
        {
            Reply(client, "No target.");
            return;
        }

        if (!info.TryResolveMonster(handle, out var instanceId) ||
            !_monsterState.TryGetInstance(instanceId, out var monster))
        {
            Reply(client, $"Target {handle:X8} is not a visible monster.");
            return;
        }

        var state = _monsterState.IsAlive(instanceId) ? string.Empty : " (dead)";
        Reply(client, $"Monster {monster.MonsterId} lv {monster.Level} HP {_monsterState.GetHp(instanceId)}/" +
                      $"{monster.Hp} handle {handle:X8}{state}");
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
