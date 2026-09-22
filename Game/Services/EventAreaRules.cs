namespace Navislamia.Game.Services;

/// <summary>
/// What an <c>event_area</c> claim is allowed to mean once the server has checked it against its own
/// position and loaded polygons.
/// </summary>
public enum EventAreaTransition
{
    /// <summary>The claim agrees with the session state: already in that area, or already out of it.</summary>
    None,

    /// <summary>The claim contradicts the server position, or names an area that is not loaded.</summary>
    Ignored,

    Entered,

    Left
}

/// <summary>
/// The event area decision table, kept free of IO so it can be exercised on its own. The packet is a
/// hint and the polygon is the authority: a claim only ever moves the session state when the server's
/// own position agrees with it, and a claim that would not change anything changes nothing.
/// </summary>
public static class EventAreaRules
{
    /// <param name="isEnter">True for <c>TM_CS_ENTER_EVENT_AREA</c>, false for <c>TM_CS_LEAVE_...</c>.</param>
    /// <param name="inside">Whether the server position is inside the claimed area's polygon.</param>
    /// <param name="isCurrentArea">Whether the session's current area is the claimed one.</param>
    public static EventAreaTransition Resolve(bool isEnter, bool inside, bool isCurrentArea)
    {
        if (isEnter != inside)
        {
            // Entering from outside the polygon (or leaving while still in it) is a lie or a client
            // that desynced from the server position. Either way it is not a transition.
            return EventAreaTransition.Ignored;
        }

        if (isEnter == isCurrentArea)
        {
            // ENTER for the area already current, or LEAVE with no current area: idempotent no-op.
            return EventAreaTransition.None;
        }

        return isEnter ? EventAreaTransition.Entered : EventAreaTransition.Left;
    }
}
