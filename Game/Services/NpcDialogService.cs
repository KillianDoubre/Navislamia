using System;
using System.Collections.Frozen;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;
using Navislamia.Configuration.Options;
using Navislamia.Game.Network.Clients;
using Navislamia.Game.Network.Packets.Game;
using Navislamia.Game.Services.Interfaces;
using Navislamia.Game.Services.Props;
using Serilog;

namespace Navislamia.Game.Services;

public class NpcDialogService : INpcDialogService
{
    private const int MaxTriggerLength = 1024;

    /// <summary>The storage trigger the counter NPCs script (<c>open_storage()</c>), the way a warp is.</summary>
    private const string StorageFunction = "open_storage";
    private readonly ILogger _logger = Log.ForContext<NpcDialogService>();
    private readonly FrozenDictionary<int, string> _contacts;
    private readonly FrozenDictionary<string, CompiledDialog> _dialogs;
    private readonly IWarpService _warpService;
    private readonly IStorageService _storageService;
    private readonly IMarketService _marketService;
    private readonly IQuestService _quests;
    private readonly Jobs.IJobChangeService _jobChange;
    private readonly Creatures.ICreatureDialogService _creatureDialogs;
    private readonly Huntaholic.IHuntaholicService _huntaholic;
    private readonly Dungeons.IDungeonService _dungeons;
    private readonly Dungeons.DungeonCatalog _dungeonCatalog;
    private readonly Guilds.IGuildService _guilds;
    private readonly ReturnPoints.IReturnPointService _returnPoints;
    private readonly INpcScriptService _npcScripts;
    private readonly IReadOnlyList<GameEventWindow> _events;
    private readonly Func<DateTime> _localNow;

    public NpcDialogService(IOptions<NpcDialogOptions> options, IWarpService warpService,
        IStorageService storageService, IMarketService marketService, IQuestService quests = null,
        Jobs.IJobChangeService jobChange = null, Creatures.ICreatureDialogService creatureDialogs = null,
        Dungeons.IDungeonService dungeons = null, Dungeons.DungeonCatalog dungeonCatalog = null, Guilds.IGuildService guilds = null,
        Huntaholic.IHuntaholicService huntaholic = null, ReturnPoints.IReturnPointService returnPoints = null,
        INpcScriptService npcScripts = null, IOptions<GameRuleOptions> rules = null, Func<DateTime> localNow = null)
    {
        _events = rules?.Value?.Events ?? new List<GameEventWindow>();
        _localNow = localNow ?? (() => DateTime.Now);
        _npcScripts = npcScripts;
        _returnPoints = returnPoints;
        _huntaholic = huntaholic;
        _guilds = guilds;
        _dungeons = dungeons;
        _dungeonCatalog = dungeonCatalog;
        _jobChange = jobChange;
        _creatureDialogs = creatureDialogs;
        _warpService = warpService;
        _storageService = storageService;
        _marketService = marketService;
        _quests = quests;
        _contacts = CompileContacts(options.Value.Npcs);
        _dialogs = CompileDialogs(options.Value.Dialogs);
        _guilds?.AttachDialogs(RunGuildDialogAsync, quests is null ? null
            : (client, code) => quests.GetQuestProgressAsync(client, code).GetAwaiter().GetResult());
        _logger.Information("Loaded {npcCount} NPC dialog links and {dialogCount} dialog definitions",
            _contacts.Count, _dialogs.Count);
    }

