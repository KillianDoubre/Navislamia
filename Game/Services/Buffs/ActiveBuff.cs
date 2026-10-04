namespace Navislamia.Game.Services.Buffs;

public readonly record struct ActiveBuff(
    ushort StateHandle,
    int StateId,
    int SkillId,
    int StateLevel,
    uint StartTick,
    uint EndTick,
    uint SourceHandle = 0,
    bool AuraProjection = false, StatePulse Pulse = null);
