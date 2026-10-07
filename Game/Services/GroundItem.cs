using System;
using Navislamia.Game.Network.Clients;

namespace Navislamia.Game.Services;

/// <summary>One slot of a <c>pick_up_order</c> after the first: the group's representative, its handle when alone, its party.</summary>
public sealed record GroundItemSlot(GameClient Holder, uint Handle, long PartyId)
{
    /// <summary>The slot of a contributing group: a party by its id, a player alone by its handle.</summary>
    public static GroundItemSlot Of(GameClient representative) => representative.ConnectionInfo.PartyId is { } party
        ? new GroundItemSlot(representative, 0, party)
        : new GroundItemSlot(representative, representative.ConnectionInfo.CharacterHandle, 0);
}

public class GroundItem
{
    public int TakenBy;

    public uint Handle { get; init; }
    public int ItemCode { get; init; }
    public long Count { get; init; }
    public float X { get; init; }
    public float Y { get; init; }
    public float Z { get; init; }
    public byte Layer { get; init; }
    public GameClient Owner { get; init; }
    public uint OwnerHandle { get; init; }
    public long? PartyId { get; init; }
    public bool MonsterDrop { get; init; }

    /// <summary>A quest item dropped for one player: theirs alone (<c>onTakeItem</c>'s <c>IsTakeableQuestItem</c>).</summary>
    public bool QuestItem { get; init; }

    /// <summary>
    /// Whether the object carries a <c>pick_up_order</c>. Only a monster's drop does
    /// (<c>StructMonster.cpp:1673, 1768</c>, <c>SetPickupOrder</c>); an object a player drops has an empty order,
    /// which anybody takes at once and the client shows as open to all (state 3).
    /// </summary>
    public bool HasPickupOrder => (MonsterDrop && !Unclaimed) || QuestItem;

    /// <summary>
    /// A raid boss's loot (<c>IsDungeonRaidMonster() &amp;&amp; GetMonsterType() &gt;= MONSTER_TYPE_HIGHEST_1_STAR</c>,
    /// <c>StructMonster.cpp:2064-2068</c>): its party breaks up at once, so the official clears every slot.
    /// </summary>
    public bool Unclaimed { get; init; }

    /// <summary>
    /// Slots 1 and 2 of the order: the second and third contributing groups of the kill, a player alone by its handle,
    /// a party by its id (<c>StructMonster.cpp:2061-2085</c>). Slot 0 is <see cref="OwnerHandle"/>/<see cref="PartyId"/>.
    /// </summary>
    public GroundItemSlot[] FollowingSlots { get; init; } = Array.Empty<GroundItemSlot>();

    /// <summary>
    /// The instant the object fell, in <c>ar_time</c> ticks of the <b>server</b> clock
    /// (<see cref="ServerClock"/>), fixed once when the object is created. Two readers depend on it and
    /// neither may see it move: the <c>drop_time</c> of the entry, offset into the recipient's clock base,
    /// which the client turns into its 30/40/50 s pickup window — a re-send to a late arrival must carry the
    /// original instant, not the instant of the send — and the take window itself, which counts
    /// <c>GetArTime() - drop_time</c> (<see cref="GroundItemPickupRules"/>).
    /// </summary>
    public uint DropTime { get; init; }

    public DateTime ExpiresAt { get; init; }
}
