using System;
using System.Globalization;

namespace Navislamia.Game.Services.ItemShop;

/// <summary>What a purchase writes: <see cref="Count"/> units of <see cref="Code"/> for <see cref="Price"/> points.</summary>
public sealed record ItemShopPurchase(long AccountId, string CharacterName, int Code, int Count, long Price,
    string ServerName);

public enum ItemShopPurchaseOutcome
{
    Bought,
    NotEnoughPoints
}

/// <summary>The outcome and the balance left after it.</summary>
public readonly record struct ItemShopPurchaseResult(ItemShopPurchaseOutcome Outcome, long Balance);

/// <summary>The three numbers of a 10001 the client hands to the shop page.</summary>
public readonly record struct ItemShopLogin(int OneTimePassword, int ClientId, int AccountId);

/// <summary>
/// The pure rules of the item shop: the <c>id</c> the client writes into the shop URL, and the points arithmetic.
/// </summary>
public static class ItemShopRules
{
    /// <summary>The largest balance an account can hold.</summary>
    public const long MaxPoints = 1_000_000_000_000L;

    /// <summary>The one-time password draw of <c>onOpenItemShop</c>: <c>XRandom() % 0x00ffffff</c> (GameMessage.cpp:5066).</summary>
    public const int OneTimePasswordModulus = 0x00ffffff;

    private const uint Key1 = 0xD8FB51A9;
    private const uint Key2 = 0x9DC720AC;
    private const uint Key3 = 0x31F42CB7;
    private const uint KeyChecksum = 0x7F9B3D2E;

    /// <summary>
    /// The URL <c>id</c>: five hex words, the client's <c>getItemShopURL</c> (official client SGameInterface.cpp:2300-2340,
    /// <c>SFrame.exe</c> 0x62a5b0, which draws no seed and writes the constant 0x16ee69e8). The first number is the
    /// <c>one_time_password</c> of the 10001, the second its <c>client_id</c>, the third its <c>account_id</c>
    /// (<c>SGameVM.cpp:1171</c> passes them in that order). Gala Lab's own <c>ItemShopUrlEncoder</c> tool
    /// (program/client/Tool/ItemShopUrlEncoder/Form1.cs) is the same function.
    /// </summary>
    public static string EncodeId(uint seed, int first, int second, int third)
    {
        uint n1 = (uint)first, n2 = (uint)second, n3 = (uint)third;
        var checksum = unchecked(n1 + n2 + n3);
        n1 ^= seed;
        n2 ^= n1;
        n3 ^= n2;
        checksum ^= n3;
        return string.Concat(seed.ToString("X8"), (n1 ^ Key1).ToString("X8"), (n2 ^ Key2).ToString("X8"),
            (n3 ^ Key3).ToString("X8"), (checksum ^ KeyChecksum).ToString("X8"));
    }

    /// <summary>The decoder of the same tool (<c>button2_Click</c>): refused unless forty hex digits and the checksum agree.</summary>
    public static bool TryDecodeId(string id, out ItemShopLogin login)
    {
        login = default;
        if (id is null || id.Length != 40)
        {
            return false;
        }

        Span<uint> words = stackalloc uint[5];
        for (var i = 0; i < 5; i++)
        {
            if (!uint.TryParse(id.AsSpan(i * 8, 8), NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture,
                    out words[i]))
            {
                return false;
            }
        }

        var seed = words[0];
        var n1 = words[1] ^ Key1;
        var n2 = words[2] ^ Key2;
        var n3 = words[3] ^ Key3;
        var checksum = words[4] ^ KeyChecksum;
        checksum ^= n3;
        n3 ^= n2;
        n2 ^= n1;
        n1 ^= seed;
        if (checksum != unchecked(n1 + n2 + n3))
        {
            return false;
        }

        login = new ItemShopLogin((int)n1, (int)n2, (int)n3);
        return true;
    }

    /// <summary>A balance change that never goes below zero nor past <see cref="MaxPoints"/>.</summary>
    public static bool TryAdd(long balance, long delta, out long result)
    {
        result = balance;
        if (delta < -MaxPoints || delta > MaxPoints)
        {
            return false;
        }

        var sum = balance + delta;
        if (sum < 0 || sum > MaxPoints)
        {
            return false;
        }

        result = sum;
        return true;
    }
}
