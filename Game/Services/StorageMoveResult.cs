using Navislamia.Game.DataAccess.Entities.Telecaster;

namespace Navislamia.Game.Services;

/// <summary>
/// What the repository did with a <c>TM_CS_STORAGE</c> move request. The three refusals are the ones the
/// session answers with a <c>TS_SC_RESULT</c>; <see cref="Ignored"/> is the silent case NGemity leaves
/// unanswered (wrong side for the mode, WorldSession.cpp:1618-1637).
/// </summary>
public enum StorageMoveOutcome
{
    /// <summary>The whole stack changed side; <see cref="StorageMoveResult.Destination"/> is that row.</summary>
    Moved,

    /// <summary>Part of the stack moved; the source keeps <see cref="StorageMoveResult.Remaining"/> units.</summary>
    Split,

    /// <summary>No character of that name: the session has no storage to act on.</summary>
    UnknownCharacter,

    /// <summary>No item row carries that handle.</summary>
    UnknownHandle,

    /// <summary>The row belongs to neither this character nor this account.</summary>
    AccessDenied,

    /// <summary>Nothing to do: the item already sits on the requested side, or the stack is empty.</summary>
    Ignored
}

/// <summary>
/// The result of a storage move: the row now on the destination side, the source row when the stack was
/// split, and the units left behind.
/// </summary>
public readonly record struct StorageMoveResult(
    StorageMoveOutcome Outcome,
    ItemEntity Destination,
    ItemEntity Source,
    long Remaining)
{
    public static StorageMoveResult Refused(StorageMoveOutcome outcome) => new(outcome, null, null, 0);
}
