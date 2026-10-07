using Navislamia.Game.Network.Clients;

namespace Navislamia.Game.Services.Casting;

/// <summary>
/// What breaks or delays a player's cast in progress, raised by the services that see it happen. Combat and
/// warping cannot depend on <see cref="ISkillCastService"/> (it depends on them, a DI cycle that only throws at
/// runtime), so they raise here and the cast service listens.
/// </summary>
public interface ICastInterrupts
{
    /// <summary>Death, warp, leaving the world: <c>StructCreature::CancelSkill</c>.</summary>
    void Interrupt(GameClient client);

    /// <summary>A hit taken while casting: <c>StructSkill::onDamage</c>.</summary>
    void Damaged(GameClient client, int damage);
    void InterruptSummon(GameClient client, uint handle) { }
    void ForgetSummon(GameClient client, uint handle) { }
    void SummonDamaged(GameClient client, uint handle, int damage) { }
    void ApplyCombatState(Combat.CombatActor target, Combat.CombatActor source, Combat.StateProc proc) { }

    void ApplyCooldownProc(Combat.CombatActor owner, Combat.CooldownProc proc) { }

    void ApplyState(GameClient client, int stateId, int level, uint duration) { }

    /// <summary><c>RemoveState(code)</c> on the player, as <see cref="ISkillCastService.RemoveState(GameClient,int)"/>.</summary>
    bool RemoveState(GameClient client, int stateId) => false;
}

/// <summary>The relay a single <see cref="ISkillCastService"/> attaches to.</summary>
public sealed class CastInterrupts : ICastInterrupts
{
    private volatile ISkillCastService _listener;

    public void Attach(ISkillCastService listener) => _listener = listener;

    public void Interrupt(GameClient client)
    {
        _listener?.CancelCast(client);
        foreach (var summon in client.ConnectionInfo.Summons) _listener?.ForgetSummonCaster(client, summon.Handle);
    }

    public void Damaged(GameClient client, int damage) => _listener?.OnCasterDamaged(client, damage);
    public void InterruptSummon(GameClient client, uint handle) => _listener?.CancelSummonCast(client, handle, force: true);
    public void ForgetSummon(GameClient client, uint handle) => _listener?.ForgetSummonCaster(client, handle);
    public void SummonDamaged(GameClient client, uint handle, int damage) => _listener?.OnSummonCasterDamaged(client, handle, damage);
    public void ApplyCombatState(Combat.CombatActor target, Combat.CombatActor source, Combat.StateProc proc) =>
        _listener?.ApplyCombatState(target, source, proc);

    public void ApplyCooldownProc(Combat.CombatActor owner, Combat.CooldownProc proc) =>
        _listener?.ApplyCooldownProc(owner, proc);

    public void ApplyState(GameClient client, int stateId, int level, uint duration) => _listener?.ApplyState(client, stateId, level, duration);

    public bool RemoveState(GameClient client, int stateId) => _listener?.RemoveState(client, stateId) ?? false;
}
