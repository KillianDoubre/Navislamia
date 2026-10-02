using System;
using System.IO;
using System.Linq;
using System.Reflection;
using Microsoft.Extensions.Logging;
using MoonSharp.Interpreter;
using Navislamia.Game.Scripting.Functions;
using Navislamia.Scripting.Functions;

namespace Navislamia.Game.Scripting;

public class ScriptService : IScriptService
{
    private readonly Script _luaVm = new();
    private readonly object _gate = new();
    private readonly ILogger<ScriptService> _logger;
    private MonsterScriptContext _monster;
    private QuestScriptContext _quest;
    public static ScriptService Instance { get; private set; }

    public ScriptService(ILogger<ScriptService> logger)
    {
        _logger = logger;
        Instance = this;
        RegisterFunction("call_lc_In", MiscFunc.SetCurrentLocationId);
        RegisterFunction("GetMonsterId", MonsterFunc.GetMonsterId);
        RegisterFunction("get_value", Player.get_value);
        _luaVm.Globals["get_quest_progress"] = DynValue.NewCallback((_, args) =>
            DynValue.NewNumber(_quest is { } c && args.Count >= 1
                && (args.Count == 1 || args[1].Number == c.PlayerHandle)
                    ? c.GetProgress?.Invoke((int)args[0].Number) ?? -1 : -1));
        _luaVm.Globals["set_quest_status"] = DynValue.NewCallback((_, args) =>
            DynValue.NewNumber(_quest is { } c && args.Count >= 3
                && (args.Count == 3 || args[3].Number == c.PlayerHandle)
                && (c.SetStatus?.Invoke((int)args[0].Number, (int)args[1].Number, (int)args[2].Number) ?? false) ? 1 : -1));
        _luaVm.Globals["get_monster_id"] = DynValue.NewCallback((_, args) =>
            DynValue.NewNumber(_monster is { } c && args.Count > 0 && args[0].Number == c.MonsterHandle ? c.MonsterId : 0));
        _luaVm.Globals["set_title_condition"] = DynValue.NewCallback((_, args) =>
            DynValue.NewNumber(_quest is { } c && args.Count >= 2
                && (args.Count == 2 || args[2].Number == c.PlayerHandle)
                && (c.SetTitleCondition?.Invoke((int)args[0].Number, (long)args[1].Number) ?? false) ? 1 : -1));
        _luaVm.Globals["monster_skill_cast"] = DynValue.NewCallback((_, args) =>
        {
            if (_monster is not { } c || args.Count < 2 || args[1].Number != c.MonsterHandle) return DynValue.NewBoolean(false);
            var target = args.Count == 3 ? (uint)args[2].Number : 0u;
            var ok = c.CastSkill?.Invoke((int)args[0].Number, target,
                args.Count == 4 ? (float)args[2].Number : null,
                args.Count == 4 ? (float)args[3].Number : null) ?? false;
            return DynValue.NewBoolean(ok);
        });
        _luaVm.Globals["add_state"] = DynValue.NewCallback((_, args) =>
        {
            if (_monster is { } c && args.Count == 4)
                c.AddState?.Invoke((int)args[0].Number, (int)args[1].Number,
                    (uint)Math.Max(0, args[2].Number), (uint)args[3].Number);
            return DynValue.Nil;
        });
        _luaVm.Globals["respawn_near_monster"] = DynValue.NewCallback((_, args) =>
        {
            if (_monster is { } c && args.Count == 3 && args[0].Number == c.MonsterHandle)
                (c.RespawnNearMonster ?? throw new ScriptRuntimeException("No monster spawn context"))
                    .Invoke((int)args[1].Number, (int)args[2].Number);
            return DynValue.Nil;
        });
        _luaVm.Globals["set_auto_user"] = DynValue.NewCallback((_, args) =>
        {
            if (_monster is not { } c || args.Count != 2) return DynValue.Nil;
            return DynValue.NewNumber((c.SetAutoUser ?? throw new ScriptRuntimeException("No player persistence context"))
                .Invoke(args[0].Number != 0, (uint)args[1].Number));
        });

        using var profiles = typeof(ScriptService).Assembly.GetManifestResourceStream("Navislamia.MonsterTriggerProfiles.lua");
        if (profiles is not null) using (var reader = new StreamReader(profiles)) _luaVm.DoString(reader.ReadToEnd());
        using var embedded = typeof(ScriptService).Assembly.GetManifestResourceStream("Navislamia.MonsterTriggers.lua");
        if (embedded is not null) using (var reader = new StreamReader(embedded)) _luaVm.DoString(reader.ReadToEnd());
    }

    public void Start()
    {
        var directory = Path.Combine(Path.GetDirectoryName(Assembly.GetEntryAssembly()?.Location) ?? ".", "Scripts");
        Directory.CreateDirectory(directory);
        var count = 0;
        lock (_gate)
            foreach (var path in Directory.EnumerateFiles(directory, "*.lua", SearchOption.AllDirectories).OrderBy(p => p, StringComparer.Ordinal))
            {
                try { _luaVm.DoFile(path); count++; }
                catch (Exception ex) { _logger.LogError(ex, "Could not load Lua script {Path}", path); }
            }
        _logger.LogDebug("Loaded {Count} Lua scripts", count);
    }

    public void RegisterFunction(string name, Func<object[], int> function)
    {
        lock (_gate) _luaVm.Globals[name] = DynValue.NewCallback((_, args) =>
            DynValue.NewNumber(function(Enumerable.Range(0, args.Count).Select(i => args[i].ToObject()).ToArray())));
    }
    public void RegisterFunction(string name, Action<object[]> function)
    {
        lock (_gate) _luaVm.Globals[name] = DynValue.NewCallback((_, args) =>
        { function(Enumerable.Range(0, args.Count).Select(i => args[i].ToObject()).ToArray()); return DynValue.Nil; });
    }
    public int RunString(string script)
    {
        if (string.IsNullOrEmpty(script)) return 0;
        lock (_gate)
        {
            try { _luaVm.DoString(script); return 1; }
            catch (Exception ex) { _logger.LogError(ex, "Lua execution failed"); return 0; }
        }
    }
    public bool RunMonsterTrigger(string function, MonsterScriptContext context)
    {
        lock (_gate)
        {
            var previous = _monster;
            var previousPlayer = _quest;
            try
            {
                var callback = _luaVm.Globals.Get(function);
                if (callback.Type is not (DataType.Function or DataType.ClrFunction))
                { _logger.LogWarning("Unknown monster Lua trigger {Function}", function); return false; }
                _monster = context;
                _quest = context.PlayerContext;
                _luaVm.Call(callback, context.MonsterHandle, context.TargetHandle, context.TriggerIndex,
                    context.X, context.Y, context.Layer, context.IsDungeonRaidMonster ? 1 : 0);
                return true;
            }
            catch (Exception ex) { _logger.LogError(ex, "Monster Lua trigger {Function} failed for {MonsterId}", function, context.MonsterId); return false; }
            finally { _monster = previous; _quest = previousPlayer; }
        }
    }

    public int RunQuestScript(string script, QuestScriptContext context)
    {
        lock (_gate)
        {
            var previous = _quest;
            try { _quest = context; _luaVm.DoString(script); return 1; }
            catch (Exception ex) { _logger.LogError(ex, "Quest Lua execution failed"); return 0; }
            finally { _quest = previous; }
        }
    }
}
