using System;
using System.Collections.Frozen;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using MoonSharp.Interpreter;
using Navislamia.Configuration.Options;
using Navislamia.Game.DataAccess.Contexts;
using Navislamia.Game.DataAccess.Entities.Enums;
using Navislamia.Game.DataAccess.Entities.Telecaster;
using Navislamia.Game.Network.Clients;
using Navislamia.Game.Network.Packets.Enums;
using Navislamia.Game.Network.Packets.Game;
using Serilog;

namespace Navislamia.Game.Services.Guilds;

/// <summary>What the official guild or siege manager script asks the dialog service to do once it has run.</summary>
public sealed record GuildDialogResult(NpcScriptPage Page, int QuestInfo = 0, (float X, float Y, byte Layer)? Warp = null);

/// <summary>
/// The guild officers' and the dungeon siege managers' dialogues, run from the official Epic 7 Lua
/// (<c>NPC_CreateGuild.lua</c>, <c>NPC_QuestClient.lua</c>, exported by <c>tools/export_guild_dialogs.py</c>) with the engine
/// functions they call ported from the official C++ (<c>ScriptGuild.cpp</c>, <c>ScriptPlayer.cpp</c>). The script runs under
/// the guild gate then the character gate — the order of every other guild operation — with one transaction, so a
/// creation, a tax draw or an abandon commits with the page it produces, or not at all.
/// docs/packet-specs/socle-dialogues-guilde-siege.md.
/// </summary>
public sealed partial class GuildService
{
    private static readonly Lazy<(FrozenDictionary<string, string> Functions, string Program)> DialogScripts = new(() =>
    {
        using var resource = typeof(GuildService).Assembly.GetManifestResourceStream("Navislamia.GuildDialogs.json")
            ?? throw new InvalidOperationException("Guild dialog resource is missing");
        var functions = JsonSerializer.Deserialize<Dictionary<string, string>>(resource)!.ToFrozenDictionary();
        return (functions, string.Join("\n", functions.Values));
    });

    /// <summary><c>GUILD_CREATE_*</c>, <c>ALLIANCE_CREATE_*</c> and <c>ALLIANCE_DESTROY_*</c> (<c>ScriptGuild.cpp:15-40</c>).</summary>
    public static class LuaCodes
    {
        public const int Success = 0;
        public const int GuildAlreadyInGuild = 1, GuildInvalidName = 2, GuildExistingName = 3, GuildBlockTime = 5;
        public const int AllianceAlreadyIn = 1, AllianceInvalidName = 2, AllianceExistingName = 3, AllianceNotLeader = 5,
            AllianceRaidStarted = 6, AllianceBlockTime = 7;
        public const int DestroyNotInGuild = 1, DestroyNotInAlliance = 2, DestroyNotGuildLeader = 4,
            DestroyNotAllianceLeader = 5, DestroyOtherGuilds = 6, DestroyDungeonOwner = 7, DestroyRaidRequested = 8;
    }

    /// <summary><c>DEFAULT_MAX_ALLIANCE_CNT</c> (<c>GuildManager.h:99</c>).</summary>
    public const int DefaultMaxAllianceCount = 3;

    /// <summary>
    /// <c>GameRule::IsValidName(code_page, name, …, 1, 16)</c> for an English client: one to sixteen ASCII letters or digits
    /// (<c>GameRule.cpp:403</c>; no space, no punctuation).
    /// </summary>
    public static bool OfficialName(string name) => name is { Length: >= 1 and <= 16 } && name.All(char.IsAsciiLetterOrDigit);

    /// <summary>Whether <paramref name="function"/> is one of the guild officers' or siege managers' official functions.</summary>
    public static bool IsDialogFunction(string function) => function is not null && DialogScripts.Value.Functions.ContainsKey(function);

    private Func<GameClient, uint, long, string, Task> _dialogRunner;

    /// <summary>The dialog service renders the pages these scripts produce; a window callback (650/660) reaches it here.</summary>
    public void AttachDialogs(Func<GameClient, uint, long, string, Task> runner, Func<GameClient, int, int> questProgress = null)
    {
        _dialogRunner = runner;
        QuestProgress = questProgress;
    }

