using Navislamia.Game.DataAccess.Entities.Telecaster;

namespace Navislamia.Game.Services;

public enum EquipItemOutcome
{
    Success,
    NotFound,
    AlreadyWorn
}

public readonly record struct EquipItemResult(
    EquipItemOutcome Outcome,
    CharacterEntity Character,
    ItemEntity Equipped,
    ItemEntity Displaced);
