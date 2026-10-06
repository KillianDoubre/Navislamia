using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Navislamia.Game.DataAccess.Entities.Enums;
using Navislamia.Game.DataAccess.Entities.Telecaster;
using Navislamia.Game.Network.Clients;
using Navislamia.Game.Network.Packets.Game;
using Serilog;

namespace Navislamia.Game.Services.Creatures;

/// <summary>
/// <c>TM_CS_FOSTER_CREATURE</c> (6002), the deposit gesture: the write path opposite
/// <see cref="ICreatureFarmService"/>'s read path.
/// </summary>
public interface ICreatureFarmDepositService
{
    /// <summary>
    /// Validates and writes the deposition of <paramref name="request"/> for the calling character.
    /// <c>true</c> is <c>result = 1</c> of <c>TM_SC_RESULT_FOSTER</c> (6003), <c>false</c> the refusal the
    /// caller answers with <c>result = 0</c>; the caller owns both the frame and the logging of a refusal.
    /// </summary>
    Task<bool> FosterCreatureAsync(GameClient client, GameFarmPackets.FosterCreatureRequest request);
}

/// <summary>
/// <c>FosterCreature</c> (<c>GameMessage.cpp:11860-11908</c>), ported point by point in the reference's own
/// order (docs/packet-specs/6002-foster-creature.md §5.2): the card, the ticket cost of its summon's
/// <c>(rate, form, enhance)</c>, the ticket stacks, the cracker stacks, the free slot, the farm row, then the
/// consumption — never the other way round, so a refusal never costs a ticket.
/// <para>
/// Three things the 2015 source does and the 7.3 era contradicts are deliberately not ported (§6.4): the
/// missing <c>break</c> that would consume one stack per entry, <c>is_cash</c> left unassigned, and the
/// <c>limit_*</c> refusals. A negative <c>ticket_count</c>/<c>cracker_count</c> is refused here: the parser
/// bounds the frame, not the counters inside it, and a negative sum could otherwise satisfy the cost while
/// erasing nothing (a fourth trap of the same family).
/// </para>
/// </summary>
public sealed class CreatureFarmDepositService : ICreatureFarmDepositService
{
    private readonly ILogger _logger = Log.ForContext<CreatureFarmDepositService>();
    private readonly ICharacterService _characters;
    private readonly ICreatureFarmStore _store;
    private readonly ICreatureCatalog _creatures;
    private readonly ICreatureFarmItemCatalog _items;
    private readonly ICreatureFarmTicketCost _costs;
    private readonly Func<DateTime> _localNow;

    /// <param name="localNow">
    /// The local server clock the farm row's <c>registration_time</c> is stamped with, as for the nursing
    /// reset at 06:00 (<c>StructPlayer.cpp:11439-11456</c>), not the UTC line PostgreSQL stores.
    /// </param>
    public CreatureFarmDepositService(ICharacterService characters, ICreatureFarmStore store,
        ICreatureCatalog creatures, ICreatureFarmItemCatalog items, ICreatureFarmTicketCost costs,
        Func<DateTime> localNow = null)
    {
        _characters = characters;
        _store = store;
        _creatures = creatures;
        _items = items;
        _costs = costs;
        _localNow = localNow ?? (() => DateTime.Now);
    }