    /// <summary>
    /// Runs <paramref name="call"/> — a contact (<c>NPC_CreateGuild_Deva_contact()</c>,
    /// <c>NPC_dungeon_siege_manager_contact(130000)</c>), an advertised menu trigger or a window callback — for the dialog
    /// <paramref name="npcHandle"/>/<paramref name="revision"/>. Null when nothing is to be shown or the dialog moved on.
    /// </summary>
    public async Task<GuildDialogResult> RunDialogAsync(GameClient client, uint npcHandle, long revision, string call)
    {
        var info = client.ConnectionInfo;
        var handle = info.CharacterHandle; var name = info.CharacterName;
        await _gate.WaitAsync();
        try
        {
            if (!Current(client, handle, name)) return null;
            PruneTeams();
            return await _characters.RunAsync(name, async () =>
            {
                var transfer = new WalletTransfer();
                long spent = 0;
                try
                {
                    await using var db = new TelecasterContext(_options);
                    await using var transaction = db.Database.IsRelational() ? await db.Database.BeginTransactionAsync() : null;
                    var member = await db.Characters.SingleOrDefaultAsync(c => c.Id == handle && c.CharacterName == name);
                    long npcId;
                    lock (info.NpcVisibilityLock)
                        if (member is null || info.NpcDialogHandle != npcHandle || info.NpcDialogRevision != revision
                            || !info.SpawnedNpcIdsByHandle.TryGetValue(npcHandle, out npcId)) return null;
                    var host = new DialogHost(this, db, client, member, npcId, npcHandle, revision, transfer);
                    host.Run(call);
                    spent = host.Spent;
                    await db.SaveChangesAsync();
                    if (transaction is not null) await transaction.CommitAsync();
                    transfer.Committed = true;
                    await SyncAsync(db);
                    foreach (var notify in host.Notifications) notify();
                    return new GuildDialogResult(host.Shown ? new NpcScriptPage(host.Page, host.IncludeQuests) : null,
                        host.QuestInfo, host.Warp);
                }
                finally
                {
                    if (!transfer.Committed)
                    {
                        if (transfer.Gold > 0) info.TryDebitGold(transfer.Gold);
                        if (spent > 0) info.TryCreditGold(spent, long.MaxValue);
                        if (transfer.Chaos > 0) lock (info.ProgressLock) info.CharacterChaos -= transfer.Chaos;
                    }
                }
            });
        }
        catch (Exception exception)
        {
            Log.Warning(exception, "Guild dialog {Call} rejected for {Character}", call, name);
            return null;
        }
        finally { _gate.Release(); }
    }

    /// <summary>
    /// <c>onGuildCreate</c> / <c>onAllianceCreate</c> (<c>GameMessage.cpp:4312</c>): the name typed in the window comes back as
    /// <c>/gcreate</c> or <c>/gacreate</c> and runs <c>on_create_guild('name')</c> or <c>on_create_alliance('name')</c> with
    /// the officer the window was opened from. A quote or a backslash would break out of the Lua string: the official
    /// server closes the connection, this one refuses.
    /// </summary>
    private bool PrepareCreation(GameClient client, bool alliance, string name)
    {
        if (name is not { Length: >= 1 and <= 30 } || name.Any(c => c is '\'' or '\\' || char.IsControl(c))) return false;
        var info = client.ConnectionInfo;
        InputWindow window;
        lock (info.NpcVisibilityLock)
        {
            lock (_inputs) if (!_inputs.Remove(client, out window)) return false;
            if (window.Alliance != alliance || window.Handle != info.NpcDialogHandle || window.Revision != info.NpcDialogRevision
                || !info.SpawnedNpcIdsByHandle.TryGetValue(window.Handle, out var npc) || !GuildRules.Officers.Contains((int)npc)) return false;
        }

        if (_dialogRunner is null) return false;
        // After the command's gates: the dialog takes them again.
        var callback = $"{(alliance ? "on_create_alliance" : "on_create_guild")}( '{name}' )";
        _ = Task.Run(() => _dialogRunner(client, window.Handle, window.Revision, callback));
        return true;
    }

