using System.Threading.Tasks;
using Navislamia.Game.Network.Clients;
using Navislamia.Game.Network.Packets;
using Navislamia.Game.Services.Dungeons;

namespace Navislamia.Game.Services.Guilds;

public interface IGuildService
{
    Task<bool> CompleteUploadAsync(Navislamia.Game.Network.Packets.Upload.GuildUploadPackets.Upload upload) => Task.FromResult(false);
    bool TryHandleCommand(GameClient client, string message);
    Task<bool> ExecuteCommandAsync(GameClient client, string message);
    Task OnWorldEntryAsync(GameClient client);
    Task OnWorldExitAsync(GameClient client);
    void OnVitalsChanged(GameClient client);
    bool Contact(GameClient client, uint npcHandle, int npcId);
    bool Select(GameClient client, uint npcHandle, string trigger);
    Task TickAsync();
    long EffectiveGuild(long? guildId);
    bool SameAttackTeam(GameClient first, GameClient second);
    Task<ResultCode> AuthorizeDungeonAsync(GameClient client, int dungeon, bool siege, bool starting = true);
    Task<bool> RaidStartedAsync(GameClient client, DungeonRoom room);
    Task<bool> SiegeEnteredAsync(GameClient client, DungeonRoom room);
}
