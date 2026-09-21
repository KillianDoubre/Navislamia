using System.Threading.Tasks;
using Navislamia.Game.Network.Clients;
using Navislamia.Game.Network.Packets.Game;

namespace Navislamia.Game.Services.Interfaces;

public interface ISkillCardService
{
    /// <summary>
    /// Handles one <c>TM_CS_UNBIND_SKILLCARD</c> (285): refuses through <c>TS_SC_RESULT</c> when the
    /// card cannot be unbound, and reports the cleared bind with the <c>TM_SC_SKILLCARD_INFO</c> (286)
    /// echo when it can. Nothing else is sent on success: the reference has no success result.
    /// </summary>
    Task UnbindAsync(GameClient client, GameActionPackets.UnbindSkillCardRequest request);
}