    public void Contact(GameClient client, byte[] packet)
    {
        if (!GameNpcDialogPackets.TryReadContact(packet, out var handle))
        {
            _logger.Warning("Malformed NPC contact from {clientTag}", client.ClientTag);
            return;
        }

        long npcId;
        var info = client.ConnectionInfo;
        lock (info.NpcVisibilityLock)
        {
            if (!info.SpawnedNpcIdsByHandle.TryGetValue(handle, out npcId))
            {
                _logger.Warning("Rejected contact with unknown NPC handle {handle} from {clientTag}", handle,
                    client.ClientTag);
                return;
            }
        }

        // The guild officers and the siege managers run their official Lua (GuildService.Lua.cs): the contact call carries the
        // manager's dungeon (NPC_dungeon_siege_manager_contact( dungeon_id )), the catalogue keeps the function name only.
        if (_guilds is not null && _contacts.TryGetValue((int)npcId, out var guildContact)
            && Guilds.GuildService.IsDialogFunction(guildContact))
        {
            var dungeonId = 0;
            _dungeonCatalog?.NpcDungeons.TryGetValue((int)npcId, out dungeonId);
            if (guildContact == SiegeManagerContact && dungeonId == 0) return;
            long revision;
            lock (info.NpcVisibilityLock)
            {
                info.ClearNpcDialog();
                info.NpcDialogHandle = handle;
                revision = info.NpcDialogRevision;
            }

            _ = RunGuildDialogAsync(client, handle, revision,
                guildContact == SiegeManagerContact ? $"{guildContact}( {dungeonId} )" : guildContact + "()");
            return;
        }

        // The master-class NPC's contact is a job change page of its own (NPC_master_partdevil_contact), absent
        // from the catalogue's contact list.
        if (_jobChange is not null && npcId == Jobs.JobChangeRules.MasterNpcId)
        {
            long revision;
            lock (info.NpcVisibilityLock)
            {
                info.ClearNpcDialog();
                info.NpcDialogHandle = handle;
                revision = info.NpcDialogRevision;
            }

            _ = SelectJobChangeAsync(client, handle, revision, Jobs.JobChangeRules.MasterContact, string.Empty);
            return;
        }

        if (_contacts.TryGetValue((int)npcId, out var scriptContact) && _npcScripts?.Handles(scriptContact) == true)
        {
            long revision;
            lock (info.NpcVisibilityLock)
            {
                info.ClearNpcDialog();
                info.NpcDialogHandle = handle;
                revision = info.NpcDialogRevision;
            }
            _ = RunNpcScriptAsync(client, handle, revision, scriptContact + "()");
            return;
        }

        if (_quests?.HasNpcQuests((int)npcId) == true)
        {
            long revision;
            lock (info.NpcVisibilityLock)
            {
                info.ClearNpcDialog();
                info.NpcDialogHandle = handle;
                revision = info.NpcDialogRevision;
            }
            _ = ShowQuestContactAsync(client, handle, (int)npcId, revision);
            return;
        }

        if (!_contacts.TryGetValue((int)npcId, out var function))
        {
            _logger.Debug("NPC {npcId} has no renderable Epic 7.3 contact dialog", npcId);
            return;
        }

        // The island teleporter's menu depends on a quest and the job depth: built, not read from the catalogue.
        if (function == TownTeleportRules.BeginnerContact)
        {
            long revision;
            lock (info.NpcVisibilityLock)
            {
                info.ClearNpcDialog();
                info.NpcDialogHandle = handle;
                revision = info.NpcDialogRevision;
            }

            _ = ShowBeginnerTeleporterAsync(client, handle, revision);
            return;
        }

        if (!TryShow(client, handle, function))
        {
            _logger.Debug("NPC {npcId} contact {function} has no renderable Epic 7.3 dialog", npcId, function);
        }
    }

