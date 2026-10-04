using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using MoonSharp.Interpreter;
using Navislamia.Configuration.Options;
using Microsoft.Extensions.Options;
using Navislamia.Game.DataAccess.Entities.Enums;
using Navislamia.Game.DataAccess.Entities.Telecaster;
using Navislamia.Game.DataAccess.Repositories.Interfaces;
using Navislamia.Game.Network.Clients;
using Navislamia.Game.Network.Packets.Game;
using Navislamia.Game.Services.Auction;
using Navislamia.Game.Services.Casting;
using Navislamia.Game.Services.Creatures;
using Navislamia.Game.Services.Interfaces;
using Navislamia.Game.Services.Weight;
using Navislamia.Game.Services.Props;
using Serilog;

namespace Navislamia.Game.Services;

public sealed record NpcScriptPage(NpcDialogDefinition Dialog, bool IncludeQuests);

public interface INpcScriptService
{
    bool Handles(string function);
    Task<NpcScriptPage> RunAsync(GameClient client, uint npcHandle, long revision, string trigger);

    /// <summary>A field prop's <c>script_text</c>, run by <c>UseProp</c> with no dialogue around it.</summary>
    Task RunPropScriptAsync(GameClient client, string script) => Task.CompletedTask;
}

/// <summary>Only the exact server-advertised trigger may reach this interpreter (NpcDialogService).</summary>
public sealed class NpcScriptService : INpcScriptService
{
    private readonly NpcScriptCatalog _scripts;
    private readonly ICharacterRepositoryFactory _repositories;
    private readonly CharacterGate _gate;
    private readonly IItemMatchCatalog _items;
    private readonly IAuctionCatalog _names;
    private readonly IQuestService _quests;
    private readonly IStatService _stats;
    private readonly ICreatureEvents _creatures;
    private readonly ICastInterrupts _states;
    private readonly IWarpService _warp;
    private readonly IInventoryChangeFeed _feed;
    private readonly NpcScriptOptions _options;
    private readonly ILevelingService _leveling;

    public NpcScriptService(NpcScriptCatalog scripts, ICharacterRepositoryFactory repositories, CharacterGate gate,
        IItemMatchCatalog items, IAuctionCatalog names, IQuestService quests = null, IStatService stats = null,
        ICreatureEvents creatures = null, ICastInterrupts states = null, IWarpService warp = null,
        IInventoryChangeFeed feed = null, IOptions<NpcScriptOptions> options = null, ILevelingService leveling = null)
    {
        _scripts = scripts; _repositories = repositories; _gate = gate; _items = items; _names = names;
        _quests = quests; _stats = stats; _creatures = creatures; _states = states; _warp = warp; _feed = feed;
        _options = options?.Value ?? new NpcScriptOptions();
        _leveling = leveling;
    }

    public bool Handles(string function) => _scripts.Handles(function)
        || function.StartsWith("NPC_all_2012_EnchantEvent_b_", StringComparison.Ordinal)
        || function == "set_flag";

    public Task<NpcScriptPage> RunAsync(GameClient client, uint npcHandle, long revision, string trigger) =>
        RunAsync(client, npcHandle, revision, trigger, dialog: true);

    public Task RunPropScriptAsync(GameClient client, string script) =>
        Handles(NpcDialogService.ReadFunctionName(script) ?? string.Empty)
            ? RunAsync(client, 0, 0, script, dialog: false)
            : Task.CompletedTask;

