using System.Threading.Tasks;
using Navislamia.Game.Network.Clients;
using Navislamia.Game.Network.Packets.Game;

namespace Navislamia.Game.Services;

public interface IItemDonateService
{
    Task DonateAsync(GameClient client, GameActionPackets.DonateItemRequest request);

    /// <summary>259: the moral points spent on the donation rewards.</summary>
    Task<Navislamia.Game.Network.Packets.ResultCode> RewardAsync(GameClient client,
        System.Collections.Generic.IReadOnlyList<GameActionPackets.DonateRewardEntry> rewards) =>
        Task.FromResult(Navislamia.Game.Network.Packets.ResultCode.Success);
}