    private void OpenCreationWindow(GameClient client, uint npcHandle, long revision, bool alliance)
    {
        var info = client.ConnectionInfo;
        lock (info.NpcVisibilityLock)
        {
            if (info.NpcDialogHandle != npcHandle) return;
            lock (_inputs) _inputs[client] = new InputWindow(npcHandle, revision, alliance);
            client.Connection.Send(GameGuildPackets.BuildWindow(alliance ? GamePackets.TM_SC_SHOW_CREATE_ALLIANCE
                : GamePackets.TM_SC_SHOW_CREATE_GUILD));
        }
    }

    /// <summary>One script run: the page it builds, the effects it stages and the engine functions it may call.</summary>
    private sealed class DialogHost
    {
        private readonly GuildService _service;
        private readonly TelecasterContext _db;
        private readonly GameClient _client;
        private readonly CharacterEntity _member;
        private readonly long _npcId;
        private readonly WalletTransfer _transfer;
        private GuildEntity _guild;

        public NpcDialogDefinition Page { get; } = new();
        public bool Shown { get; private set; }
        public bool IncludeQuests { get; private set; } = true;
        public int QuestInfo { get; private set; }
        public (float X, float Y, byte Layer)? Warp { get; private set; }
        public long Spent { get; private set; }
        public List<Action> Notifications { get; } = new();

        public DialogHost(GuildService service, TelecasterContext db, GameClient client, CharacterEntity member, long npcId,
            uint npcHandle, long revision, WalletTransfer transfer)
        {
            _service = service; _db = db; _client = client; _member = member; _npcId = npcId; _transfer = transfer;
            _guild = member.GuildId is > 0 ? db.Guilds.SingleOrDefault(g => g.Id == member.GuildId) : null;
            NpcHandle = npcHandle; Revision = revision;
        }

        private uint NpcHandle { get; }
        private long Revision { get; }
        private ConnectionInfo Info => _client.ConnectionInfo;

        public void Run(string call)
        {
            var script = new Script(CoreModules.Preset_HardSandbox);
            script.DoString(DialogScripts.Value.Program);
            Bind(script);
            var coroutine = script.CreateCoroutine(script.LoadString(call)).Coroutine;
            // Coroutine instruction budget bounds the run; only the committed official functions are loaded.
            coroutine.AutoYieldCounter = 100_000;
            coroutine.Resume();
            if (coroutine.State != CoroutineState.Dead) throw new InvalidOperationException("Guild script instruction budget exceeded");
        }

        private static string Text(CallbackArguments a, int i) => a.Count > i ? a[i].CastToString() ?? string.Empty : string.Empty;
        private static long Number(CallbackArguments a, int i) => a.Count > i ? checked((long)a[i].CastToNumber().GetValueOrDefault()) : 0;
        private static DynValue N(double value) => DynValue.NewNumber(value);

