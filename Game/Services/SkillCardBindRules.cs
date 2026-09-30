using Navislamia.Game.DataAccess.Entities.Enums;
using Navislamia.Game.DataAccess.Entities.Telecaster;
using Navislamia.Game.Network.Packets;

namespace Navislamia.Game.Services;

/// <summary>
/// The judgements a skill card request must pass before the object is touched, plus the shape of the
/// state they read. They are pure: the caller holds the database gate and passes the loaded item.
/// </summary>
public static class SkillCardBindRules
{
    /// <summary>
    /// The socket that carries the bind state of a skill card: it holds the <c>Id</c> of the bearer
    /// after a bind, so a zero there means "not bound". Navislamia keeps no in memory
    /// <c>m_hBindedTarget</c>, this socket is the single source of truth (spec §6.2).
    /// </summary>
    public const int BearerSocketIndex = 0;

    /// <summary>The sockets an item row carries (<c>Socket_0..Socket_3</c>).</summary>
    public const int SocketCount = 4;

    /// <summary>
    /// A request only ever acts on the character itself (NGemity <c>WorldSession.cpp:1710</c> rejects
    /// any other target). A null handle is not a character: a logged in character always has a
    /// non-zero one (<c>ConnectionInfo.CharacterHandle</c>), and socket zero doubles as the "not
    /// bound" reading, so a zero handle must not be accepted as a self target.
    /// </summary>
    public static bool IsSelfTarget(uint targetHandle, uint characterHandle)
    {
        return characterHandle != 0 && targetHandle == characterHandle;
    }

    /// <summary>
    /// The item carries a bearer socket: the card is bound. A missing or short socket array reads as
    /// "not bound".
    /// </summary>
    public static bool IsBound(ItemEntity item)
    {
        return item?.SocketItemIds is { Length: > BearerSocketIndex } sockets && sockets[BearerSocketIndex] != 0;
    }

    /// <summary>
    /// The unbind judgement, in the order of the reference (<c>WorldSession.cpp:1706</c> then
    /// <c>:1710</c> then <c>:1714</c>): an unknown handle answers <c>NotExist</c> before the target is
    /// even looked at, a target that is not the character answers <c>NotActable</c>, and everything the
    /// item itself fails answers <c>AccessDenied</c>.
    /// </summary>
    public static SkillCardBindResult CheckUnbindable(uint characterHandle, uint itemHandle, uint targetHandle,
        ItemEntity item, IItemGroupCatalog catalog)
    {
        if (item is null)
        {
            return SkillCardBindResult.Refuse(SkillCardBindOutcome.NotFound, ResultCode.NotExist,
                unchecked((int)itemHandle));
        }

        if (!IsSelfTarget(targetHandle, characterHandle))
        {
            return SkillCardBindResult.Refuse(SkillCardBindOutcome.NotActable, ResultCode.NotActable,
                unchecked((int)targetHandle));
        }

        // A resource the catalog does not know cannot be judged: refusing it would be an unproven
        // refusal, so the group gate stays open (the IItemGroupCatalog contract, as the 253 does).
        if (catalog.TryGetGroup(item.ItemResourceId, out var group) && group != ItemGroup.Skillcard)
        {
            return SkillCardBindResult.Refuse(SkillCardBindOutcome.AccessDenied, ResultCode.AccessDenied,
                unchecked((int)itemHandle));
        }

        // Item::IsInInventory(): a worn item is not in the inventory.
        if (item.WearInfo != ItemWearType.None)
        {
            return SkillCardBindResult.Refuse(SkillCardBindOutcome.AccessDenied, ResultCode.AccessDenied,
                unchecked((int)itemHandle));
        }

        // m_hBindedTarget == 0 in the reference: unbinding what is not bound is refused, not ignored.
        if (!IsBound(item))
        {
            return SkillCardBindResult.Refuse(SkillCardBindOutcome.AccessDenied, ResultCode.AccessDenied,
                unchecked((int)itemHandle));
        }

        return SkillCardBindResult.Success;
    }
}
