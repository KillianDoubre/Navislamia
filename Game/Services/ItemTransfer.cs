using System.Collections.Generic;
using Navislamia.Game.DataAccess.Entities.Telecaster;

namespace Navislamia.Game.Services;

/// <summary>One stack to move: an item of the giver's bag, by handle, and how many units.</summary>
public readonly record struct ItemTransferLine(uint ItemHandle, long Count);

/// <summary>
/// A move of items between two characters, with the gold each one holds afterwards. The balances are
/// the session's own, already judged and applied under its gold lock: the transfer writes them with the
/// items, in the same save, so the database never holds the items of a trade without its price.
/// </summary>
public sealed record ItemTransfer(
    string GiverName,
    string ReceiverName,
    IReadOnlyList<ItemTransferLine> Lines,
    long GiverGold,
    long ReceiverGold);

public enum ItemTransferOutcome
{
    Success,

    /// <summary>One of the two characters does not exist.</summary>
    CharacterMissing,

    /// <summary>A handle is not in the giver's bag (moved, sold or consumed since it was offered).</summary>
    ItemMissing,

    /// <summary>A stack holds fewer units than the line asks for.</summary>
    NotEnough,

    /// <summary>The item is worn: it has to be taken off before it can change hands.</summary>
    Worn
}

/// <summary>
/// What one line did: the giver's handle and what is left of that stack (0 when it left the bag), and
/// the item the receiver now holds — the same entity when the whole stack moved, a new one otherwise.
/// </summary>
public readonly record struct ItemTransferred(uint GiverHandle, long GiverRemaining, ItemEntity Received);

public sealed record ItemTransferResult(ItemTransferOutcome Outcome, IReadOnlyList<ItemTransferred> Moved)
{
    public static ItemTransferResult Failed(ItemTransferOutcome outcome) =>
        new(outcome, System.Array.Empty<ItemTransferred>());
}
