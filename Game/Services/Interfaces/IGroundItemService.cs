using System.Threading.Tasks;
using Navislamia.Game.Network.Clients;

namespace Navislamia.Game.Services;

public interface IGroundItemService
{
    void DropForMonster(GameClient killer, int monsterId, float x, float y, float z);

    /// <summary>
    /// <c>TM_CS_DROP_ITEM</c> (203): drops <paramref name="count"/> units of an inventory item on the
    /// ground at the character's own position. Answers with <c>TM_SC_DROP_RESULT</c> (205) alone on a
    /// refusal, and with the ground spawn then the inventory erase before the result on a success.
    /// </summary>
    Task DropFromInventoryAsync(GameClient client, uint itemHandle, int count);

    Task TakeAsync(GameClient client, uint itemHandle);
}
