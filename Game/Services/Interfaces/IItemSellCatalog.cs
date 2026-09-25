namespace Navislamia.Game.Services;

/// <summary>
/// The pair the sell price is computed from: the <c>rank</c> and the <c>price</c> of an item resource
/// (<c>ItemResourceEntity.cs:25</c>, <c>:35</c>). Both are read once by <see cref="ItemSellCatalog"/>.
/// </summary>
public readonly record struct ItemSellTemplate(int Rank, int Price);

public interface IItemSellCatalog
{
    /// <summary>
    /// The sell template of an item resource. Returns <c>false</c> when the resource carries no known id:
    /// the caller then refuses the sale in <c>NotExist</c> (1) with value 0, the code the reference answers
    /// an item without a template with (<c>WorldSession.cpp:1026-1030</c>).
    /// </summary>
    bool TryGetTemplate(int itemResourceId, out ItemSellTemplate template);
}
