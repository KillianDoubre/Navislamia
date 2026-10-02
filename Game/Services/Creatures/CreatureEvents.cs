using Navislamia.Game.Network.Clients;

namespace Navislamia.Game.Services.Creatures;

/// <summary>
/// What the combat path tells the creatures, without depending on them: <see cref="CreatureService"/> depends on
/// <see cref="Interfaces.ICombatService"/>, so the reverse would be a DI cycle that only fails at runtime. The service
/// attaches itself, like <see cref="Casting.CastInterrupts"/>.
/// </summary>
public interface ICreatureEvents
{
    /// <summary>A player (or its summon) hit a monster: a tamer's hit renews the taming window.</summary>
    void MonsterDamaged(GameClient attacker, long instanceId);

    /// <summary>
    /// A monster died. True when a taming succeeded on it, which takes the loot away
    /// (<c>StructMonster::onDead</c>, <c>if( !m_bTamedSuccess )</c> around gold, chaos and items).
    /// </summary>
    bool MonsterKilled(long instanceId);
}

public interface ICreatureEventListener
{
    void OnMonsterDamaged(GameClient attacker, long instanceId);

    bool OnMonsterKilled(long instanceId);
}

public sealed class CreatureEvents : ICreatureEvents
{
    private volatile ICreatureEventListener _listener;

    public void Attach(ICreatureEventListener listener) => _listener = listener;

    public void MonsterDamaged(GameClient attacker, long instanceId) => _listener?.OnMonsterDamaged(attacker, instanceId);

    public bool MonsterKilled(long instanceId) => _listener?.OnMonsterKilled(instanceId) == true;
}
