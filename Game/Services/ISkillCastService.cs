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
}