        private void Bind(Script script)
        {
            void On(string name, Func<CallbackArguments, DynValue> action) =>
                script.Globals[name] = DynValue.NewCallback((_, a) => action(a));

            On("dlg_title", a => { Page.Title = Text(a, 0); return DynValue.Nil; });
            On("dlg_text", a => { Page.Text = Text(a, 0); IncludeQuests = true; return DynValue.Nil; });
            On("dlg_text_without_quest_menu", a => { Page.Text = Text(a, 0); IncludeQuests = false; return DynValue.Nil; });
            On("dlg_menu", a => { Page.Menu.Add(new NpcDialogMenuEntry { Label = Text(a, 0), Trigger = Text(a, 1).Trim() }); return DynValue.Nil; });
            On("dlg_show", _ => { Shown = true; return DynValue.Nil; });
            On("sconv", a => DynValue.NewString(string.Join("\v", Enumerable.Range(0, a.Count).Select(i => Text(a, i)))));
            On("get_npc_id", _ => N(_npcId));
            // LuaVM registers cprint as the server console (_outputConsole), not a chat line.
            On("cprint", a => { Log.Debug("Guild script: {Text}", Text(a, 0)); return DynValue.Nil; });
            On("message", a =>
            {
                var text = Text(a, 0);
                Notifications.Add(() => _client.Connection.Send(GameChatPackets.BuildChat("@SCRIPT", Props.TownTeleportRules.ChatNpc, text)));
                return DynValue.Nil;
            });
            On("save", _ => DynValue.Nil);
            On("set_npc_name", _ => DynValue.Nil);
            On("update_gold_chaos", _ =>
            {
                Notifications.Add(() => _client.Connection.Send(GameCharacterPackets.BuildGoldUpdate(Info.CharacterGold, Info.CharacterChaos)));
                return DynValue.Nil;
            });
            DynValue Value(CallbackArguments a) => Text(a, 0) switch
            {
                "gold" => N(Info.CharacterGold), "level" => N(Info.CharacterLevel), "guild_id" => N(_member.GuildId ?? 0),
                "x" => N(Info.X), "y" => N(Info.Y), "race" => N(Info.CharacterRace), "job" => N(Info.CharacterJob),
                _ => N(0)
            };
            On("get_value", Value); On("gv", Value);
            On("set_value", a =>
            {
                if (Text(a, 0) != "gold") throw new InvalidOperationException("Guild scripts only set gold");
                var target = Number(a, 1);
                var current = Info.CharacterGold;
                if (target < current)
                {
                    if (!Info.TryDebitGold(current - target)) throw new InvalidOperationException("Gold changed under the script");
                    Spent += current - target;
                }
                else if (target > current)
                {
                    if (!Info.TryCreditGold(target - current, GuildRules.MaxGold)) throw new InvalidOperationException("Gold limit");
                    _transfer.Gold += target - current;
                }

                _member.Gold = Info.CharacterGold;
                return DynValue.Nil;
            });
            On("get_quest_progress", a => N(_service.QuestProgress?.Invoke(_client, (int)Number(a, 0)) ?? -1));
            On("show_quest_info_without_npc", a => { QuestInfo = (int)Number(a, 0); return DynValue.Nil; });
            On("warp", a => { Warp = ((float)a[0].CastToNumber().GetValueOrDefault(), (float)a[1].CastToNumber().GetValueOrDefault(),
                a.Count > 2 ? checked((byte)Number(a, 2)) : Info.Layer); return DynValue.Nil; });
            BindGuild(On);
            BindSiege(On);
        }

        // ---- NPC_CreateGuild.lua: ScriptGuild.cpp ---------------------------------------------------------------

        private bool Permitted(GuildPermissions permission) => GuildRules.Permitted(_guild, _member, permission);

        private AllianceEntity Alliance => _guild?.AllianceId is > 0 ? _db.Alliances.SingleOrDefault(a => a.Id == _guild.AllianceId) : null;

        private bool LeadsAlliance(AllianceEntity alliance) => alliance is not null && alliance.LeadGuildId == _guild.Id;

        private bool OwnsDungeon(GuildEntity guild) => guild?.DungeonId is > 0
            && _db.Dungeons.Any(d => d.Id == guild.DungeonId && d.OwnerGuildId == guild.Id);

        private bool Banned(string name) => _service._banned?.ContainsBannedWord(name) == true;

