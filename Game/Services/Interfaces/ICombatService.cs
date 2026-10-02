using Navislamia.Game.Network.Clients;

namespace Navislamia.Game.Services;

public interface ICombatService
{
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

    /// <summary>
    /// Rolls one swing of a monster on a player, and gives the monster's swing interval in ar_time ticks
    /// (its attack speed). A player under <c>/immortal</c> takes 0.
    /// </summary>
    HitResult RollMonsterHit(long instanceId, GameClient target, out uint intervalTicks);

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
