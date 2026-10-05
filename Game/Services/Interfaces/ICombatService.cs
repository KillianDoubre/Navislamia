using Navislamia.Game.Network.Clients;

namespace Navislamia.Game.Services;

public interface ICombatService
{
    void NotifyHit(Combat.CombatActor attacker, Combat.CombatActor target, HitResult hit,
        uint attackType = Combat.EnergyProcs.NormalAttack, int element = 0, bool attackProcs = true) { }
    void NotifyDeath(Combat.CombatActor killer, Combat.CombatActor victim) { }

    /// <summary>The duel/PK relation used to exclude hostile players from beneficial skills.</summary>
    bool ArePlayerEnemies(GameClient attacker, GameClient target) => false;
    void OnPkEnabled(GameClient client) { }
    HitResult RollPlayerHit(GameClient attacker, GameClient target, float damage, DamageKind kind,
        int accuracy, int critical, int element = 0);
    HitResult RollSummonHitOnPlayer(GameClient master, GameClient target, Stats.StatBlock stats, int level,
        float damage, DamageKind kind, int accuracy, int critical, int element = 0) => new(0, HitFlags.Miss);
    int DamagePlayerByPlayer(GameClient attacker, GameClient target, int damage, bool magical = false);
    int DamagePlayerBySummon(GameClient master, GameClient target, uint summonHandle, int damage, bool magical = false) =>
        DamagePlayerByPlayer(master, target, damage, magical);
    void StartAttack(GameClient client, uint targetHandle);
    void StopAttack(GameClient client);

    /// <summary>
    /// Drops every monster's aggro on a leaving player, so a disconnect or a warp leaves nothing
    /// chasing a ghost. The monsters return home on the next AI tick.
    /// </summary>
    void DropAggro(GameClient client);

    /// <summary>
    /// Rolls one hit of the player on a monster through <see cref="CombatFormulas.Resolve"/>: the
    /// player's live stats against the monster's, its states included. An auto-attack passes the attack
    /// point; a skill passes its own base damage and bonuses, so both follow the same rule.
    /// </summary>
    HitResult RollHit(GameClient client, long instanceId, float baseDamage, DamageKind kind, int accuracyBonus,
        int criticalBonus);

    /// <summary>A skill hit against a monster, including resistance to its elemental type.</summary>
    HitResult RollHit(GameClient client, long instanceId, float baseDamage, DamageKind kind,
        int accuracyBonus, int criticalBonus, int element);

    /// <summary>A monster skill hit against a player, including resistance to its elemental type.</summary>
    HitResult RollMonsterHit(long instanceId, GameClient target, float baseDamage, DamageKind kind,
        int accuracyBonus, int criticalBonus, int element);

    /// <summary>
    /// Rolls one swing of a monster on a player, and gives the monster's swing interval in ar_time ticks
    /// (its attack speed). A player under <c>/immortal</c> takes 0.
    /// </summary>
    HitResult RollMonsterHit(long instanceId, GameClient target, out uint intervalTicks);

    /// <summary>
    /// Rolls one swing of a monster on a creature that is not a player — a summon —, from that creature's stats and
    /// level, by the same rule as a swing on a player.
    /// </summary>
    /// <summary>The same, the raw damage multiplied by the defender's hunting expertise against the monster's type.</summary>
    HitResult RollMonsterHitOn(long instanceId, Stats.StatBlock defender, int defenderLevel, float damageTaken,
        out uint intervalTicks) => RollMonsterHitOn(instanceId, defender, defenderLevel, out intervalTicks);

    HitResult RollMonsterHitOn(long instanceId, Stats.StatBlock defender, int defenderLevel, out uint intervalTicks)
    {
        intervalTicks = CombatFormulas.AttackIntervalTicks(100f);
        return new HitResult(0, HitFlags.Miss);
    }

    /// <summary>
    /// Rolls one hit of a monster's skill on a player: <paramref name="baseDamage"/> and the skill's bonuses
    /// through the same rule as a swing. A player under <c>/immortal</c> takes 0.
    /// </summary>
    HitResult RollMonsterHit(long instanceId, GameClient target, float baseDamage, DamageKind kind,
        int accuracyBonus, int criticalBonus);

    /// <summary>
    /// The one place a monster's damage lands on a player: HP (never below 0), the <c>hp</c> property, and
    /// on the hit that brings it to 0 the end of its attack and the death penalty
    /// (<see cref="ILevelingService.ApplyDeathPenalty"/>). Returns the player's HP.
    /// </summary>
    int DamagePlayer(GameClient target, int damage);

    /// <summary>The monster's current stats, its states included; null for an unknown instance.</summary>
    Navislamia.Game.Services.Stats.StatBlock GetMonsterStats(long instanceId);

    /// <summary>
    /// <see cref="ApplyDamage(GameClient,long,uint,int)"/> with the monster's hate set by the caller (a skill's
    /// <c>GetHatePoint</c>) instead of the damage a swing is worth.
    /// </summary>
    int ApplyDamage(GameClient client, long instanceId, uint targetHandle, int damage, int hate) =>
        ApplyDamage(client, instanceId, targetHandle, damage);

    /// <summary>
    /// <see cref="DamagePlayer(GameClient,int)"/> from a known monster: the player's mana shield absorbs its share
    /// first, and a reflection sends part of the hit back to <paramref name="attackerInstanceId"/>.
    /// </summary>
    int DamagePlayer(GameClient target, int damage, long attackerInstanceId, bool magical) =>
        DamagePlayer(target, damage);

    /// <summary>The player's total stats, as the hit rolls read them; null when unknown.</summary>
    Navislamia.Game.Services.Stats.StatBlock GetPlayerStats(GameClient client) => null;

    /// <summary>
    /// Applies damage to a monster and owns everything that follows: death, the corpse, its states, the
    /// drops, the reward and the respawn. Returns the monster's remaining HP.
    /// </summary>
    /// <remarks>
    /// The caller sends whatever packet carries the damage — <c>TS_SC_ATTACK_EVENT</c> for an
    /// auto-attack, the <c>ST_Fire</c> hit for a skill — but must not reimplement the death path.
    /// </remarks>
    int ApplyDamage(GameClient client, long instanceId, uint targetHandle, int damage);
}
