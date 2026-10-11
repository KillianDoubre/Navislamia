using System;
using System.Collections.Generic;
using System.Security.Cryptography;

namespace Navislamia.Game.Services.ItemShop;

/// <summary>Who an opened shop page belongs to.</summary>
public sealed record ItemShopSession(string Token, long AccountId, long CharacterId, string CharacterName);

/// <summary>
/// The one-time passwords of the 10001 and the logged-in pages. The official server wrote the password into the
/// database for its web shop to read (<c>DB_UpdateOneTimePassword</c>, GameMessage.cpp:5069); the page lives in this
/// process here, so it stays in memory. A password opens one page once, before it expires; a newer <c>/cshop</c>
/// replaces the older password of the same character. A page is reached by a random token of its own, carried in its
/// links and forms (the client's embedded browser is spared cookies), and expires after a quiet spell.
/// </summary>
public sealed class ItemShopSessions
{
    private readonly object _gate = new();
    private readonly Dictionary<int, Ticket> _tickets = new();
    private readonly Dictionary<string, Entry> _sessions = new(StringComparer.Ordinal);
    private readonly Func<DateTime> _now;
    private readonly TimeSpan _ticketLifetime;
    private readonly TimeSpan _sessionLifetime;
    private readonly Func<int> _draw;

    public ItemShopSessions(TimeSpan ticketLifetime, TimeSpan sessionLifetime, Func<DateTime> now = null,
        Func<int> draw = null)
    {
        _ticketLifetime = ticketLifetime;
        _sessionLifetime = sessionLifetime;
        _now = now ?? (() => DateTime.UtcNow);
        _draw = draw ?? (() => RandomNumberGenerator.GetInt32(1, ItemShopRules.OneTimePasswordModulus));
    }

    /// <summary>A fresh password for the character's 10001.</summary>
    public int Issue(int clientId, long accountId, string characterName)
    {
        var password = _draw();
        lock (_gate)
        {
            Prune();
            _tickets[clientId] = new Ticket(password, accountId, characterName, _now() + _ticketLifetime);
        }

        return password;
    }

    /// <summary>The page's login: the three numbers of the URL must name a live password, which is spent.</summary>
    public bool TryRedeem(ItemShopLogin login, out ItemShopSession session)
    {
        session = null;
        lock (_gate)
        {
            Prune();
            if (!_tickets.TryGetValue(login.ClientId, out var ticket) || ticket.Password != login.OneTimePassword
                || ticket.AccountId != login.AccountId)
            {
                return false;
            }

            _tickets.Remove(login.ClientId);
            var token = Convert.ToHexString(RandomNumberGenerator.GetBytes(24));
            session = new ItemShopSession(token, ticket.AccountId, login.ClientId, ticket.CharacterName);
            _sessions[token] = new Entry(session, _now() + _sessionLifetime);
            return true;
        }
    }

    /// <summary>A live page by its token; each use pushes its expiry back.</summary>
    public bool TryGet(string token, out ItemShopSession session)
    {
        session = null;
        if (string.IsNullOrEmpty(token))
        {
            return false;
        }

        lock (_gate)
        {
            Prune();
            if (!_sessions.TryGetValue(token, out var entry))
            {
                return false;
            }

            _sessions[token] = entry with { Expires = _now() + _sessionLifetime };
            session = entry.Session;
            return true;
        }
    }

    private void Prune()
    {
        var now = _now();
        List<int> tickets = null;
        foreach (var (key, ticket) in _tickets)
        {
            if (ticket.Expires <= now) (tickets ??= new()).Add(key);
        }

        tickets?.ForEach(key => _tickets.Remove(key));

        List<string> sessions = null;
        foreach (var (key, entry) in _sessions)
        {
            if (entry.Expires <= now) (sessions ??= new()).Add(key);
        }

        sessions?.ForEach(key => _sessions.Remove(key));
    }

    private sealed record Ticket(int Password, long AccountId, string CharacterName, DateTime Expires);

    private sealed record Entry(ItemShopSession Session, DateTime Expires);
}
