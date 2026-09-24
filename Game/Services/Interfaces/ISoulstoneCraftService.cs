using System.Threading.Tasks;
using Navislamia.Game.Network.Clients;

namespace Navislamia.Game.Services;

/// <summary>
/// The engine of <c>TM_CS_SOULSTONE_CRAFT</c> (260): the framing rule of the socle, plus what the frame
/// does. See docs/packet-specs/260-soulstone-craft.md §5.
/// </summary>
public interface ISoulstoneCraftService
{
    /// <summary>
    /// Reads a 260 frame out of <paramref name="packet"/>, judges it against the character's own items and
    /// the item resource table, then sockets the stones it names.
    /// </summary>
    Task HandleAsync(GameClient client, byte[] packet);
}
