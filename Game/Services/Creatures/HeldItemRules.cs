using System;
using System.Collections.Generic;
using System.Linq;
using Navislamia.Game.Network.Clients;

namespace Navislamia.Game.Services.Creatures;

/// <summary>
/// The part of the official <c>StructPlayer::IsErasable</c> (<c>StructPlayer.cpp:12498-12557</c>) that only the
/// session knows: an item the character's creatures or pet hold cannot leave the bag. Trade, sale, booth, auction,
/// storage, drop, destruction and crafting all start from <c>IsErasable</c> (<c>IsTradable</c>, <c>IsSellable</c>,
/// <c>IsDropable</c>, <c>IsMixable</c>, <c>MoveInventoryToStorage</c>, <c>EraseItem_</c>); the database part — worn,
/// stored, someone else's — stays where each site already judges it. docs/packet-specs/socle-duree-invocations.md §6.
/// </summary>
public static class HeldItemRules
{
    /// <summary>
    /// Whether <paramref name="itemId"/> may leave the bag: not a formed card (<c>m_aBindSummonCard</c>), not a card
    /// whose summon is in the world, not on a belt slot (<c>m_aBeltSlotCard</c>, cards and belt equipment alike), not
    /// the cage of the pet that is out.
    /// </summary>
    public static bool IsErasable(ConnectionInfo info, long itemId)
    {
        if (Array.IndexOf(info.SummonSlots, itemId) >= 0 || Array.IndexOf(info.BeltItemIds, itemId) >= 0)
        {
            return false;
        }

        // A reference read, like the player visibility: the pet path takes the locks in the other order.
        if (info.ActivePet is { } pet && pet.CageHandle == unchecked((uint)itemId))
        {
            return false;
        }

        return !IsCardInWorld(info, itemId);
    }

    /// <summary>Whether every item of <paramref name="itemIds"/> may leave the bag.</summary>
    public static bool AreErasable(ConnectionInfo info, IEnumerable<long> itemIds) =>
        itemIds.All(itemId => IsErasable(info, itemId));

    /// <summary>Whether the summon of card <paramref name="cardId"/> is in the world.</summary>
    public static bool IsCardInWorld(ConnectionInfo info, long cardId)
    {
        CreatureCard card;
        lock (info.SummonLock)
        {
            card = info.CreatureCards.GetValueOrDefault(cardId);
        }

        return card is not null && card.SummonHandle != 0
               && info.Summons.Any(summon => summon.Handle == card.SummonHandle);
    }
}
