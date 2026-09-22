using Navislamia.Game.DataAccess.Entities.Telecaster;

namespace Navislamia.Game.Services;

/// <summary>
/// The outcome of <see cref="ICharacterService.RemoveItemAsync"/>: the resolved item (<c>null</c> when the
/// handle is unknown) and the units really taken off its stack (<c>0</c> when refused).
/// </summary>
public readonly record struct ItemRemoval(ItemEntity Item, long Removed);
