using System.Threading.Tasks;
using Navislamia.Game.Network.Clients;

namespace Navislamia.Game.Services;

/// <summary>
/// The structural socle of the crafting and item-enchantment packet family. It owns the four client
/// frames of the family that still have no engine (256, 262, 263 and 264) and nothing else: it reads,
/// bounds, resolves and refuses. <c>TM_CS_SOULSTONE_CRAFT</c> (260) has left it for
/// <see cref="ISoulstoneCraftService"/>.
/// See docs/packet-specs/socle-artisanat-objets.md §9.2.
/// </summary>
public interface ICraftingSocleService
{
    /// <summary>
    /// Reads <paramref name="packetId"/> (256, 262, 263 or 264) out of <paramref name="packet"/>,
    /// bounces it off the character's own items and answers with a refusal. An id outside the family is
    /// logged and dropped.
    /// </summary>
    Task HandleAsync(GameClient client, ushort packetId, byte[] packet);
}
