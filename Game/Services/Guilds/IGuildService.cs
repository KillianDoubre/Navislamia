using System.Threading.Tasks;
using Navislamia.Game.Network.Clients;
using Navislamia.Game.Network.Packets;
using Navislamia.Game.Services.Dungeons;

namespace Navislamia.Game.Services.Guilds;

public interface IGuildService
{
    Task LoadAsync();
    Task OnNameChangedAsync(GameClient client, string oldName);
    Task<bool> CompleteUploadAsync(Navislamia.Game.Network.Packets.Upload.GuildUploadPackets.Upload upload) => Task.FromResult(false);
    bool TryHandleCommand(GameClient client, string message);
    Task<bool> ExecuteCommandAsync(GameClient client, string message);
    Task OnWorldEntryAsync(GameClient client);
    Task OnWorldExitAsync(GameClient client);
    void OnVitalsChanged(GameClient client);

    /// <summary>
    /// The guild officers' and siege managers' official Lua (GuildService.Lua.cs): runs a contact, an advertised trigger
    /// or a window callback and returns what the dialog service shows.
    /// </summary>
    Task<GuildDialogResult> RunDialogAsync(GameClient client, uint npcHandle, long revision, string call) =>
        Task.FromResult<GuildDialogResult>(null);

    /// <summary>The dialog service's renderer for window callbacks, and the quest progress the scripts read.</summary>
    void AttachDialogs(System.Func<GameClient, uint, long, string, Task> runner,
        System.Func<GameClient, int, int> questProgress = null) { }
    Task TickAsync();
    long EffectiveGuild(long? guildId);
    bool SameAttackTeam(GameClient first, GameClient second);
    Task<ResultCode> AuthorizeDungeonAsync(GameClient client, int dungeon, bool siege, bool starting = true);
    Task<bool> RaidStartedAsync(GameClient client, DungeonRoom room);
    Task<bool> SiegeEnteredAsync(GameClient client, DungeonRoom room);
}