    public void Select(GameClient client, byte[] packet)
    {
        if (!GameNpcDialogPackets.TryReadSelection(packet, MaxTriggerLength, out var trigger))
        {
            _logger.Warning("Malformed NPC dialog selection from {clientTag}", client.ClientTag);
            return;
        }

        var info = client.ConnectionInfo;
        var special = false;
        ScriptWindow window = null;
        lock (info.NpcVisibilityLock)
        {
            var pending = info.ScriptWindowTrigger;
            var candidate = info.ScriptWindow;
            if (pending.Length > 0 && (candidate is not null && candidate.Trigger == pending
                ? candidate.Character == info.CharacterHandle && candidate.Matches(trigger)
                    && (candidate.Window != "number_input_window" || candidate.Npc == info.NpcDialogHandle
                        && candidate.Revision == info.NpcDialogRevision)
                : trigger == pending || (ReadFunctionName(pending) == pending && trigger == pending + "()")))
            {
                info.ScriptWindowTrigger = string.Empty;
                info.ScriptWindow = null;
                window = candidate?.Trigger == pending ? candidate : null;
                info.NpcDialogTriggers.Clear();
                special = true;
            }
        }
        if (special)
        {
            var windowAction = window?.DungeonAction ?? PropScript.Parse(trigger);
            if (_dungeons is not null && Dungeons.DungeonService.Handles(windowAction.Kind))
                _ = SelectDungeonAsync(client, windowAction);
            else if (window?.Window == "number_input_window" && window.Npc != 0 && _npcScripts is not null)
                _ = RunNpcScriptAsync(client, window.Npc, window.Revision, trigger);
            else if (_npcScripts is not null) _ = _npcScripts.RunWindowScriptAsync(client, trigger);
            return;
        }
        if (trigger.Length == 0)
        {
            lock (info.NpcVisibilityLock)
            {
                info.ClearNpcDialog();
                info.ScriptWindowTrigger = string.Empty;
                info.ScriptWindow = null;
            }
            return;
        }

        uint npcHandle;
        lock (info.NpcVisibilityLock)
        {
            if (info.NpcDialogHandle == 0 || !info.NpcDialogTriggers.Contains(trigger))
            {
                _logger.Warning("Rejected unexpected NPC dialog trigger from {clientTag}", client.ClientTag);
                return;
            }

            npcHandle = info.NpcDialogHandle;
        }

        // A teleport trigger carries its destination in the trigger itself, so it is resolved rather
        // than looked up as a follow-up dialog page. The guard above already proved the current
        // dialog advertised it.
        if (_guilds is not null && Guilds.GuildService.IsDialogFunction(ReadFunctionName(trigger)))
        {
            long revision;
            lock (info.NpcVisibilityLock)
            {
                // Claim this exact action once. The fresh page advertises any repeatable action again.
                if (info.NpcDialogHandle != npcHandle || !info.NpcDialogTriggers.Remove(trigger)) return;
                revision = info.NpcDialogRevision;
            }

            _ = RunGuildDialogAsync(client, npcHandle, revision, trigger);
            return;
        }

        var action = PropScript.Parse(trigger);
        if (_dungeons is not null && Dungeons.DungeonService.Handles(action.Kind))
        {
            // Only the official entries that open a native window confirm first: enter_dungeon (SCRIPT_WarpToDungeon,
            // the raid window), enter_instance_dungeon, enter_secret_dungeon and leave_instance_dungeon. A menu's
            // warp_to_instance_dungeon / warp_to_secret_dungeon warps at once (socle-fenetres-script.md §3).
            _ = SelectDungeonAsync(client, action, confirm: OpensConfirmation(trigger, action));
            return;
        }
        if (_quests is not null && ReadFunctionName(trigger) is "set_quest_status" or "set_title_condition")
        {
            // Only an exact action from the current server-authored menu reaches Lua.
            _ = _quests.RunScriptAsync(client, trigger);
            return;
        }
        if (_quests is not null && ReadFunctionName(trigger) is "quest_info" or "start_quest" or "end_quest")
        {
            _ = SelectQuestAsync(client, npcHandle, trigger);
            return;
        }
        if (action.Kind is PropActionKind.RunTeleport or PropActionKind.RunTeleportBeginToCity
            or PropActionKind.RunTeleportCityToCamp)
        {
            lock (info.NpcVisibilityLock)
            {
                info.ClearNpcDialog();
            }

            Teleport(client, action);
            return;
        }

        // A teleporter's Binding_* action: the return point it names (docs/packet-specs/socle-point-de-retour.md).
        if (_returnPoints is not null && _returnPoints.TryBind(client, ReadFunctionName(trigger)))
        {
            lock (info.NpcVisibilityLock)
            {
                info.ClearNpcDialog();
            }

            return;
        }

        // open_storage() opens the account storage of the character: the dialog link closes and the
        // storage frames answer the player, exactly like the teleport action above. The request is
        // asynchronous, so the frame is not awaited here — the receive loop must not wait on the database.
        if (ReadFunctionName(trigger) == StorageFunction)
        {
            lock (info.NpcVisibilityLock)
            {
                info.ClearNpcDialog();
            }

            _ = _storageService.OpenAsync(client);
            return;
        }

        // show_creature_farm_window() (NPC 11467, SCRIPT_ShowCreatureFarmWindow): an empty TS_SC_DIALOG of
        // TYPE_CREATURE_FARM_WINDOW (9) titled "CreatureFarm" opens the farm window, which asks its content with
        // 6000 and deposits, retrieves and nurses with 6002/6004/6006. The dialog stays current, like the auction
        // window's (docs/packet-specs/socle-ferme-creatures-officielle.md).
        if (ReadFunctionName(trigger) == Creatures.CreatureFarmRules.WindowFunction)
        {
            client.Connection.Send(GameNpcDialogPackets.BuildDialog(npcHandle, "CreatureFarm", string.Empty,
                Array.Empty<NpcDialogMenuEntry>(), Creatures.CreatureFarmRules.WindowDialogType));
            return;
        }

        // A merchant trigger is answered with TM_SC_MARKET (250), which opens the trade window; the
        // catalogue lines come from the market the trigger names. The NPC dialog is deliberately left
        // current — the window is additive, and leaving it lets the guard above validate a second
        // selection. The packet carries this dialog's NPC handle, not a market name.
        if (action.Kind == PropActionKind.OpenMarket)
        {
            // The window is additive: the dialog stays current, and the market it announced is remembered
            // for as long as the dialog lives, because TM_CS_BUY_ITEM (251) carries no market name. Only a
            // window that really opened is remembered: Open reports whether 250 was sent.
            if (_marketService.Open(client, npcHandle, action.Name))
            {
                info.OpenMarketName = action.Name;
            }

            return;
        }

        var function = ReadFunctionName(trigger);

        // NPC_huntaholic.lua: the lobby trip (go_to_huntaholic) and the JP boxes (hunterholic_jpbox_sell).
        if (_huntaholic is not null && _huntaholic.HandlesDialog(function))
        {
            _ = _huntaholic.SelectDialogAsync(client, function, trigger);
            return;
        }

        // The creature keeper's pages (care, revival, evolution) depend on the summons, so they are built here too.
        if (_creatureDialogs is not null && Creatures.CreatureDialogService.Handles(function))
        {
            SelectCreatureDialog(client, npcHandle, function, trigger);
            return;
        }

        // The job change pages depend on the character (race, job, levels, quests), so they are built here rather
        // than read from the catalogue; their triggers were advertised like any other.
        if (_jobChange is not null && Jobs.JobChangeRules.Handles(function))
        {
            long revision;
            lock (info.NpcVisibilityLock)
            {
                revision = info.NpcDialogRevision;
            }

            _ = SelectJobChangeAsync(client, npcHandle, revision, function, trigger);
            return;
        }

        if (_npcScripts?.Handles(function) == true)
        {
            long revision;
            lock (info.NpcVisibilityLock)
            {
                // Claim this exact action once. The fresh page advertises any repeatable action again.
                if (info.NpcDialogHandle != npcHandle || !info.NpcDialogTriggers.Remove(trigger)) return;
                revision = info.NpcDialogRevision;
            }
            _ = RunNpcScriptAsync(client, npcHandle, revision, trigger);
            return;
        }

        if (!TryShow(client, npcHandle, function))
        {
            lock (info.NpcVisibilityLock)
            {
                info.ClearNpcDialog();
            }
            _logger.Debug("NPC dialog action {function} is not implemented yet", function);
        }
    }