    /// <summary>
    /// <paramref name="dialog"/>: the script answers an NPC page, which must still be the one shown when it starts and
    /// when it commits. A prop's script has no page: the NPC id is 0 and nothing is shown.
    /// </summary>
    private async Task<NpcScriptPage> RunAsync(GameClient client, uint npcHandle, long revision, string trigger, bool dialog)
    {
        var info = client.ConnectionInfo;
        // set_quest_status takes the character gate itself: it runs once the script's own gate is released.
        var questStatuses = new List<(int Code, int Index, int Value)>();
        var committed = false;
        try
        {
            var result = await _gate.RunAsync(info.CharacterName, async () =>
            {
                long npcId = 0;
                if (dialog)
                    lock (info.NpcVisibilityLock)
                        if (info.NpcDialogHandle != npcHandle || info.NpcDialogRevision != revision
                            || !info.SpawnedNpcIdsByHandle.TryGetValue(npcHandle, out npcId)) return null;
                using var repository = _repositories.Create();
                var character = await repository.GetCharacterByNameWithItemsAsync(info.CharacterName);
                if (character is null) return null;
                var function = NpcDialogService.ReadFunctionName(trigger);
                var flags = ReadFlags(character.FlagList);
                // The menu checks these too; repeat under the character gate to prevent replay after a new contact.
                var giftFlag = function switch { "second_present_weapon" => "q18", "second_present_armor" => "q19",
                    "dormancyuser_item_weapon_receive" => "rental_weapon", "dormancyuser_item_armor_receive" => "rental_armor", _ => null };
                if (giftFlag is not null && flags.GetValueOrDefault(giftFlag) == "1") return null;
                if ((function.StartsWith("dormancyuser_item_", StringComparison.Ordinal) || function.StartsWith("second_present", StringComparison.Ordinal))
                    && flags.GetValueOrDefault("event_code") != "1") return null;

                var script = _scripts.Create();
                var page = new NpcDialogDefinition();
                var show = false;
                var includeQuests = true;
                var dirty = false;
                var inventory = (character.Items ??= new List<ItemEntity>()).ToList();
                var changed = new HashSet<ItemEntity>();
                var created = new List<ItemEntity>();
                var messages = new List<string>();
                var buffs = new List<(int Id, int Level, uint Duration)>();
                (float X, float Y)? destination = null;
                var goldBefore = info.CharacterGold;
                var gold = goldBefore;
                long expBefore, jpBefore;
                lock (info.ProgressLock) { expBefore = info.CharacterExp; jpBefore = info.CharacterJp; }
                var exp = expBefore; var jp = jpBefore;

                ItemEntity Item(long handle) => inventory.SingleOrDefault(i => i.Id == handle && i.Amount > 0)
                    ?? throw new InvalidOperationException("NPC item handle is no longer carried");
                bool Erasable(ItemEntity item) => item.WearInfo == ItemWearType.None && item.EquippedBySummonId is null
                    && item.Summon is null && !(character.BeltItemIds ?? Array.Empty<long>()).Contains(item.Id)
                    && !(character.SummonSlotItemIds ?? Array.Empty<long>()).Contains(item.Id);
                int MaxDurability(ItemEntity item) => _items.TryGetFields(item.ItemResourceId, out var row)
                    ? Math.Max(0, row.Mix?.MaxEtherealDurability ?? 0) : 0;
                void Mutate(long handle, Action<ItemEntity> mutation) { var item = Item(handle); mutation(item); changed.Add(item); dirty = true; }
                void Bind(string name, Func<CallbackArguments, DynValue> action) =>
                    script.Globals[name] = DynValue.NewCallback((_, args) => action(args));
                static long Number(CallbackArguments a, int index) => checked((long)a[index].CastToNumber().GetValueOrDefault());
                static string Text(CallbackArguments a, int index) => a[index].CastToString() ?? string.Empty;
                static DynValue Nil() => DynValue.Nil;
                static DynValue Numeric(double value) => DynValue.NewNumber(value);

                Bind("dlg_title", a => { page.Title = Text(a, 0); return Nil(); });
                Bind("dlg_text", a => { page.Text = Text(a, 0); includeQuests = true; return Nil(); });
                Bind("dlg_text_without_quest_menu", a => { page.Text = Text(a, 0); includeQuests = false; return Nil(); });
                Bind("dlg_menu", a => { page.Menu.Add(new NpcDialogMenuEntry { Label = Text(a, 0), Trigger = Text(a, 1).Trim() }); return Nil(); });
                Bind("dlg_show", _ => { show = true; return Nil(); });
                foreach (var name in new[] { "cprint", "message" }) Bind(name, a => { messages.Add(Text(a, 0)); return Nil(); });
                Bind("sconv", a => DynValue.NewString(string.Join("\v", Enumerable.Range(0, a.Count).Select(i => Text(a, i)))));
                Bind("get_npc_id", _ => Numeric(npcId));
                Bind("get_local_info", _ => Numeric(_options.LocalInfo));
                Bind("get_env", _ => Numeric(0));
                Bind("is_premium", _ => DynValue.False);
                Bind("get_flag", a => {
                    var key = Text(a, 0);
                    var value = flags.GetValueOrDefault(key) ?? (key is "q18" or "q19" or "rental_weapon" or "rental_armor" ? "0" : string.Empty);
                    // The official flags API exposes numeric values as numbers; an unset flag is an empty string.
                    return double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var n) ? Numeric(n) : DynValue.NewString(value);
                });
                Bind("set_flag", a => { flags[Text(a, 0)] = Text(a, 1); dirty = true; return Nil(); });
                Bind("del_flag", a => { dirty |= flags.Remove(Text(a, 0)); return Nil(); });
                DynValue Value(CallbackArguments a) => Text(a, 0) switch {
                    "name" => DynValue.NewString(info.CharacterName), "level" => Numeric(info.CharacterLevel),
                    "job" => Numeric(info.CharacterJob), "job_level" => Numeric(info.CharacterJobLevel),
                    "job_depth" => Numeric(JobDepths.ToIndex((short)character.JobDepth, true)), "race" => Numeric(info.CharacterRace),
                    "layer" => Numeric(info.Layer), "gold" => Numeric(gold), "exp" => Numeric(exp), "jp" => Numeric(jp),
                    "event_code" => Numeric(flags.GetValueOrDefault("event_code") == "1" ? 1 : 0), _ => Numeric(0) };
                Bind("get_value", Value); Bind("gv", Value);
                Bind("set_value", a => {
                    var value = Number(a, 1);
                    if (value < 0) throw new InvalidOperationException("Invalid NPC property value");
                    switch (Text(a, 0)) {
                        case "gold": if (value > GoldRules.MaxCarried) throw new InvalidOperationException("Invalid NPC gold balance"); gold = value; break;
                        case "exp": exp = value; break;
                        case "jp": jp = value; break;
                        default: throw new InvalidOperationException("Use the job change service for job changes");
                    }
                    dirty = true; return Nil(); });
                Bind("set_quest_status", a => { questStatuses.Add(((int)Number(a, 0), (int)Number(a, 1), (int)Number(a, 2))); return Nil(); });
                Bind("get_quest_status", a => Numeric(_quests?.GetQuestStatusAsync(client, (int)Number(a, 0), (int)Number(a, 1)).GetAwaiter().GetResult() ?? 0));
                Bind("get_quest_progress", a => Numeric(_quests?.GetQuestProgressAsync(client, (int)Number(a, 0)).GetAwaiter().GetResult() ?? -1));
                Bind("is_able_to_jobchange", _ => DynValue.NewBoolean(info.CharacterJobLevel >= 10));
                Bind("find_item", a => Numeric(inventory.Where(i => i.ItemResourceId == Number(a, 0)).Sum(i => i.Amount)));
                Bind("has_item", a => Numeric(inventory.Where(i => i.ItemResourceId == Number(a, 0)).Sum(i => i.Amount)));
                Bind("get_item_handle", a => Numeric(inventory.FirstOrDefault(i => i.ItemResourceId == Number(a, 0) && i.Amount > 0)?.Id ?? 0));
                Bind("get_item_handle_list", a => {
                    var table = new Table(script); var index = 1;
                    foreach (var item in inventory.Where(i => i.ItemResourceId == Number(a, 0) && i.Amount > 0)) table[index++] = Numeric(item.Id);
                    return DynValue.NewTable(table);
                });
                Bind("get_wear_item_handle", a => Numeric(inventory.FirstOrDefault(i => ItemWearRules.IsWornByPlayerAt(i, (ItemWearType)Number(a, 0)))?.Id ?? 0));
                // The official script API answers numbers (SCRIPT_IsErasableItem: 0 for an unknown handle).
                Bind("is_erasable_item", a => Numeric(inventory.SingleOrDefault(i => i.Id == Number(a, 0) && i.Amount > 0) is { } item && Erasable(item) ? 1 : 0));
                Bind("get_item_code", a => Numeric(Number(a, 0) == 0 ? 0 : Item(Number(a, 0)).ItemResourceId));
                Bind("get_item_enhance", a => Numeric(Item(Number(a, 0)).Enhance));
                Bind("get_item_level", a => Numeric(Item(Number(a, 0)).Level));
                Bind("get_item_rank", a => Numeric(_items.TryGetFields(Item(Number(a, 0)).ItemResourceId, out var row) ? row.Rank : 0));
                Bind("get_item_price", a => Numeric(_items.TryGetFields(Item(Number(a, 0)).ItemResourceId, out var row) ? row.Mix?.Price ?? 0 : 0));
                Bind("get_item_name_id", a => Numeric(_names.TryGetItem((int)Number(a, 0), out var row) ? row.NameId : 0));
                Bind("get_item_name_by_code", a => DynValue.NewString(_names.TryGetItem((int)Number(a, 0), out var row) ? $"@{row.NameId}" : string.Empty));
                Bind("get_item_ethereal_durability", a => Numeric(Item(Number(a, 0)).EtherealDurability));
                Bind("get_max_item_ethereal_durability", a => Numeric(MaxDurability(Item(Number(a, 0)))));
                Bind("set_item_ethereal_durability", a => { Mutate(Number(a, 0), i => i.EtherealDurability = (int)Math.Clamp(Number(a, 1), 0, MaxDurability(i))); return Nil(); });
                Bind("set_item_level", a => { Mutate(Number(a, 0), i => i.Level = checked((uint)Number(a, 1))); return Numeric(Number(a, 1)); });
                Bind("set_item_enhance", a => { Mutate(Number(a, 0), i => i.Enhance = checked((uint)Number(a, 1))); return Numeric(Number(a, 1)); });
                Bind("get_item_random_option", a => {
                    var item = Item(Number(a, 0)); var index = checked((int)Number(a, 1)) - 1;
                    if (index is < 0 or > 1) throw new InvalidOperationException("Invalid random option index");
                    var table = new Table(script);
                    table[1] = Numeric(item.RandomOptionTypes?.ElementAtOrDefault(index) ?? 0);
                    table[2] = Numeric(item.RandomOptionVars?.ElementAtOrDefault(index) ?? 0);
                    table[3] = Numeric((double)(item.RandomOptionValues?.ElementAtOrDefault(index) ?? 0));
                    return DynValue.NewTable(table);
                });
                Bind("set_item_random_option", a => {
                    var index = checked((int)Number(a, 1)) - 1;
                    if (index is < 0 or > 1) throw new InvalidOperationException("Invalid random option index");
                    Mutate(Number(a, 0), item => {
                        item.RandomOptionTypes = Two(item.RandomOptionTypes); item.RandomOptionVars = Two(item.RandomOptionVars);
                        item.RandomOptionValues = Two(item.RandomOptionValues);
                        item.RandomOptionTypes[index] = checked((int)Number(a, 2)); item.RandomOptionVars[index] = checked((int)Number(a, 3));
                        item.RandomOptionValues[index] = (decimal)a[4].CastToNumber().GetValueOrDefault();
                    }); return Nil();
                });
                Bind("change_item_code", a => {
                    var code = checked((int)Number(a, 1));
                    if (!_names.TryGetItem(code, out _)) throw new InvalidOperationException("Replacement item is not available in Epic 7.3");
                    Mutate(Number(a, 0), i => i.ItemResourceId = code); return Numeric(1);
                });
                Bind("delete_item", a => {
                    var item = Item(Number(a, 0)); var count = Number(a, 1);
                    if (count <= 0 || item.Amount < count || !Erasable(item)) throw new InvalidOperationException("NPC exchange material is unavailable");
                    item.Amount -= count; changed.Add(item); dirty = true;
                    if (item.Amount == 0) { repository.DeleteItem(item); inventory.Remove(item); }
                    // The scripts test "delete_item(...) == 1"; a refusal throws rather than answer 0, so the whole
                    // exchange is rolled back instead of trusting every script to check.
                    return Numeric(1);
                });
                Bind("insert_item", a => {
                    var code = checked((int)Number(a, 0)); var count = Number(a, 1);
                    if (count <= 0 || !_names.TryGetItem(code, out _)) throw new InvalidOperationException("Reward item is not available in Epic 7.3");
                    var item = new ItemEntity { CharacterId = character.Id, ItemResourceId = code, Amount = count,
                        Level = 1, WearInfo = ItemWearType.None, GenerateBySource = ItemGenerateSource.Script,
                        Idx = inventory.Select(i => i.Idx).DefaultIfEmpty(-1).Max() + 1, SocketItemIds = new long[4] };
                    EtherealWearRules.Initialize(item, _items);
                    character.Items.Add(item); inventory.Add(item); created.Add(item); changed.Add(item); dirty = true;
                    return Numeric(1);
                });
                Bind("get_state_level", a => {
                    lock (info.BuffLock) return Numeric(info.ActiveBuffs.Where(b => b.StateId == Number(a, 0)
                        && (b.EndTick == uint.MaxValue || unchecked((int)(b.EndTick - ServerClock.Now)) > 0)).Select(b => (int)b.StateLevel).DefaultIfEmpty(0).Max());
                });
                Bind("add_state", a => { if (_states is null) throw new InvalidOperationException("State relay is unavailable");
                    buffs.Add(((int)Number(a, 0), (int)Number(a, 1), checked((uint)Number(a, 2)))); return Nil(); });
                Bind("warp", a => { if (_warp is null) throw new InvalidOperationException("Warp service is unavailable");
                    destination = ((float)a[0].Number, (float)a[1].Number); return Nil(); });
                // Saving remains atomic at the end of the function, including legacy explicit save() calls.
                Bind("save", _ => Nil()); Bind("update_gold_chaos", _ => Nil());
                // Lua 5.0's table.getn is still used by the card exchange.
                var tableModule = script.Globals.Get("table").Table;
                tableModule["getn"] = DynValue.NewCallback((_, a) => Numeric(a[0].Table.Length));

                // Coroutine instruction budget bounds server script execution; no untrusted script is loaded.
                var entry = script.LoadString(trigger);
                var coroutine = script.CreateCoroutine(entry).Coroutine;
                coroutine.AutoYieldCounter = 100_000;
                coroutine.Resume();
                if (coroutine.State != CoroutineState.Dead) throw new InvalidOperationException("NPC script instruction budget exceeded");

                character.FlagList = flags.Select(f => $"{f.Key}:{f.Value}").ToArray();
                if (dialog)
                    lock (info.NpcVisibilityLock)
                        if (info.NpcDialogHandle != npcHandle || info.NpcDialogRevision != revision
                            || !info.SpawnedNpcIdsByHandle.ContainsKey(npcHandle)) return null;
                var debit = goldBefore - gold;
                var expGain = checked(exp - expBefore); var jpGain = checked(jp - jpBefore);
                if (expGain < 0 || jpGain < 0) throw new InvalidOperationException("NPC experience must be a reward");
                lock (info.ProgressLock)
                {
                    character.Exp = checked(info.CharacterExp + expGain);
                    character.Jp = checked(info.CharacterJp + jpGain);
                    character.Lv = info.CharacterLevel;
                    if (expGain > 0)
                        while (_leveling?.TryGetExperienceFor(character.Lv + 1, out var required) == true && character.Exp >= required)
                            character.Lv++;
                }
                if (debit > 0 && !info.TryDebitGold(debit)) throw new InvalidOperationException("NPC gold balance changed");
                if (debit < 0 && !info.TryCreditGold(-debit, GoldRules.MaxCarried)) throw new InvalidOperationException("NPC gold limit exceeded");
                try { if (dirty) { character.Gold = info.CharacterGold; await repository.SaveChangesAsync(); } }
                catch { info.AddGold(debit); throw; }
                committed = true;
                if (expGain > 0 || jpGain > 0)
                    lock (info.ProgressLock)
                    {
                        info.CharacterExp = checked(info.CharacterExp + expGain); info.CharacterJp = checked(info.CharacterJp + jpGain);
                        _leveling?.ApplyExperience(client);
                        client.Connection.Send(GameCharacterPackets.BuildExpUpdate(info.CharacterHandle, info.CharacterExp, info.CharacterJp));
                    }
                if (changed.Count > 0)
                {
                    foreach (var item in changed.Where(i => i.Amount == 0)) client.Connection.Send(GameCharacterPackets.BuildDestroyItem((uint)item.Id));
                    var carried = changed.Where(i => i.Amount > 0).ToArray();
                    if (carried.Length > 0)
                        foreach (var frame in GameCharacterPackets.BuildInventory(carried)) client.Connection.Send(frame);
                }
                if (debit != 0) client.SendVitalProperty(GameStatPackets.BuildProperty(info.CharacterHandle, "gold", info.CharacterGold));
                foreach (var message in messages) client.Connection.Send(GameChatPackets.BuildChat("@SCRIPT", TownTeleportRules.ChatNpc, message));
                foreach (var state in buffs) _states.ApplyState(client, state.Id, state.Level, state.Duration);
                if (changed.Count > 0)
                {
                    _creatures?.EquipmentDurabilityChanged(client, changed.ToArray());
                    EquipmentStatRefresh.Send(client, _stats, inventory);
                    _feed?.Publish(info.CharacterName);
                }
                if (destination is { } point) _warp.Warp(client, point.X, point.Y);
                return show && dialog ? new NpcScriptPage(page, includeQuests) : null;
            });

            foreach (var (code, index, value) in committed ? questStatuses : new())
                if (_quests is null || !await _quests.SetQuestStatusAsync(client, code, index, value))
                    Log.Warning("NPC script quest status {Code}/{Index} = {Value} not applied for {Character}", code, index, value, info.CharacterName);
            return result;
        }
        catch (Exception exception)
        {
            Log.Warning(exception, "NPC script {Function} rejected for {Character}", NpcDialogService.ReadFunctionName(trigger), info.CharacterName);
            // A prop's script fails silently, like a Lua error under UseProp.
            if (dialog)
                client.Connection.Send(GameChatPackets.BuildChat("@SCRIPT", TownTeleportRules.ChatNpc,
                    "Cette opération n’est pas disponible pour votre personnage ou vos objets."));
            return null;
        }
    }

    private static Dictionary<string, string> ReadFlags(string[] entries) => (entries ?? Array.Empty<string>())
        .Where(e => e?.Contains(':') == true).Select(e => e.Split(':', 2)).GroupBy(e => e[0], StringComparer.Ordinal)
        .ToDictionary(g => g.Key, g => g.Last()[1], StringComparer.Ordinal);
    private static T[] Two<T>(T[] source) { var result = new T[2]; if (source is not null) Array.Copy(source, result, Math.Min(source.Length, 2)); return result; }
}
