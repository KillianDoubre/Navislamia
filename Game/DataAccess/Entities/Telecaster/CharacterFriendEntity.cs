namespace Navislamia.Game.DataAccess.Entities.Telecaster;

/// <summary>
/// One entry of a character's friend list or block list (the official <c>Friend</c>/<c>Denial</c> rows,
/// <c>smp_insert_friend</c>/<c>smp_insert_denial</c>). Keyed by character ids rather than the official names, so a
/// rename or a deletion needs no list rewrite: the names are read from <c>Characters</c> when the list is loaded.
/// </summary>
public sealed class CharacterFriendEntity : Entity
{
    public long OwnerId { get; set; }
    public long TargetId { get; set; }

    /// <summary>True for the block list (<c>denial</c>), false for the friend list.</summary>
    public bool IsDenial { get; set; }
}
