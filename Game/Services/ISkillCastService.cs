using Navislamia.Game.Network.Clients;
using Navislamia.Game.Network.Packets.Game;

namespace Navislamia.Game.Services;

public interface ISkillCastService
{
    System.Threading.Tasks.Task RestoreBuffsAsync(GameClient client) => System.Threading.Tasks.Task.CompletedTask;
    System.Threading.Tasks.Task SaveBuffsAsync(GameClient client) => System.Threading.Tasks.Task.CompletedTask;
    void SynchronizeBuffs(GameClient client) { }
    void Cast(GameClient client, GameActionPackets.SkillRequest request);

    /// <summary>
    /// Cancels one of the player's own states, as requested by the client's state window
    /// (<c>TM_CS_REQUEST_REMOVE_STATE</c>, 408).
    /// </summary>
    void RemoveState(GameClient client, GameActionPackets.RemoveStateRequest request);

    void Register(GameClient client);

    void Unregister(GameClient client);

    /// <summary>
    /// Puts <paramref name="stateId"/> on the caster for <paramref name="durationTicks"/> ar_time ticks,
    /// through the same path a buff cast takes — same state handle rule, same expiry tick, same stat
    /// refresh. Used by the GM command <c>/buff</c>.
    /// </summary>
    void ApplyCombatState(Combat.CombatActor target, Combat.CombatActor source, Combat.StateProc proc) { }

    /// <summary><c>StructCooldownProc</c>: the owner's cooling skills get the proc's seconds, then a 403.</summary>
    void ApplyCooldownProc(Combat.CombatActor owner, Combat.CooldownProc proc) { }

    void ApplyState(GameClient client, int stateId, int stateLevel, uint durationTicks);
    void ApplyMonsterState(GameClient client, int stateId, int stateLevel, uint durationTicks, long monsterId) =>
        ApplyState(client, stateId, stateLevel, durationTicks);

    /// <summary>Applies a self buff or heal carried by an item effect without a learned-skill gate.</summary>
    bool ApplyItemSkill(GameClient client, int skillId, int skillLevel);

    /// <summary>
    /// <c>pClient-&gt;CastSkill(skill_id, 1, self, …)</c> for an instance game spell (64818/64827): the cast a 4250/4251
    /// starts, with its delay and its interruptions; Success when the cast began.
    /// </summary>
    Navislamia.Game.Network.Packets.ResultCode CastInstanceGameSkill(GameClient client, int skillId) =>
        Navislamia.Game.Network.Packets.ResultCode.NotActable;

    /// <summary>
    /// Takes the active instance of <paramref name="stateId"/> off the caster: <c>TS_SC_STATE</c> removal
    /// and the stat refresh, exactly as the expiry tick does. False when no such state is active.
    /// </summary>
    bool RemoveState(GameClient client, int stateId);

    /// <summary>
    /// <c>StructCreature::RemoveStateIf(StateFlagChecker(flag))</c>: takes off every active state whose
    /// <c>state_time_type</c> carries <paramref name="flag"/>, each as <see cref="RemoveState(GameClient,int)"/> does.
    /// Returns how many were removed.
    /// </summary>
    int RemoveStatesWithTimeFlag(GameClient client, Navislamia.Game.DataAccess.Entities.Enums.StateTimeType flag) => 0;

    /// <summary>
    /// Cancels the cast in progress, if it can be cancelled (<c>StructSkill::Cancel</c>): <c>ST_Cancel</c> goes
    /// to the caster and the players who see them. False when there is none or it cannot be.
    /// </summary>
    bool CancelCast(GameClient client) => false;

    /// <summary><c>turnOffAuraOnSkillReset</c>: the cast in progress stops and the auras of skills taken away go out.</summary>
    void TurnOffAurasOf(GameClient client, System.Collections.Generic.IReadOnlyCollection<int> skillIds) { }
    bool CancelSummonCast(GameClient client, uint handle, bool force = false) => false;
    void ForgetSummonCaster(GameClient client, uint handle) { }
    void OnSummonCasterDamaged(GameClient client, uint handle, int damage) { }

    /// <summary>A hit taken while casting pushes the fire back or may break the cast (<c>casting_type</c>).</summary>
    void OnCasterDamaged(GameClient client, int damage) { }

    /// <summary>
    /// An item's skill used at <paramref name="targetHandle"/>: the Resurrection Scroll (603002, skill 6001)
    /// brings a dead player back; any other skill behaves as <see cref="ApplyItemSkill(GameClient,int,int)"/>.
    /// </summary>
    bool ApplyItemSkill(GameClient client, int skillId, int skillLevel, uint targetHandle) =>
        ApplyItemSkill(client, skillId, skillLevel);

    /// <summary>Whether an item's skill can land on <paramref name="targetHandle"/>, judged before the item is spent.</summary>
    Navislamia.Game.Network.Packets.ResultCode CheckItemSkillTarget(GameClient client, int skillId, uint targetHandle) =>
        Navislamia.Game.Network.Packets.ResultCode.Success;
}