    public async Task<bool> FosterCreatureAsync(GameClient client, GameFarmPackets.FosterCreatureRequest request)
    {
        var characterName = client?.ConnectionInfo?.CharacterName;
        if (string.IsNullOrEmpty(characterName))
        {
            _logger.Warning("Refused a deposition for {clientTag}: the session carries no character",
                client?.ClientTag);
            return false;
        }

        // T >= 1: no reference defines a deposition paid with nothing, and a zero-ticket frame would otherwise
        // be priced against a cost of 0 when the ticket table is absent (§6.4).
        if (request.Tickets is null || request.Tickets.Length == 0)
        {
            _logger.Warning("Refused a deposition of {characterName}: the frame carries no ticket_info entry",
                characterName);
            return false;
        }

        var card = await LoadCardAsync(characterName, request.CreatureCardHandle);
        if (card is null)
        {
            return false;
        }

        var (cardItemId, enhance, summonResource) = card.Value;

        // GameContent::GetCreatureFarmTicketCount(GetRate(), GetTransformLevel(), GetEnhance()) (:11866): the
        // rate and the form come from the summon the card holds, the enhance from the card itself, as
        // CreatureService reads a card (CreatureCard.Enhance <- ItemEntity.Enhance).
        var cost = _costs.GetTicketCount(summonResource.Rate, summonResource.Form, enhance);
        if (cost <= 0)
        {
            // The 7.3 table answers 0 for an absent key too, so the two cases share the refusal; the log tells
            // the operator which one it is (docs/packet-specs/6002-foster-creature.md §5.5, A VERIFIER 1).
            _logger.Warning("Refused a deposition of {characterName}: no ticket cost for (rate {rate}, form {form}, "
                + "enhance {enhance}) — the farm's cost table is not loaded", characterName, summonResource.Rate,
                summonResource.Form, enhance);
            return false;
        }

        var tickets = new List<GameActionPackets.EraseItemRequest>();
        var duration = 0;
        var isCash = false;
        long offered = 0;
        for (var i = 0; i < request.Tickets.Length; i++)
        {
            var entry = request.Tickets[i];
            if (entry.TicketCount <= 0)
            {
                _logger.Warning("Refused a deposition of {characterName}: ticket {index} offers {count} units",
                    characterName, i, entry.TicketCount);
                return false;
            }

            var stack = await LoadStackAsync(characterName, entry.TicketHandle);
            if (stack is null)
            {
                _logger.Warning("Refused a deposition of {characterName}: ticket {handle} is not a stack of the bag",
                    characterName, entry.TicketHandle);
                return false;
            }

            if (!_items.TryGetClass((int)stack.ItemResourceId, out var type) || type != ItemType.FarmPass)
            {
                _logger.Warning("Refused a deposition of {characterName}: item {handle} is not a farm pass",
                    characterName, entry.TicketHandle);
                return false;
            }

            if (stack.Amount < entry.TicketCount)
            {
                _logger.Warning("Refused a deposition of {characterName}: ticket {handle} holds {held} of the "
                    + "{asked} offered", characterName, entry.TicketHandle, stack.Amount, entry.TicketCount);
                return false;
            }

            // The duration and the premium flag come from the ticket's resource, never from the frame (:11873-11874).
            if (!_items.TryGetTicket((int)stack.ItemResourceId, out var entryDuration, out var entryCash)
                || entryDuration <= 0)
            {
                _logger.Warning("Refused a deposition of {characterName}: ticket resource {resource} carries no "
                    + "duration", characterName, stack.ItemResourceId);
                return false;
            }

            // From the second entry on, both must match the first (:11787-11796) — one farm row cannot honour a
            // 1-day ticket and a 5-day one at once.
            if (i == 0)
            {
                duration = entryDuration;
                isCash = entryCash;
            }
            else if (entryDuration != duration || entryCash != isCash)
            {
                _logger.Warning("Refused a deposition of {characterName}: ticket {index} ({duration}s, cash {cash}) "
                    + "differs from the first one ({first}s, cash {firstCash})", characterName, i, entryDuration,
                    entryCash, duration, isCash);
                return false;
            }

            offered += entry.TicketCount;
            tickets.Add(new GameActionPackets.EraseItemRequest(entry.TicketHandle, entry.TicketCount));
        }

        if (offered != cost)
        {
            _logger.Warning("Refused a deposition of {characterName}: {offered} tickets offered for a cost of {cost}",
                characterName, offered, cost);
            return false;
        }

        var crackers = new List<GameActionPackets.EraseItemRequest>();
        long crackerTotal = 0;
        for (var i = 0; i < request.Crackers.Length; i++)
        {
            var entry = request.Crackers[i];
            if (entry.CrackerCount <= 0)
            {
                _logger.Warning("Refused a deposition of {characterName}: cracker {index} offers {count} units",
                    characterName, i, entry.CrackerCount);
                return false;
            }

            var stack = await LoadStackAsync(characterName, entry.CrackerHandle);
            if (stack is null)
            {
                _logger.Warning("Refused a deposition of {characterName}: cracker {handle} is not a stack of the bag",
                    characterName, entry.CrackerHandle);
                return false;
            }

            if (!_items.TryGetClass((int)stack.ItemResourceId, out var type) || type != ItemType.CreatureFood)
            {
                _logger.Warning("Refused a deposition of {characterName}: item {handle} is not creature food",
                    characterName, entry.CrackerHandle);
                return false;
            }

            if (stack.Amount < entry.CrackerCount)
            {
                _logger.Warning("Refused a deposition of {characterName}: cracker {handle} holds {held} of the "
                    + "{asked} offered", characterName, entry.CrackerHandle, stack.Amount, entry.CrackerCount);
                return false;
            }

            crackerTotal += entry.CrackerCount;
            crackers.Add(new GameActionPackets.EraseItemRequest(entry.CrackerHandle, entry.CrackerCount));
        }

        // The crackers are priced with the tickets' own number (:11869, :11827); zero crackers is accepted as is.
        if (crackers.Count > 0 && crackerTotal != cost)
        {
            _logger.Warning("Refused a deposition of {characterName}: {offered} crackers offered for a cost of {cost}",
                characterName, crackerTotal, cost);
            return false;
        }

        // FindSuitableFarmSlot(isCash) (:11830-11845, called :11877): slot 0 for the ordinary tickets, 1..2 for
        // the premium ones.
        var farm = await _store.LoadAsync(characterName);
        var taken = farm is null ? Array.Empty<int>() : farm.Select(entry => entry.Slot).ToArray();
        if (!CreatureFarmRules.TryFindSlot(isCash, taken, out var slot))
        {
            _logger.Warning("Refused a deposition of {characterName}: the farm is full for cash {isCash} tickets",
                characterName, isCash);
            return false;
        }

        // max_level is frozen at the deposition: FARM_MAX_LEVEL (100) for a premium ticket, the depositing
        // character's level otherwise (StructPlayer.cpp:11319).
        var maxLevel = isCash
            ? CreatureFarmRules.MaxLevel
            : Math.Max(1, client.ConnectionInfo.CharacterLevel);
        var rowId = await _store.InsertAsync(new FarmedSummonDeposit(characterName, slot, cardItemId, maxLevel,
            crackers.Count > 0, isCash, _localNow(), duration));
        if (rowId == 0)
        {
            _logger.Warning("Refused a deposition of {characterName}: the store wrote no row for card {card}",
                characterName, cardItemId);
            return false;
        }

        // EraseItem, one stack at a time, and only after the row exists (:11881-11891) — a refusal above costs
        // nothing. A failure here leaves the row written, which the log makes visible; the client, told 0, can
        // ask again and will meet the card's farmed flag.
        var erased = await _characters.EraseItemsAsync(characterName, tickets.Concat(crackers).ToArray());
        if (erased is { Count: > 0 })
        {
            client.Connection.Send(GameCharacterPackets.BuildEraseItem(erased));
        }

        _logger.Information("Deposited card {card} of {characterName} in farm slot {slot} for {duration}s "
            + "(cash {isCash}, cracker {cracker}, max level {maxLevel})", cardItemId, characterName, slot,
            duration, isCash, crackers.Count > 0, maxLevel);
        return true;
    }

