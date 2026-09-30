using System;
using Navislamia.Game.Network.Clients;

namespace Navislamia.Game.Services.Interfaces;

/// <summary>
/// Makes the other players visible: who is in whose window, and the frames that keep both sides in
/// step (docs/packet-specs/socle-visibilite-joueurs.md §5.3).
/// </summary>
public interface IPlayerVisibilityService
{
    /// <summary>The handle to session resolution the socle adds, reusable by any other service.</summary>
    PlayerRegistry Registry { get; }

    /// <summary>The presence index, for the tests and for a service that needs to look a character up.</summary>
    PlayerVisibilityIndex Index { get; }

    /// <summary>
    /// Trigger 1, world entry: registers the character and sends the reciprocal <c>ENTER</c> pairs with
    /// everyone already in its window. Called at the end of the login bootstrap.
    /// </summary>
    void EnterWorld(GameClient client);

    /// <summary>
    /// Trigger 3, region border: moves the entry in the index and re-synchronises the window. No
    /// <c>MOVE</c> frame is ever emitted here.
    /// </summary>
    void Sync(GameClient client);

    /// <summary>
    /// Trigger 2, walking: re-indexes the walker, re-synchronises its window and diffuses its
    /// <c>MOVE</c> — the received waypoints, verbatim (8 bytes each), or nothing when the request
    /// carried none. The array is the walk request's waypoint block, already validated by the
    /// receive loop: 8 bytes per point, <c>x</c> then <c>y</c>.
    /// </summary>
    void OnMove(GameClient walker, byte[] waypoints);

    /// <summary>
    /// Triggers 4 and 5, warp and exit: removes the character from the index and the registry and sends
    /// its <c>LEAVE</c> to every observer that could see it. The departing client also receives the
    /// <c>LEAVE</c> of the players it could see when <paramref name="notifyWalker"/> is set — the warp
    /// case, where the client keeps a world it must be told to forget.
    /// </summary>
    void LeaveWorld(GameClient client, bool notifyWalker = false);
}