    /// <summary>
    /// NPC_TeleportTown.lua's three teleports: the cost is checked and charged first (<c>@90010008</c> without
    /// enough gold), <c>RunTeleport_City_To_Camp</c> moves the return point to the camp, then the character is
    /// warped near the destination.
    /// </summary>
    private void Teleport(GameClient client, PropAction action)
    {
        var info = client.ConnectionInfo;
        if (!info.TryDebitGold(action.Cost))
        {
            client.Connection.Send(GameChatPackets.BuildChat("@SCRIPT", TownTeleportRules.ChatNpc,
                TownTeleportRules.NotEnoughGold));
            return;
        }

        if (action.Cost > 0)
        {
            client.Connection.Send(GameCharacterPackets.BuildGoldUpdate(info.CharacterGold, info.CharacterChaos));
        }

        if (action.Kind == PropActionKind.RunTeleportCityToCamp)
        {
            _returnPoints?.Set(client, TownTeleportRules.CampReturnPoint(action, Random.Shared));
        }

        var spread = TownTeleportRules.ArrivalSpread(action.Kind);
        _warpService.Warp(client, action.X + Random.Shared.Next(0, spread + 1),
            action.Y + Random.Shared.Next(0, spread + 1));
    }

    private async Task ShowBeginnerTeleporterAsync(GameClient client, uint handle, long revision)
    {
        try
        {
            ShowDynamic(client, handle, revision, await BuildBeginnerTeleporterAsync(client), 0, 0);
        }
        catch (Exception exception)
        {
            _logger.Error(exception, "Could not show the island teleporter to {clientTag}", client.ClientTag);
        }
    }

