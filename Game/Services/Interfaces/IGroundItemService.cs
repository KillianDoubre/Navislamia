using System.Threading.Tasks;
using Navislamia.Game.Network.Clients;

namespace Navislamia.Game.Services;

public interface IGroundItemService
{
    void DropForMonster(GameClient killer, int monsterId, float x, float y, float z);

    Task TakeAsync(GameClient client, uint itemHandle);
}
