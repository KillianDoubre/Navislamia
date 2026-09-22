using Navislamia.Game.Network.Clients;
using Navislamia.Game.Network.Packets.Game;

namespace Navislamia.Game.Services;

public interface ISkillCastService
{
    void Cast(GameClient client, GameActionPackets.SkillRequest request);

    void Register(GameClient client);

    void Unregister(GameClient client);

    /// <summary>
    /// Puts <paramref name="stateId"/> on the caster for <paramref name="durationTicks"/> ar_time ticks,
    /// through the same path a buff cast takes — same state handle rule, same expiry tick, same stat
    /// refresh. Used by the GM command <c>/buff</c>.
    /// </summary>
    void ApplyState(GameClient client, int stateId, int stateLevel, uint durationTicks);
}