    private async Task<NpcDialogDefinition> BuildBeginnerTeleporterAsync(GameClient client)
    {
        var questProgress = _quests is null ? -1 : await _quests.GetQuestProgressAsync(client, TownTeleportRules.EastCoastQuest);
        var info = client.ConnectionInfo;
        return TownTeleportRules.BeginnerTeleporter(info.CharacterRace, info.PreviousJobs.Count, questProgress);
    }

    /// <summary>Whether a menu trigger is one of the official functions that open a native confirmation window.</summary>
    public static bool OpensConfirmation(string trigger, PropAction action) =>
        action.Kind == PropActionKind.EnterDungeon
        || ReadFunctionName(trigger) is "enter_instance_dungeon" or "enter_secret_dungeon" or "leave_instance_dungeon";

    private async Task SelectDungeonAsync(GameClient client, PropAction action, bool confirm = false)
    {
        var result = confirm ? await _dungeons.ConfirmAsync(client, action) : await _dungeons.ExecuteAsync(client, action);
        if (result != Navislamia.Game.Network.Packets.ResultCode.Success)
            client.SendResult(3001, (ushort)result);
    }

    private bool TryShow(GameClient client, uint npcHandle, string function)
    {
        var info = client.ConnectionInfo;
        lock (info.NpcVisibilityLock)
        {
            if (!info.SpawnedNpcIdsByHandle.TryGetValue(npcHandle, out var npcId))
            {
                return false;
            }

            var dungeonId = 0;
            _dungeonCatalog?.NpcDungeons.TryGetValue((int)npcId, out dungeonId);
            NpcDialogDefinition definition;
            if (!string.IsNullOrEmpty(function) && _dialogs.TryGetValue(function, out var dialog))
                definition = dialog.Definition;
            else return false;

            // The Lua exporter retained only the prefix of concatenated dungeon arguments. Recover it
            // from the contacted NPC, never from client-supplied Lua or a global last-selected dungeon.
            var menu = ShownMenu(definition.Menu).Select(entry => new NpcDialogMenuEntry
            {
                Label = entry.Label,
                Trigger = dungeonId != 0 && entry.Trigger.TrimEnd().EndsWith('(')
                    && (ReadFunctionName(entry.Trigger).StartsWith("question_secret_dungeon_", StringComparison.Ordinal)
                        || ReadFunctionName(entry.Trigger) is "secret_dungeon_information" or "NPC_dungeon_siege_manager_contact" or "dungeon_information")
                    ? entry.Trigger + dungeonId + ")" : entry.Trigger
            }).ToArray();

            info.NpcDialogHandle = npcHandle;
            info.NpcDialogRevision++;
            info.NpcQuestCode = 0;
            info.NpcDialogTriggers.Clear();
            foreach (var trigger in menu.Select(m => m.Trigger).Where(t => t.Length > 0))
            {
                info.NpcDialogTriggers.Add(trigger);
            }

            client.Connection.Send(GameNpcDialogPackets.BuildDialog(npcHandle, definition.Title, definition.Text, menu));
        }
        return true;
    }

