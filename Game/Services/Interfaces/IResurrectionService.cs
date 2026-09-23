using Navislamia.Game.Network.Clients;
using Navislamia.Game.Network.Packets.Game;

namespace Navislamia.Game.Services.Interfaces;

/// <summary>
/// The death/respawn foundation: a dead player character is brought back at its return point by
/// <c>TM_CS_RESURRECTION</c> (513). See docs/packet-specs/socle-mort-respawn.md.
/// </summary>
public interface IResurrectionService
{
    void Resurrect(GameClient client, GameActionPackets.ResurrectionRequest request);
}
