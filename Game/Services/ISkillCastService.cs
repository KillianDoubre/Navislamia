using Navislamia.Game.Network.Clients;
using Navislamia.Game.Network.Packets.Game;

namespace Navislamia.Game.Services;

public interface ISkillCastService
{
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
    void ApplyState(GameClient client, int stateId, int stateLevel, uint durationTicks);

    /// <summary>
    /// Takes the active instance of <paramref name="stateId"/> off the caster: <c>TS_SC_STATE</c> removal
    /// and the stat refresh, exactly as the expiry tick does. False when no such state is active.
    /// </summary>
    bool RemoveState(GameClient client, int stateId);
}