    /// <summary>
    /// The entries of a catalogue menu, an event's left out while its event is closed (<see cref="NpcEvents"/>): the
    /// town NPCs offered Halloween candy all year. A hidden entry is never advertised, so it cannot be selected either.
    /// </summary>
    private IEnumerable<NpcDialogMenuEntry> ShownMenu(IEnumerable<NpcDialogMenuEntry> menu)
    {
        var now = _localNow();
        return menu.Where(entry => NpcEvents.IsMenuShown(entry.Trigger, _events, now));
    }

    private static FrozenDictionary<int, string> CompileContacts(Dictionary<int, string> contacts)
    {
        var compiled = new Dictionary<int, string>(contacts.Count);
        foreach (var (npcId, expression) in contacts)
        {
            var function = ReadFunctionName(expression);
            if (function.Length > 0)
            {
                compiled[npcId] = function;
            }
        }

        return compiled.ToFrozenDictionary();
    }

    private static FrozenDictionary<string, CompiledDialog> CompileDialogs(
        Dictionary<string, NpcDialogDefinition> dialogs)
    {
        var compiled = new Dictionary<string, CompiledDialog>(dialogs.Count, StringComparer.Ordinal);
        foreach (var (function, dialog) in dialogs)
        {
            var menu = new List<NpcDialogMenuEntry>(dialog.Menu.Count);
            var triggers = new HashSet<string>(StringComparer.Ordinal);
            foreach (var entry in dialog.Menu)
            {
                if (entry.Label.Contains('\t') || entry.Trigger.Contains('\t'))
                {
                    continue;
                }

                menu.Add(entry);
                if (entry.Trigger.Length > 0)
                {
                    triggers.Add(entry.Trigger);
                }
            }

            var packet = GameNpcDialogPackets.BuildDialog(0, dialog.Title, dialog.Text, menu);
            var triggerArray = new string[triggers.Count];
            triggers.CopyTo(triggerArray);
            compiled[function] = new CompiledDialog(packet, triggerArray,
                new NpcDialogDefinition { Title = dialog.Title, Text = dialog.Text, Menu = menu });
        }

        return compiled.ToFrozenDictionary(StringComparer.Ordinal);
    }

    internal static string ReadFunctionName(string expression)
    {
        var value = expression.AsSpan().Trim();
        if (value.Length == 0 || (!char.IsAsciiLetter(value[0]) && value[0] != '_'))
        {
            return string.Empty;
        }

        var length = 1;
        while (length < value.Length &&
               (char.IsAsciiLetterOrDigit(value[length]) || value[length] == '_'))
        {
            length++;
        }

        return value.Slice(0, length).ToString();
    }

    private async Task ShowQuestContactAsync(GameClient client, uint handle, int npcId, long revision)
    {
        try
        {
            var offers = await _quests.GetNpcOffersAsync(client, npcId);
            var basis = _contacts.TryGetValue(npcId, out var function) && function == TownTeleportRules.BeginnerContact
                ? await BuildBeginnerTeleporterAsync(client)
                : _contacts.TryGetValue(npcId, out function) && _dialogs.TryGetValue(function, out var compiled)
                    ? compiled.Definition : new NpcDialogDefinition();
            var menu = new List<NpcDialogMenuEntry>(offers);
            menu.AddRange(ShownMenu(basis.Menu));
            ShowDynamic(client, handle, revision, new NpcDialogDefinition { Title = basis.Title, Text = basis.Text, Menu = menu }, 0, 0);
        }
        catch (Exception exception) { _logger.Error(exception, "Could not show quests for NPC {npcId}", npcId); }
    }