        private void BindGuild(Action<string, Func<CallbackArguments, DynValue>> on)
        {
            on("is_guild_leader", _ => N(_guild is not null && _guild.LeaderId == _member.Id ? 1 : 0));
            on("is_alliance_leader", _ => N(_guild is not null && _guild.LeaderId == _member.Id && LeadsAlliance(Alliance) ? 1 : 0));
            on("get_max_alliance_member_count", _ =>
                N(Permitted(GuildPermissions.AllianceManagement) && Alliance is { } a && LeadsAlliance(a) ? a.MaxAllianceCount : 0));
            on("increase_max_alliance_member_count", _ =>
            {
                if (!Permitted(GuildPermissions.AllianceManagement) || Alliance is not { } alliance || !LeadsAlliance(alliance)) return N(0);
                alliance.MaxAllianceCount++;
                return N(1);
            });
            on("check_valid_guild_name", a =>
            {
                var name = Text(a, 0);
                if (name.Length is < 1 or > 16) return N(LuaCodes.GuildInvalidName);
                var normalized = GuildRules.Normalize(name);
                if (_db.Guilds.Any(g => g.NormalizedName == normalized)) return N(LuaCodes.GuildExistingName);
                return N(OfficialName(name) && !Banned(name) ? LuaCodes.Success : LuaCodes.GuildInvalidName);
            });
            on("check_valid_alliance_name", a =>
            {
                var name = Text(a, 0);
                if (name.Length is < 1 or > 16) return N(LuaCodes.AllianceInvalidName);
                var normalized = GuildRules.Normalize(name);
                if (_db.Alliances.Any(x => x.NormalizedName == normalized)) return N(LuaCodes.AllianceExistingName);
                return N(OfficialName(name) && !Banned(name) ? LuaCodes.Success : LuaCodes.AllianceInvalidName);
            });
            on("create_guild", a => N(CreateGuild(Text(a, 0).Trim())));
            on("create_alliance", a => N(CreateAlliance(Text(a, 0).Trim())));
            on("destroy_alliance", _ => N(DestroyAlliance()));
            on("show_guild_create", _ =>
            {
                Notifications.Add(() => _service.OpenCreationWindow(_client, NpcHandle, Revision, false));
                return DynValue.Nil;
            });
            on("show_alliance_create", _ =>
            {
                Notifications.Add(() => _service.OpenCreationWindow(_client, NpcHandle, Revision, true));
                return DynValue.Nil;
            });
        }

        /// <summary><c>SCRIPT_CreateGuild</c> (<c>ScriptGuild.cpp:247-301</c>); the Lua charges the 100 000 gold itself.</summary>
        private int CreateGuild(string name)
        {
            if (!OfficialName(name) || Banned(name)) return LuaCodes.GuildInvalidName;
            if (_member.GuildId is > 0) return LuaCodes.GuildAlreadyInGuild;
            // GetPrevGuildID: a character who left a guild waits out the block before founding one.
            if (_member.GuildBlockTime > _service.Now) return LuaCodes.GuildBlockTime;
            var normalized = GuildRules.Normalize(name);
            if (_db.Guilds.Any(g => g.NormalizedName == normalized)) return LuaCodes.GuildExistingName;
            var guild = new GuildEntity
            {
                Name = name, NormalizedName = normalized, LeaderId = _member.Id,
                PermissionNames = Enumerable.Repeat(string.Empty, 6).ToArray(), PermissionSets = new GuildPermissions[6],
                Notice = string.Empty, Url = string.Empty, Icon = string.Empty, Banner = string.Empty,
                AdvertiseComment = string.Empty, AdvertiseEndTime = _service.Now
            };
            _db.Guilds.Add(guild);
            _db.SaveChanges();
            _member.GuildId = guild.Id; _member.GuildPermission = 7; _member.GuildMemo = string.Empty;
            _guild = guild;
            Notifications.Add(() => Send(_client, $"JOIN|{name}|"));
            return LuaCodes.Success;
        }

