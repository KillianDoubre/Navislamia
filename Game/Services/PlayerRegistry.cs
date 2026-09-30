using System;
using System.Collections.Concurrent;
using System.Linq;
using Navislamia.Game.Network.Clients;

namespace Navislamia.Game.Services;

/// <summary>
/// The registry that resolves a handle to the session of the character wearing it, in O(1) — the
/// primitive <c>BoothWatchService</c> says it had to do without (it scans every authorised session for
/// the owner of a booth handle, <c>Game/Services/BoothWatchService.cs:155-175</c>).
/// </summary>
/// <remarks>
/// A <see cref="ConcurrentDictionary{TKey,TValue}"/> for the same reason
/// <c>NetworkService.AuthorizedGameClients</c> is one: the receiving thread of one client and the
/// world ticks of another both reach it. It is filled at world entry and emptied at world exit, so an
/// entry always describes a character that is really in the world
/// (docs/packet-specs/socle-visibilite-joueurs.md §5.2 point 2).
/// </remarks>
public sealed class PlayerRegistry
{
    private readonly ConcurrentDictionary<uint, GameClient> _clients = new();

    public int Count => _clients.Count;

    public System.Collections.Generic.IReadOnlyCollection<GameClient> Clients =>
        _clients.Values.ToArray();

    /// <summary>
    /// Binds <paramref name="handle"/> to <paramref name="client"/>. Returns false when the handle was
    /// already registered: the newer session replaces the older one, which happens when the same
    /// character enters the world twice — the second entry owns the handle from then on.
    /// </summary>
    public bool Register(uint handle, GameClient client)
    {
        if (handle == 0)
        {
            throw new ArgumentOutOfRangeException(nameof(handle), "0 is not a character handle.");
        }

        if (client is null)
        {
            throw new ArgumentNullException(nameof(client));
        }

        if (_clients.TryGetValue(handle, out var existing))
        {
            if (!ReferenceEquals(existing, client))
            {
                _clients[handle] = client;
            }

            return false;
        }

        _clients[handle] = client;
        return true;
    }

    /// <summary>
    /// Forgets <paramref name="handle"/>, but only when it still belongs to
    /// <paramref name="client"/>: a session that leaves must never unregister the handle a newer
    /// session took over.
    /// </summary>
    public bool Unregister(uint handle, GameClient client = null)
    {
        if (!_clients.TryGetValue(handle, out var existing))
        {
            return false;
        }

        if (client is not null && !ReferenceEquals(existing, client))
        {
            return false;
        }

        return _clients.TryRemove(handle, out _);
    }

    public bool TryResolve(uint handle, out GameClient client)
    {
        return _clients.TryGetValue(handle, out client);
    }
}
