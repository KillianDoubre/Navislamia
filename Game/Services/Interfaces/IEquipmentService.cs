using System.Threading.Tasks;
using Navislamia.Game.Network.Clients;
using Navislamia.Game.Network.Packets.Game;

namespace Navislamia.Game.Services;

public interface IEquipmentService
{
    Task UnequipAsync(GameClient client, GameActionPackets.PutoffItemRequest request);

    Task EquipAsync(GameClient client, GameActionPackets.PutonItemRequest request);

    /// <summary>
    /// <c>TM_CS_PUTON_ITEM_SET</c> (281): the 24 positional handles of one equipment set, already
    /// read from the frame. Zero entries are empty slots.
    /// </summary>
    Task EquipSetAsync(GameClient client, uint[] handles);

    /// <summary><c>TM_CS_SWAP_EQUIP</c> (223): the main and spare weapon sets change places.</summary>
    Task SwapAsync(GameClient client) => Task.CompletedTask;

    /// <summary>World entry (<c>DB_Login::readEquipItemList</c>): worn items that no longer qualify go back to the bag.</summary>
    Task RevalidateWornItemsAsync(Navislamia.Game.DataAccess.Entities.Telecaster.CharacterEntity character) =>
        Task.CompletedTask;

    /// <summary>
    /// The auction search's <c>is_equipable</c> filter (<c>SearchAndSendAuctionList</c>): an equipment the player
    /// could wear (levels, rank, class, race, job depth); anything that is not worn passes.
    /// </summary>
    bool CanWear(Navislamia.Game.Network.Clients.ConnectionInfo info, long itemResourceId) => true;
}
