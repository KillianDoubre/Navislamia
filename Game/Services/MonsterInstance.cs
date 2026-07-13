namespace Navislamia.Game.Services;

public readonly record struct MonsterInstance(
    long InstanceId,
    int MonsterId,
    float X,
    float Y,
    float Z,
    int Level,
    int Hp,
    byte Race);
