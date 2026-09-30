using System.Collections.Generic;
using System.Threading.Tasks;
using Navislamia.Game.Network.Clients;

namespace Navislamia.Game.Services;

/// <summary>
/// The visibility half of the player booth socle: <c>TM_CS_WATCH_BOOTH</c> (702),
/// <c>TM_SC_WATCH_BOOTH</c> (703) and <c>TM_CS_STOP_WATCH_BOOTH</c> (704). It owns those three frames and
/// nothing else. <c>703</c> is the only frame of the family a 7.3 client has an incoming slot for, and
/// therefore the only one this service ever sends
/// (docs/packet-specs/socle-booths-visibilite.md §2.4 and §5.2).
/// </summary>
public interface IBoothWatchService
{
    /// <summary>
    /// Reads a <c>TM_CS_WATCH_BOOTH</c> (702) out of <paramref name="packet"/>, resolves the booth it
    /// names among <paramref name="sessions"/>, and answers the <strong>requesting</strong> client with a
    /// <c>TM_SC_WATCH_BOOTH</c> (703) built from the owner's inventory, or with a refusal. The owner is
    /// sent nothing: the family is broadcast to no one (§5.2 point 8).
    /// </summary>
    Task HandleWatchAsync(GameClient client, byte[] packet, IEnumerable<GameClient> sessions);

    /// <summary>
    /// Reads a <c>TM_CS_STOP_WATCH_BOOTH</c> (704) and forgets the booth this connection was observing.
    /// Closing with nothing observed stays idempotent and answers <c>Success</c> (§5.2 point 5).
    /// </summary>
    void HandleStopWatch(GameClient client, byte[] packet);

    /// <summary>
    /// The <c>TM_SC_WATCH_BOOTH</c> (703) a watcher of <paramref name="owner"/>'s booth should see now,
    /// resolved against the owner's bag, or null when no booth is open. A trade uses it to refresh every
    /// open window of the booth (docs/packet-specs/705-buy-from-booth.md §5).
    /// </summary>
    Task<byte[]> BuildWatchFrameAsync(ConnectionInfo owner);
}
