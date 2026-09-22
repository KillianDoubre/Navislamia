using Navislamia.Game.Network.Clients;

namespace Navislamia.Game.Services.Interfaces;

public interface IMarketService
{
    /// <summary>
    /// Serves the market of <paramref name="marketName"/> to the client, carried by the NPC whose dialog
    /// was selected. Refuses — logged, nothing sent — when the name is empty (the trigger was announced
    /// in its truncated <c>open_market(</c> form) or when the catalogue does not know it.
    /// </summary>
    void Open(GameClient client, uint npcHandle, string marketName);
}