    /// <summary>
    /// The card of the frame, with the summon resource and the enhance the cost key reads. False-when-absent at
    /// every step, as <c>GetSummonStruct()</c> being null or <c>IsInInventory()</c> being false does in the
    /// reference (:11744-11760).
    /// </summary>
    private async Task<(long CardId, int Enhance, SummonResourceInfo SummonResource)?>
        LoadCardAsync(string characterName, uint cardHandle)
    {
        var state = await _characters.GetCreatureStateAsync(characterName, _creatures.CardIds);
        var record = state?.Cards?.FirstOrDefault(card => card.Card.Id == cardHandle);
        if (record is null || !IsInBag(record.Card))
        {
            _logger.Warning("Refused a deposition of {characterName}: card {handle} is not a card of the bag",
                characterName, cardHandle);
            return null;
        }

        if (record.Summon is null)
        {
            _logger.Warning("Refused a deposition of {characterName}: card {handle} holds no summon",
                characterName, cardHandle);
            return null;
        }

        if (CreatureFarmRules.IsFarmed(record.Card.Flag))
        {
            _logger.Warning("Refused a deposition of {characterName}: card {handle} is already farmed",
                characterName, cardHandle);
            return null;
        }

        if (!_creatures.TryGetSummon(record.Summon.SummonResourceId, out var summon))
        {
            _logger.Warning("Refused a deposition of {characterName}: summon resource {resource} of card {handle} "
                + "is unknown", characterName, record.Summon.SummonResourceId, cardHandle);
            return null;
        }

        return (record.Card.Id, (int)record.Card.Enhance, summon);
    }

    /// <summary>The stack a handle names, null unless it is a live stack of the character's bag.</summary>
    private async Task<ItemEntity> LoadStackAsync(string characterName, uint handle)
    {
        var item = await _characters.GetItemByHandleAsync(characterName, handle);
        return item is not null && IsInBag(item) ? item : null;
    }

    /// <summary>
    /// <c>IsInInventory()</c>: a live bag stack, neither stored, nor auctioned, nor worn. Same idiom as the
    /// quest's own item scan (<c>QuestService.cs:582</c>). The handle is already resolved among the character's
    /// own items, which covers the reference's <c>GetOwnerUID() == player</c>.
    /// </summary>
    private static bool IsInBag(ItemEntity item) =>
        item.Amount > 0 && item.StorageId is null && item.AuctionId is null
        && item.WearInfo == ItemWearType.None && item.EquippedBySummonId is null;
}
