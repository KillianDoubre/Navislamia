namespace Navislamia.Game.Services;

/// <summary>
/// One monster in the world, frozen from its <c>tf_monster_resource</c> row at startup.
/// </summary>
/// <remarks>
/// <see cref="TamingId"/> and <see cref="TamingPercentage"/> are that row's taming columns, carried so the
/// taming path can reach them from <see cref="MonsterWorldState.TryGetInstance"/> without a database round
/// trip (docs/packet-specs/socle-apprivoisement-invocation.md §11, étape 0.5). Nothing reads
/// <see cref="TamingPercentage"/> yet: the draw at the monster's death is étape 1, and it also needs the
/// card flags and the loot skip that do not exist here. <see cref="TamingId"/> is what
/// <see cref="TamingRules.IsTamable"/> judges.
/// <para>
/// <see cref="Hp"/> is the real maximum (<see cref="MonsterCombatStats.MaxHp"/>), not the <c>hp</c> column:
/// the column is only the resource's adjustment on top of level and vitality. <see cref="Combat"/> is shared
/// by every instance of the resource. <see cref="SkillLinkId"/> is <c>monster_skill_link_id</c>, the key of
/// its skills in <see cref="MonsterSkillCatalog"/>. <see cref="MonsterGroup"/> and <see cref="GroupFirstAttack"/>
/// are <c>monster_group</c> and <c>f_group_first_attack</c>, read by the official group aggro
/// (<see cref="MonsterAiRules.JoinsGroupAttack"/>). <see cref="Rewards"/> is the reward block of the same
/// row, read by <see cref="MonsterRewardRules"/> at the monster's death (docs/packet-specs/
/// socle-recompenses-monstres.md §9.1).
/// </para>
/// </remarks>
public readonly record struct MonsterInstance(
    long InstanceId,
    int MonsterId,
    float X,
    float Y,
    float Z,
    int Level,
    int Hp,
    byte Race,
    float FaceDirection,
    bool FirstAttack,
    int VisibleRange,
    int ChaseRange,
    float AttackRange,
    float Size,
    float Scale,
    int TamingId,
    decimal TamingPercentage,
    MonsterCombatStats Combat = null,
    int SkillLinkId = 0,
    int MonsterGroup = 0,
    bool GroupFirstAttack = false,
    byte Layer = 0,
    MonsterRewardProfile Rewards = default,
    bool IsDungeonRaidMonster = false,
    int MonsterType = 0);
