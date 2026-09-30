using System;
using System.Threading;
using System.Threading.Tasks;

namespace Navislamia.Game.Services;

/// <summary>
/// Serialises the database work of one character, not of the whole server.
/// </summary>
/// <remarks>
/// <c>CharacterService</c> and <c>StorageService</c> each used to hold a single global semaphore, needed
/// because they shared one long-lived context. With a context per operation, two players no longer have
/// anything to share; what still has to be atomic is a read-modify-write on the <b>same</b> character —
/// the client sends <c>TM_CS_ERASE_ITEM</c> twice per destroy, and a drop judged against an item must not
/// see an equip land in between. Both services take the same gate, keyed by character name, so a storage
/// move and an inventory operation on one character still exclude each other.
/// <para>
/// The gate is striped rather than one semaphore per name: it is bounded, never leaks an entry for a
/// character that logged out, and two characters that happen to share a stripe only wait on each other.
/// </para>
/// </remarks>
public sealed class CharacterGate
{
    private const int StripeCount = 64;

    private readonly SemaphoreSlim[] _stripes = CreateStripes();

    public async Task<T> RunAsync<T>(string key, Func<Task<T>> operation)
    {
        var stripe = StripeOf(key);
        await stripe.WaitAsync();
        try
        {
            return await operation();
        }
        finally
        {
            stripe.Release();
        }
    }

    public async Task RunAsync(string key, Func<Task> operation)
    {
        var stripe = StripeOf(key);
        await stripe.WaitAsync();
        try
        {
            await operation();
        }
        finally
        {
            stripe.Release();
        }
    }

    /// <summary>
    /// Runs <paramref name="operation"/> holding the gates of two characters — a trade between them. The
    /// stripes are taken in index order, so two trades between the same pair can never hold one each and
    /// wait for the other; two names sharing a stripe take it once, since a stripe is not reentrant.
    /// </summary>
    public async Task<T> RunPairAsync<T>(string first, string second, Func<Task<T>> operation)
    {
        var a = StripeIndexOf(first);
        var b = StripeIndexOf(second);
        if (a == b)
        {
            return await RunOnStripeAsync(_stripes[a], operation);
        }

        var (low, high) = a < b ? (a, b) : (b, a);
        await _stripes[low].WaitAsync();
        try
        {
            return await RunOnStripeAsync(_stripes[high], operation);
        }
        finally
        {
            _stripes[low].Release();
        }
    }

    private static async Task<T> RunOnStripeAsync<T>(SemaphoreSlim stripe, Func<Task<T>> operation)
    {
        await stripe.WaitAsync();
        try
        {
            return await operation();
        }
        finally
        {
            stripe.Release();
        }
    }

    private static uint StripeIndexOf(string key) =>
        (uint)StringComparer.Ordinal.GetHashCode(key ?? string.Empty) % StripeCount;

    private SemaphoreSlim StripeOf(string key)
    {
        var hash = (uint)StringComparer.Ordinal.GetHashCode(key ?? string.Empty);
        return _stripes[hash % StripeCount];
    }

    private static SemaphoreSlim[] CreateStripes()
    {
        var stripes = new SemaphoreSlim[StripeCount];
        for (var i = 0; i < stripes.Length; i++)
        {
            stripes[i] = new SemaphoreSlim(1, 1);
        }

        return stripes;
    }
}
