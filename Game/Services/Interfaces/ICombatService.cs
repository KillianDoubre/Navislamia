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
    /// The damage a hit deals to a monster. Currently a placeholder — the monster's max HP divided by
    /// three — and deliberately the same value for an auto-attack and a skill, so combat has one rule
    /// until the stats drive it.
    /// </summary>
    int GetHitDamage(long instanceId);

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
