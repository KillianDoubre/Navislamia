namespace Navislamia.Game.Services;

/// <summary>
/// What an item resource says about socketing: how many sockets the item has, and whether the
/// resource is a soul stone (so it can be socketed into one).
/// </summary>
public readonly record struct ItemSocketTemplate(int SocketCount, bool IsSoulstone);

public interface ICardSocketCatalog
{
    /// <summary>
    /// The socketing template of an item resource. Returns <c>false</c> when the catalog does not know
    /// the resource: the caller must then refuse, since neither a socket count nor a soul stone nature
    /// can be read off an unknown resource (fiche §5.3 point 3).
    /// </summary>
    bool TryGetTemplate(long itemResourceId, out ItemSocketTemplate template);
}
