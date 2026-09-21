using Navislamia.Game.DataAccess.Entities.Enums;
using Navislamia.Game.Network.Packets;

namespace Navislamia.Game.Services;

/// <summary>
/// The judgement of <c>WorldSession::onBindSkillCard</c> in NGemity
/// (Chihiro/src/Network/GameNetwork/WorldSession.cpp:1678-1701), reduced to the state this repository
/// keeps. NGemity judges the packet in three steps: the handle must resolve to an item (else
/// <c>TS_RESULT_NOT_EXIST</c>), the target must be the bearer themselves (else
/// <c>TS_RESULT_NOT_ACTABLE</c>), then the item must be an inventory skill card, worn by nobody and
/// not bound yet (else <c>TS_RESULT_ACCESS_DENIED</c>). Only the two last points live here; the two
/// first belong to the service and the gated write.
/// </summary>
/// <remarks>
/// Two deliberate reductions, both durable knowledge of the fiche
/// (<c>docs/packet-specs/284-bind-skillcard.md</c> §6.2 and §6.3):
/// <list type="bullet">
/// <item>NGemity tests the skill card's own enhance (<c>WorldSession.cpp:1698</c>). This repository
/// keeps no skill enhance, and that test is the image of socket 0: "already bound" ⇔
/// <c>sockets[0] != 0</c>. One check instead of two, same meaning.</item>
/// <item>NGemity refuses a card that is not in <c>GROUP_SKILLCARD</c>. A resource the catalog does
/// not know (unknown id, or a base without the item resource table) leaves the rule ungated rather
/// than refuse: an unproven refusal is worse than a missing gate, the discipline already applied by
/// <see cref="GroundItemDropRules.IsBoundSummonCard"/> and <c>ItemUseService</c>.</item>
/// </list>
/// </remarks>
public static class SkillCardBindRules
{
    /// <summary>
    /// Index of the bearer reference inside <c>ItemEntity.SocketItemIds</c>, the field NGemity writes
    /// through <c>Item::SetBindTarget</c> (Item.cpp:314-332). NGemity uses socket 1 for a creature
    /// (Item.cpp:322-324); that binding depends on the summoned-creature base, out of scope here
    /// (fiche §5.2 i).
    /// </summary>
    public const int BearerSocketIndex = 0;

    /// <summary>
    /// NGemity compares the packet's <c>target_handle</c> with the player's own handle
    /// (WorldSession.cpp:1688-1691): a skill card is always bound to its own bearer, never to a third
    /// party. The repository's own "self" is <c>ConnectionInfo.CharacterHandle</c>
    /// (<c>GameActions.cs:96</c>), never zero for a character in the world — the guard matters because
    /// socket 0 holding <c>0</c> is exactly what "not bound" means, so answering success on a zero
    /// handle would persist nothing while telling the client the card is bound.
    /// </summary>
    public static bool IsSelfTarget(uint targetHandle, uint characterHandle)
    {
        return characterHandle != 0 && targetHandle == characterHandle;
    }

    /// <summary>
    /// "Already bound": NGemity's <c>m_hBindedTarget != 0</c> (WorldSession.cpp:1694) read on the
    /// socket this repository persists it in. A null or empty array is an item that never met a
    /// socket, that is an unbound one.
    /// </summary>
    public static bool IsBound(long[] sockets)
    {
        return sockets is { Length: > 0 } && sockets[BearerSocketIndex] != 0;
    }

    /// <summary>
    /// The admission of the card itself: an inventory skill card (<c>ItemGroup.Skillcard</c> = 10),
    /// not worn and not bound yet. <paramref name="group"/> is <c>null</c> when the item resource
    /// catalog cannot place the resource (see the class remarks).
    /// </summary>
    public static ResultCode CheckBindable(ItemGroup? group, ItemWearType wearInfo, long[] sockets)
    {
        if (group is not null && group != ItemGroup.Skillcard)
        {
            return ResultCode.AccessDenied;
        }

        if (wearInfo != ItemWearType.None)
        {
            return ResultCode.AccessDenied;
        }

        return IsBound(sockets) ? ResultCode.AccessDenied : ResultCode.Success;
    }
}
