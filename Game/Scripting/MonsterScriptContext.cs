using System;

namespace Navislamia.Game.Scripting;

/// <summary>One trigger call, scoped to its monster and its observer's handles.</summary>
public sealed class MonsterScriptContext
{
    public uint MonsterHandle { get; init; }
    public uint TargetHandle { get; init; }
    public int MonsterId { get; init; }
    public int TriggerIndex { get; init; }
    public float X { get; init; }
    public float Y { get; init; }
    public int Layer { get; init; }
    public bool IsDungeonRaidMonster { get; init; }
    public QuestScriptContext PlayerContext { get; init; }
    public Func<int, uint, float?, float?, bool> CastSkill { get; init; }
    public Action<int, int, uint, uint> AddState { get; init; }
    public Action<int, int> RespawnNearMonster { get; init; }
    public Func<bool, uint, int> SetAutoUser { get; init; }
}
