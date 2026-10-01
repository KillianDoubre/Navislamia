using System;
using Navislamia.Game.Network.Clients;
using Navislamia.Game.Services.Interfaces;

namespace Navislamia.Game.Services;

/// <summary>
/// Frames about a fight, for the players who watch it (docs/packet-specs/socle-diffusion-combat.md). A
/// monster's handle differs per client (one handle per client, from <c>0x40000000</c>) and so does the
/// clock offset, so such a frame cannot be sent as is: it is rebuilt for each observer with that
/// observer's handle for the monster. An observer that does not see the monster gets nothing.
/// </summary>
public static class ObserverFrames
{
    /// <summary>
    /// Sends, to every player who sees <paramref name="subject"/> and has <paramref name="instanceId"/>
    /// streamed, the frame <paramref name="build"/> makes for that observer and its handle for the monster
    /// (a null frame sends nothing). The subject itself is not included: its own frame is the caller's.
    /// </summary>
    public static void SendMonsterFrame(IPlayerVisibilityService players, GameClient subject, long instanceId,
        Func<GameClient, uint, byte[]> build)
    {
        if (players is null || subject is null)
        {
            return;
        }

        foreach (var observer in players.Observers(subject))
        {
            var handle = observer.ConnectionInfo.GetMonsterHandle(instanceId);
            if (handle != 0 && build(observer, handle) is { } frame)
            {
                observer.Connection.Send(frame);
            }
        }
    }
}
