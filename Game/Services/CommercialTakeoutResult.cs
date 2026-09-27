using Navislamia.Game.DataAccess.Entities.Telecaster;

namespace Navislamia.Game.Services;

/// <summary>
/// What the repository did with a <c>TM_CS_TAKEOUT_COMMERCIAL_ITEM</c> request. The outcome follows the
/// idiom of <see cref="StorageMoveResult"/>: an explicit issue and <b>no code sent to the client</b> —
/// the takeout has no result packet, and none may be invented
/// (docs/packet-specs/socle-stockage-commercial-conteneur.md §5.4, §5.6).
/// </summary>
public enum CommercialTakeoutOutcome
{
    /// <summary>No character of that name: the session has no container to take from.</summary>
    UnknownCharacter,

    /// <summary>
    /// No row carries that uid <b>for this reader</b>: unknown, cancelled, foreign, aimed at another
    /// character, or nothing left to take. The four cases are one answer on purpose — a forged uid must
    /// not be told apart from an unknown one (§5.2.3).
    /// </summary>
    UnknownItem,

    /// <summary>The requested quantity is zero or above what the line still holds.</summary>
    InvalidCount,

    /// <summary>The units left on the line after the takeout; <see cref="CommercialTakeoutResult.Row"/> is the row.</summary>
    Consumed
}

/// <summary>
/// The result of a takeout: the row the units left, and what remains on it.
/// </summary>
public readonly record struct CommercialTakeoutResult(CommercialTakeoutOutcome Outcome, PaidItemEntity Row, int Remaining)
{
    public static CommercialTakeoutResult Refused(CommercialTakeoutOutcome outcome) => new(outcome, null, 0);

    public static CommercialTakeoutResult Consumed(PaidItemEntity row, int remaining) =>
        new(CommercialTakeoutOutcome.Consumed, row, remaining);
}
