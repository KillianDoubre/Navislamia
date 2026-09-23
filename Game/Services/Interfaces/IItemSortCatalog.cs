namespace Navislamia.Game.Services;

public interface IItemSortCatalog
{
    ulong GetResourceKey(long resourceId);

    /// <summary>Whether <paramref name="resourceId"/> is a loaded <c>ItemResource</c>.</summary>
    bool Contains(long resourceId);
}
