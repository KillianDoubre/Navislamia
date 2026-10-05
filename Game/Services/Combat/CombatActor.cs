using Navislamia.Game.Network.Clients;

namespace Navislamia.Game.Services.Combat;

/// <summary>Owner is the connection context; a monster id is stable across per-viewer wire handles.</summary>
public readonly record struct CombatActor(GameClient Owner, uint SummonHandle = 0, long MonsterId = -1)
{
    public bool IsMonster => MonsterId >= 0;
    public bool IsSummon => !IsMonster && SummonHandle != 0;
    public uint Handle => IsMonster ? Owner.ConnectionInfo.GetMonsterHandle(MonsterId)
        : IsSummon ? SummonHandle : Owner.ConnectionInfo.CharacterHandle;
}
