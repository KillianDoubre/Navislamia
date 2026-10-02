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
    void ApplyState(GameClient client, int stateId, int level, uint duration) { }
}

/// <summary>The relay a single <see cref="ISkillCastService"/> attaches to.</summary>
public sealed class CastInterrupts : ICastInterrupts
{
    private volatile ISkillCastService _listener;

    public void Attach(ISkillCastService listener) => _listener = listener;

    public void Interrupt(GameClient client) => _listener?.CancelCast(client);

    public void Damaged(GameClient client, int damage) => _listener?.OnCasterDamaged(client, damage);
    public void ApplyState(GameClient client, int stateId, int level, uint duration) => _listener?.ApplyState(client, stateId, level, duration);
}