    private async Task SelectQuestAsync(GameClient client, uint handle, string trigger)
    {
        try
        {
            var match = Regex.Match(trigger, @"\A(quest_info|start_quest|end_quest)\(([0-9]+)(?:,(-?[0-9]+))?\)\z");
            if (!match.Success || !int.TryParse(match.Groups[2].Value, out var code)) return;
            var info = client.ConnectionInfo;
            int npcId;
            long revision;
            lock (info.NpcVisibilityLock)
            {
                if (info.NpcDialogHandle != handle || !info.NpcDialogTriggers.Contains(trigger)
                    || !info.SpawnedNpcIdsByHandle.TryGetValue(handle, out var id)) return;
                npcId = (int)id;
                revision = info.NpcDialogRevision;
            }
            switch (match.Groups[1].Value)
            {
                case "quest_info":
                    var title = _contacts.TryGetValue(npcId, out var function) && _dialogs.TryGetValue(function, out var compiled)
                        ? compiled.Definition.Title : string.Empty;
                    var dialog = await _quests.GetQuestDialogAsync(client, npcId, code, title);
                    if (dialog is not null)
                    {
                        var type = dialog.Menu.Any(m => m.Label == "START") ? 3 : dialog.Menu.Any(m => m.Label == "REWARD") ? 8 : 7;
                        ShowDynamic(client, handle, revision, dialog, type, code);
                    }
                    break;
                case "start_quest":
                    if (int.TryParse(match.Groups[3].Value, out var textId)) await _quests.StartQuestAsync(client, npcId, code, textId);
                    break;
                case "end_quest":
                    if (sbyte.TryParse(match.Groups[3].Value, out var reward))
                        await _quests.EndQuestAsync(client, new GameActionPackets.EndQuestRequest(code, reward));
                    break;
            }
        }
        catch (Exception exception) { _logger.Error(exception, "Could not handle quest dialog selection"); }
    }

    private void SelectCreatureDialog(GameClient client, uint handle, string function, string trigger)
    {
        var info = client.ConnectionInfo;
        int npcId;
        long revision;
        lock (info.NpcVisibilityLock)
        {
            if (info.NpcDialogHandle != handle || !info.SpawnedNpcIdsByHandle.TryGetValue(handle, out var id))
            {
                return;
            }

            npcId = (int)id;
            revision = info.NpcDialogRevision;
        }

        try
        {
            var step = _creatureDialogs.Select(client, npcId, function, trigger);
            if (step.Page is { } page)
            {
                ShowDynamic(client, handle, revision, page, 0, 0);
                return;
            }
        }
        catch (Exception exception)
        {
            _logger.Error(exception, "Could not handle creature keeper dialog {function}", function);
        }

        lock (info.NpcVisibilityLock)
        {
            info.ClearNpcDialog();
        }
    }

    private async Task SelectJobChangeAsync(GameClient client, uint handle, long revision, string function,
        string trigger)
    {
        try
        {
            var info = client.ConnectionInfo;
            int npcId;
            lock (info.NpcVisibilityLock)
            {
                if (info.NpcDialogHandle != handle || !info.SpawnedNpcIdsByHandle.TryGetValue(handle, out var id))
                {
                    return;
                }

                npcId = (int)id;
            }

            var step = await _jobChange.SelectAsync(client, npcId, function, trigger);
            if (step.Page is { } page)
            {
                if (step.WithQuests && _quests?.HasNpcQuests(npcId) == true)
                {
                    page.Menu.InsertRange(0, await _quests.GetNpcOffersAsync(client, npcId));
                }

                ShowDynamic(client, handle, revision, page, 0, 0);
            }
            else
            {
                lock (info.NpcVisibilityLock)
                {
                    info.ClearNpcDialog();
                }
            }

            if (step.CommitJob != 0)
            {
                await _jobChange.CommitAsync(client, npcId, step.CommitJob, step.Tutorial);
            }
        }
        catch (Exception exception)
        {
            _logger.Error(exception, "Could not handle job change dialog {function}", function);
        }
    }

