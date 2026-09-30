using Navislamia.Game.DataAccess.Entities.Enums;
using Navislamia.Game.Network.Packets;

namespace Navislamia.Game.Services;

/// <summary>
/// The pure judgement of <c>TM_CS_PUTON_CARD</c> (214), socketing a soul stone into one of an
/// equipment's sockets. Nothing here touches the database: the character service resolves the target
/// and the card, this class judges them, and the verdict is applied inside the character service's
/// own gate. Keeping the rules separate is what makes them testable without a server or a client.
/// </summary>
public static class CardSocketRules
{
    /// <summary>Both the reference and <c>TS_ITEM_SOCKETS</c> model at most four sockets.</summary>
    public const int MaxSockets = 4;

    /// <summary>
    /// The resolved target of a 214 frame. <paramref name="Slot"/> is the wear slot of the equipment
    /// being socketed and <paramref name="CardHandle"/> the soul stone moved into it.
    /// </summary>
    public readonly record struct CardTarget(ItemWearType Slot, uint CardHandle);

    /// <summary>
    /// The single decision point of the chosen reading (fiche §7.1, R2): <c>position</c> is the wear
    /// slot of the target equipment and <c>item_handle</c> is the card being moved into it, exactly as
    /// the neighbouring couple 200/201 reads its own <c>position</c>. If a capture ever shows that
    /// <c>position</c> is a socket index or a bag index instead, this is the only place to change.
    /// </summary>
    public static CardTarget ResolveTarget(sbyte position, uint cardHandle)
    {
        return new CardTarget((ItemWearType)position, cardHandle);
    }

    /// <summary>
    /// A socket count outside 1..4 means the item cannot be socketed at all; the reference refuses it
    /// with <c>TS_RESULT_ACCESS_DENIED</c> (NGemity <c>WorldSession.cpp:1509-1512</c>).
    /// </summary>
    public static bool IsSocketable(int socketCount)
    {
        return socketCount >= 1 && socketCount <= MaxSockets;
    }

    /// <summary>
    /// A card is a soul stone only when all three of its resource markers agree, which is the test the
    /// reference runs (<c>WorldSession.cpp:1522-1533</c>) and what <c>smsg_soket03</c> states.
    /// </summary>
    public static bool IsSoulstone(ItemGroup group, ItemType type, ItemBaseType baseType)
    {
        return group == ItemGroup.Soulstone && type == ItemType.Soulstone
            && baseType == ItemBaseType.Soulstone;
    }

    /// <summary>
    /// How many stones that grant the same bonus an item accepts: two on a four-socket item, one on a
    /// narrower one (<c>nMaxReplicatableCount = nSocketCount == 4 ? 2 : 1</c>). Corroborated by the
    /// client's own <c>smsg_soket05</c> ("You can not have more than 2 Soul Stones ... that increase
    /// the same stat").
    /// </summary>
    public static int MaxReplicas(int socketCount)
    {
        return socketCount == MaxSockets ? 2 : 1;
    }

    /// <summary>
    /// The first empty socket inside the item's own socket count, or <c>-1</c> when they are all
    /// filled. The frame carries no socket index, so the server picks the first free one: socketing
    /// over an occupied socket (which the client offers, <c>smsg_soket01</c>) is not reachable from a
    /// 214 frame under the retained reading and is refused rather than guessed.
    /// </summary>
    public static int FindFreeSocket(int socketCount, long[] sockets)
    {
        for (var i = 0; i < socketCount; i++)
        {
            if (SocketAt(sockets, i) == 0)
            {
                return i;
            }
        }

        return -1;
    }

    /// <summary>
    /// How many occupied sockets already hold the same resource as the incoming stone.
    /// <para>
    /// The reference compares the sixteen stat fields (<c>base_type</c>, <c>base_var</c>,
    /// <c>opt_type</c>, <c>opt_var</c>) of the socketed resources instead of their codes, so it also
    /// refuses two distinct resources sharing one signature. Comparing the socketed resource code —
    /// which is what a socket stores on both sides — is the narrower, provable half of that rule: every
    /// same-stone duplicate is caught, a same-signature pair of distinct stones is accepted.
    /// </para>
    /// </summary>
    public static int CountSameResource(int socketCount, long[] sockets, long resourceId)
    {
        var count = 0;
        for (var i = 0; i < socketCount; i++)
        {
            if (SocketAt(sockets, i) == resourceId)
            {
                count++;
            }
        }

        return count;
    }

    /// <summary>
    /// Judges one socketing request and returns the first empty socket in
    /// <paramref name="socketIndex"/> when it is accepted. The guards run in the reference's order:
    /// socketable item, soul stone nature, replica ceiling, free socket.
    /// </summary>
    public static ResultCode Judge(int socketCount, long[] sockets, bool cardIsSoulstone,
        long cardResourceId, out int socketIndex)
    {
        socketIndex = -1;

        if (!IsSocketable(socketCount))
        {
            return ResultCode.AccessDenied;
        }

        if (!cardIsSoulstone)
        {
            return ResultCode.NotActable;
        }

        if (CountSameResource(socketCount, sockets, cardResourceId) >= MaxReplicas(socketCount))
        {
            return ResultCode.AlreadyExist;
        }

        socketIndex = FindFreeSocket(socketCount, sockets);
        return socketIndex < 0 ? ResultCode.AlreadyExist : ResultCode.Success;
    }

    /// <summary>
    /// Reads one socket of an item, tolerating the column being absent or shorter than the item's own
    /// socket count: both happen in stored data (the inventory sheet guards the same way).
    /// </summary>
    public static long SocketAt(long[] sockets, int index)
    {
        if (sockets is null || index < 0 || index >= sockets.Length)
        {
            return 0;
        }

        return sockets[index];
    }
}
