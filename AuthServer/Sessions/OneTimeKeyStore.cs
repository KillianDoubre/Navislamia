using System.Collections.Concurrent;
using System.Security.Cryptography;

namespace Navislamia.AuthServer.Sessions;

public sealed class OneTimeKeyStore : IOneTimeKeyStore
{
    private readonly record struct Entry(long Key, int AccountId, int ServerIdx);

    private readonly ConcurrentDictionary<string, Entry> _keys = new();

    public long Issue(string username, int accountId, int serverIdx)
    {
        var key = BitConverter.ToInt64(RandomNumberGenerator.GetBytes(8));
        _keys[username] = new Entry(key, accountId, serverIdx);
        return key;
    }

    public int? Verify(string username, long key)
    {
        if (_keys.TryGetValue(username, out var entry) && entry.Key == key)
        {
            _keys.TryRemove(username, out _);
            return entry.AccountId;
        }

        return null;
    }
}