        /// <summary><c>SCRIPT_CreateAlliance</c> (<c>ScriptGuild.cpp:376-447</c>).</summary>
        private int CreateAlliance(string name)
        {
            if (_guild is null || !Permitted(GuildPermissions.AllianceManagement)) return LuaCodes.AllianceNotLeader;
            if (_guild.AllianceBlockTime > _service.UnixNow) return LuaCodes.AllianceBlockTime;
            if (OwnsDungeon(_guild)) return LuaCodes.AllianceRaidStarted;
            if (!OfficialName(name) || Banned(name)) return LuaCodes.AllianceInvalidName;
            if (_guild.AllianceId is > 0) return LuaCodes.AllianceAlreadyIn;
            var normalized = GuildRules.Normalize(name);
            // GuildManager::CreateAlliance fails on a taken name, which the script reports as an invalid one.
            if (_db.Alliances.Any(x => x.NormalizedName == normalized)) return LuaCodes.AllianceInvalidName;
            var alliance = new AllianceEntity { Name = name, NormalizedName = normalized, LeadGuildId = _guild.Id,
                MaxAllianceCount = DefaultMaxAllianceCount };
            _db.Alliances.Add(alliance);
            _db.SaveChanges();
            _guild.AllianceId = alliance.Id;
            var guild = _guild;
            Notifications.Add(() => Send(_client, $"CREATE|{name}|", true));
            return LuaCodes.Success;
        }

        /// <summary><c>SCRIPT_DestroyAlliance</c> (<c>ScriptGuild.cpp:460-540</c>).</summary>
        private int DestroyAlliance()
        {
            if (_guild is null) return LuaCodes.DestroyNotInGuild;
            if (Alliance is not { } alliance) return LuaCodes.DestroyNotInAlliance;
            if (_guild.LeaderId != _member.Id) return LuaCodes.DestroyNotGuildLeader;
            if (!LeadsAlliance(alliance)) return LuaCodes.DestroyNotAllianceLeader;
            if (_db.Guilds.Count(g => g.AllianceId == alliance.Id) > 1) return LuaCodes.DestroyOtherGuilds;
            if (_guild.DungeonId is > 0) return OwnsDungeon(_guild) ? LuaCodes.DestroyDungeonOwner : LuaCodes.DestroyRaidRequested;
            _guild.AllianceId = null;
            _guild.AllianceBlockTime = _service.UnixNow + (long)GuildRules.RejoinDelay.TotalSeconds;
            _db.Alliances.Remove(alliance);
            var guild = _guild;
            Notifications.Add(() => _service.Broadcast(guild.Id, $"DESTROY|{alliance.Name}|", false));
            return LuaCodes.Success;
        }

        // ---- NPC_QuestClient.lua siege manager: ScriptPlayer.cpp ------------------------------------------------

        private DungeonEntity Dungeon(long id) => _db.Dungeons.SingleOrDefault(d => d.Id == id);

        private long AllianceLeadGuild => Alliance?.LeadGuildId ?? 0;

        /// <summary><c>SCRIPT_GetDungeonRelation</c> (<c>ScriptPlayer.cpp:3376-3459</c>): 0 stranger … 9 manager of the owner.</summary>
        private int Relation(long dungeonId)
        {
            if (_guild is null) return 0;
            var state = Dungeon(dungeonId);
            var owner = state?.OwnerGuildId ?? 0; var raid = state?.RaidGuildId ?? 0;
            var leader = _guild.LeaderId == _member.Id;
            if (owner != 0 && _guild.Id == owner)
                return leader ? 1 : Permitted(GuildPermissions.DungeonManagement) ? 9 : 2;
            if (_guild.AllianceId is > 0 && AllianceLeadGuild == owner) return leader ? 3 : 4;
            if (raid != 0 && _guild.Id == raid) return leader ? 5 : 6;
            if (_guild.AllianceId is > 0 && AllianceLeadGuild == raid) return leader ? 7 : 8;
            return 0;
        }

