using System.Collections.Generic;
using System.Threading.Tasks;
using Navislamia.Game.Network.Clients;

namespace Navislamia.Game.Services;

/// <summary>
/// Runs the GM commands typed in chat (docs/gm-commands.md). <paramref name="everyone"/> is the list of
/// connected clients, passed in rather than taken from <c>NetworkService</c>: injecting it here would be
/// the DI cycle that only throws at runtime.
/// </summary>
public interface IGmCommandService
{
    Task HandleAsync(GameClient client, string message, IEnumerable<GameClient> everyone);
}
