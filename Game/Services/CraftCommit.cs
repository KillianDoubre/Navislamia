using System;
using System.Collections.Generic;
using Navislamia.Game.DataAccess.Entities.Telecaster;

namespace Navislamia.Game.Services;

public enum CraftCommitOutcome
{
    Success,

    /// <summary>The character does not exist.</summary>
    CharacterMissing,

    /// <summary>A consumed stack or the target is no longer in the bag, or holds fewer units than asked.</summary>
    ItemMissing,

    /// <summary>The target changed since the craft was decided (another craft, an equip): nothing applies.</summary>
    TargetChanged
}

/// <summary>
/// What a committed craft did: what is left of each consumed stack (0 when it left the bag) and the target
/// as it now stands, null when it was destroyed.
/// </summary>
public sealed record CraftCommitResult(
    CraftCommitOutcome Outcome,
    IReadOnlyList<(uint Handle, long Remaining)> Consumed,
    ItemEntity Target)
{
    /// <summary>The rows a <c>MIX_CREATE_ITEM</c> added to the bag.</summary>
    public IReadOnlyList<ItemEntity> Created { get; init; } = Array.Empty<ItemEntity>();

    /// <summary>The bag items a mutation changed, as they now stand.</summary>
    public IReadOnlyList<ItemEntity> Mutated { get; init; } = Array.Empty<ItemEntity>();

    /// <summary>The crafter's ethereal stone after the craft, when it moved.</summary>
    public long? EtherealStone { get; init; }

    public static CraftCommitResult Failed(CraftCommitOutcome outcome) =>
        new(outcome, Array.Empty<(uint, long)>(), null);
}