        private void BindSiege(Action<string, Func<CallbackArguments, DynValue>> on)
        {
            on("get_dungeon_relation", a => N(Relation(Number(a, 0))));
            on("get_own_guild_name", a =>
            {
                var owner = Dungeon(Number(a, 0))?.OwnerGuildId;
                // GameContent::GetString(633) when nobody owns it: the client string reference.
                var name = owner is > 0 ? _db.Guilds.Where(g => g.Id == owner).Select(g => g.Name).FirstOrDefault() : null;
                return DynValue.NewString(name ?? "@633");
            });
            on("get_tax_rate", a => N(Dungeon(Number(a, 0))?.TaxRate ?? 0));
            on("set_tax_rate", a =>
            {
                var rate = Number(a, 1);
                var state = Dungeon(Number(a, 0));
                if (rate is < 1 or > 10 || _guild is null || state?.OwnerGuildId != _guild.Id
                    || !Permitted(GuildPermissions.DungeonManagement)) return DynValue.Nil;
                state.TaxRate = (int)rate;
                return DynValue.Nil;
            });
            on("get_tax_amount", _ => N(_guild?.Gold ?? 0));
            on("get_tax_chaos_amount", _ => N(_guild?.Chaos ?? 0));
            on("draw_tax", _ => N(DrawTax()));
            on("draw_tax_chaos", _ => N(DrawTaxChaos()));
            on("drop_dungeon_owner_ship", _ => N(DropOwnership()));
        }

        /// <summary><c>SCRIPT_DrawTax</c> (<c>ScriptPlayer.cpp:3613-3663</c>).</summary>
        private long DrawTax()
        {
            if (_guild is null || !Permitted(GuildPermissions.DungeonManagement)) return 0;
            var amount = Math.Max(0, _guild.Gold);
            if (Info.CharacterGold + amount > GuildRules.MaxGold || !Info.TryCreditGold(amount, GuildRules.MaxGold)) return 0;
            _transfer.Gold += amount;
            _guild.Gold = 0; _member.Gold = Info.CharacterGold;
            Notifications.Add(() => _client.Connection.Send(GameCharacterPackets.BuildGoldUpdate(Info.CharacterGold, Info.CharacterChaos)));
            return amount;
        }

        /// <summary><c>SCRIPT_DrawTaxChaos</c> (<c>ScriptPlayer.cpp:3572-3611</c>): as much as the character can carry.</summary>
        private int DrawTaxChaos()
        {
            if (_guild is null || !Permitted(GuildPermissions.DungeonManagement)) return 0;
            int amount;
            lock (Info.ProgressLock)
            {
                var capacity = _service._stats is null ? 0 : CombatRewards.ChaosCapacity(_service._stats.Compute(Info).Total.MaxChaos);
                amount = Math.Clamp(Math.Min(_guild.Chaos, capacity - Info.CharacterChaos), 0, int.MaxValue);
                Info.CharacterChaos += amount;
                _member.Chaos = Info.CharacterChaos;
            }

            _transfer.Chaos += amount;
            _guild.Chaos -= amount;
            Notifications.Add(() => _client.Connection.Send(GameCharacterPackets.BuildGoldUpdate(Info.CharacterGold, Info.CharacterChaos)));
            return amount;
        }

        /// <summary>
        /// <c>SCRIPT_DropDungeonOwnership</c> (<c>ScriptPlayer.cpp:3688-3729</c>): a manager of the owning guild gives the
        /// dungeon up. The repository's own guards stay (no abandon during a raid, a siege or a visit of the guild).
        /// </summary>
        private int DropOwnership()
        {
            if (_guild is null || !Permitted(GuildPermissions.DungeonManagement) || _guild.DungeonId is not > 0) return 0;
            var state = Dungeon(_guild.DungeonId.Value);
            if (state?.OwnerGuildId != _guild.Id || _service.Busy(_guild.Id)
                || _db.GuildSieges.Any(s => s.DungeonId == state.Id && s.FinishedAt == null)) return 0;
            state.OwnerGuildId = null; _guild.DungeonId = null;
            var until = _service.UnixNow + (long)GuildRules.RejoinDelay.TotalSeconds;
            var allianceId = _guild.AllianceId;
            foreach (var ally in _db.Guilds.Where(g => g.Id == _guild.Id || allianceId != null && g.AllianceId == allianceId).ToArray())
                ally.DungeonBlockTime = until;
            return 1;
        }
    }

    /// <summary>The quest progress the officers' scripts read (<c>get_quest_progress</c>), wired by the dialog service.</summary>
    public Func<GameClient, int, int> QuestProgress { get; set; }
}
