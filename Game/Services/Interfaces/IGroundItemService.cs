using System.Threading.Tasks;
using Navislamia.Game.Network.Clients;

namespace Navislamia.Game.Services;

public interface IGroundItemService
{
    void RemoveDungeonItems(byte layer, int cellX, int cellY) { }
    void Sync(GameClient client);
    void LeaveWorld(GameClient client);

    /// <summary>
    /// The drops of a killed monster, on the ground where it fell. <paramref name="monsterInstanceId"/> is
    /// the corpse the loot is announced from: each recipient sees <c>TM_SC_ITEM_DROP_INFO</c> (282) before
    /// the object's <c>ENTER</c>. <paramref name="lootFactor"/> is the level-gap malus on every chance
    /// (<see cref="Navislamia.Game.Services.MonsterRewardRules.LootFactor"/>).
    /// </summary>
    void DropForMonster(GameClient killer, int monsterId, float x, float y, float z,
        long monsterInstanceId = 0, double lootFactor = 1);
    void DropGoldForMonster(GameClient killer, long amount, float x, float y, float z, long monsterInstanceId = 0);
    void DropQuestItem(GameClient owner, int itemId, float x, float y, float z);

    /// <summary>
    /// <c>TM_CS_DROP_ITEM</c> (203): drops <paramref name="count"/> units of an inventory item on the
    /// ground at the character's own position. Answers with <c>TM_SC_DROP_RESULT</c> (205) alone on a
    /// refusal, and with the ground spawn then the inventory erase before the result on a success.
    /// </summary>
    Task DropFromInventoryAsync(GameClient client, uint itemHandle, int count);

    Task TakeAsync(GameClient client, uint itemHandle);

    /// <summary>
    /// The nearest ground item <paramref name="owner"/> may take, on its layer, within
    /// <paramref name="range"/> of (<paramref name="x"/>, <paramref name="y"/>) and not already being taken —
    /// what a pet goes for. False when there is none.
    /// </summary>
    bool TryFindNearest(GameClient owner, float x, float y, byte layer, float range, out GroundItemSpot spot);

    /// <summary>
    /// A pet takes a ground item for its master: the manual pickup, with the pet as the actor
    /// <c>TS_SC_TAKE_ITEM_RESULT</c> animates and no <c>TS_SC_RESULT</c>, since no <c>TM_CS_TAKE_ITEM</c> was
    /// sent. False when the item is gone, not the master's, or could not be added.
    /// </summary>
    Task<bool> TakeForPetAsync(GameClient owner, uint itemHandle, uint petHandle);
}

/// <summary>Where a ground item lies, as a pet needs it to walk there.</summary>
public readonly record struct GroundItemSpot(uint Handle, float X, float Y);
