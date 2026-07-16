namespace Navislamia.Game.Services.Stats;

public readonly record struct StatEffect(StatTarget Target, float Value, bool IsPercent);