    private const string SiegeManagerContact = "NPC_dungeon_siege_manager_contact";

    /// <summary>
    /// Renders what a guild officer's or siege manager's script produced: the warp of an abandoned dungeon, a quest page
    /// (<c>show_quest_info_without_npc</c>) or the dialog, with the NPC's quest offers on a <c>dlg_text</c> page.
    /// </summary>
    private async Task RunGuildDialogAsync(GameClient client, uint handle, long revision, string call)
    {
        try
        {
            var result = await _guilds.RunDialogAsync(client, handle, revision, call);
            if (result is null) return;
            if (result.Warp is { } warp) _warpService.Warp(client, warp.X, warp.Y, warp.Layer);
            int npcId;
            lock (client.ConnectionInfo.NpcVisibilityLock)
            {
                if (!client.ConnectionInfo.SpawnedNpcIdsByHandle.TryGetValue(handle, out var id)) return;
                npcId = (int)id;
            }

            if (result.QuestInfo != 0 && _quests is not null)
            {
                var quest = await _quests.GetQuestDialogAsync(client, npcId, result.QuestInfo, result.Page?.Dialog.Title ?? string.Empty);
                if (quest is not null)
                {
                    var type = quest.Menu.Any(m => m.Label == "START") ? 3 : quest.Menu.Any(m => m.Label == "REWARD") ? 8 : 7;
                    ShowDynamic(client, handle, revision, quest, type, result.QuestInfo);
                }

                return;
            }

            if (result.Page is not { } page) return;
            if (page.IncludeQuests && _quests is not null)
                page.Dialog.Menu.InsertRange(0, await _quests.GetNpcOffersAsync(client, npcId));
            ShowDynamic(client, handle, revision, page.Dialog, 0, 0);
        }
        catch (Exception exception) { _logger.Error(exception, "Guild dialogue failed for {Client}", client.ClientTag); }
    }

    private async Task RunNpcScriptAsync(GameClient client, uint handle, long revision, string trigger)
    {
        try
        {
            var result = await _npcScripts.RunAsync(client, handle, revision, trigger);
            if (result is null) return;
            if (result.IncludeQuests && _quests is not null)
            {
                int npcId;
                lock (client.ConnectionInfo.NpcVisibilityLock)
                {
                    if (!client.ConnectionInfo.SpawnedNpcIdsByHandle.TryGetValue(handle, out var id)) return;
                    npcId = (int)id;
                }
                result.Dialog.Menu.InsertRange(0, await _quests.GetNpcOffersAsync(client, npcId));
            }
            ShowDynamic(client, handle, revision, result.Dialog, 0, 0);
        }
        catch (Exception exception) { _logger.Error(exception, "NPC script dialogue failed for {Client}", client.ClientTag); }
    }

    private static void ShowDynamic(GameClient client, uint handle, long revision, NpcDialogDefinition dialog, int type, int code)
    {
        var info = client.ConnectionInfo;
        lock (info.NpcVisibilityLock)
        {
            if (info.NpcDialogHandle != handle || info.NpcDialogRevision != revision || !info.SpawnedNpcIdsByHandle.ContainsKey(handle)) return;
            info.NpcDialogRevision++;
            info.NpcQuestCode = code;
            info.NpcDialogTriggers.Clear();
            foreach (var entry in dialog.Menu.Where(m => m.Trigger.Length > 0)) info.NpcDialogTriggers.Add(entry.Trigger);
            client.Connection.Send(GameNpcDialogPackets.BuildDialog(handle, dialog.Title, dialog.Text, dialog.Menu, type));
        }
    }

    private sealed record CompiledDialog(byte[] PacketTemplate, string[] Triggers, NpcDialogDefinition Definition);
}
