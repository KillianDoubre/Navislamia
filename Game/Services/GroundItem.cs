using System;
using Navislamia.Game.Network.Clients;

namespace Navislamia.Game.Services;

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
    public bool HasPickupOrder => MonsterDrop || QuestItem;

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
