using System;
using Navislamia.Game.Network.Clients;

namespace Navislamia.Game.Services.Interfaces;

/// <summary>
/// Tracks which event area (if any) a session is inside. The client's 15/16 packets are one trigger
/// among two: every position change is re-checked server side as well.
/// </summary>
public interface IEventAreaService
{
    /// <summary>
    /// Handles <c>TM_CS_ENTER_EVENT_AREA</c> / <c>TM_CS_LEAVE_EVENT_AREA</c>. Returns true when the
    /// session's current area changed; a malformed, unknown or unverified claim returns false.
    /// </summary>
    bool HandlePacket(GameClient client, ReadOnlySpan<byte> packet, bool isEnter);

    /// <summary>
    /// Re-checks the current area against the session position, without any client packet. Returns
    /// true when the session's current area changed.
    /// </summary>
    bool Refresh(GameClient client);
}
