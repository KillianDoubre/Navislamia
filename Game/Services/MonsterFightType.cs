namespace Navislamia.Game.Services;

/// <summary>
/// <c>MonsterBase::fight_type</c> and the two official rules that read it (<c>StructMonster::IsAttackable</c> and
/// <c>StructMonster::IsMovable</c>, StructMonster.cpp:2363-2411): an environment object, a dungeon connector, a
/// trap, an auto trap, a runaway and an immortal dummy never attack; a connector, a trap, an auto trap, a
/// not-movable monster and an immortal dummy never move. The towns' training scarecrows are traps (6).
/// </summary>
public static class MonsterFightType
{
    public const int Normal = 0;
    public const int Environment = 1;
    public const int DungeonConnector = 2;
    public const int Boss = 3;
    public const int Agent = 4;
    public const int AutoTrap = 5;
    public const int Trap = 6;
    public const int NotMovable = 7;
    public const int Runaway = 8;
    public const int ImmortalDummy = 9;

    /// <summary><c>StructMonster::IsAttackable</c>: whether the monster may attack at all.</summary>
    public static bool CanAttack(int fightType) =>
        fightType is not (Environment or DungeonConnector or AutoTrap or Trap or Runaway or ImmortalDummy);

    /// <summary><c>StructMonster::IsMovable</c>: whether the monster may walk (wander, chase, go home).</summary>
    public static bool IsMovable(int fightType) =>
        fightType is not (DungeonConnector or AutoTrap or Trap or NotMovable or ImmortalDummy);
}
