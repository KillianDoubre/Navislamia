namespace Navislamia.Game.Services.Stats;

public readonly record struct StateEffectTemplate(StatTarget Target, float Base, float PerLevel, bool IsPercent)
{
    public StatEffect Resolve(float stateLevel)
    {
        return new StatEffect(Target, Base + PerLevel * stateLevel, IsPercent);
    }
}
