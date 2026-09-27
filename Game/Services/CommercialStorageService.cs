using System;
using System.Threading.Tasks;
using Navislamia.Game.DataAccess.Repositories.Interfaces;
using Navislamia.Game.Network.Clients;
using Navislamia.Game.Network.Packets.Game;
using Serilog;

namespace Navislamia.Game.Services;

/// <summary>
/// The commercial storage (item shop) container, derived from the dump because neither reference
/// implements it (docs/packet-specs/socle-stockage-commercial-conteneur.md §6).
/// <para>
/// What it holds comes from the table <c>PaidItems</c>, which the external item shop writes; nothing in
/// this repository feeds it (§5.10). What it does, it does with the ordinary inventory packets: the server
/// never emits a <c>TM_CS_TAKEOUT_COMMERCIAL_ITEM</c>, and the takeout has no result code at all (§5.6).
/// </para>
/// </summary>
public class CommercialStorageService : ICommercialStorageService
{
    private readonly ILogger _logger = Log.ForContext<CommercialStorageService>();
    private readonly IPaidItemRepository _repository;
    private readonly ICharacterService _characterService;
    private readonly IItemSortCatalog _itemCatalog;

    /// <summary>
    /// The gate <c>CharacterService</c> and <c>StorageService</c> take too, keyed by character. Every step
    /// of the takeout runs under it. It cannot span the whole sequence: <c>CharacterService.AddItemAsync</c>
    /// takes the very same stripe, and <see cref="CharacterGate"/> is a plain non-reentrant
    /// <c>SemaphoreSlim(1, 1)</c> — holding it across the delivery would deadlock the takeout forever
    /// (fiche §11, reserve).
    /// </summary>
    private readonly CharacterGate _gate;

    public CommercialStorageService(IPaidItemRepository repository, CharacterGate gate,
        ICharacterService characterService, IItemSortCatalog itemCatalog)
    {
        _repository = repository;
        _gate = gate;
        _characterService = characterService;
        _itemCatalog = itemCatalog;
    }

    public async Task SendContainerAsync(GameClient client)
    {
        var characterName = client.ConnectionInfo.CharacterName;
        if (string.IsNullOrEmpty(characterName))
        {
            _logger.Warning("Refused to read a commercial storage on {clientTag}: no character in session",
                client.ClientTag);
            return;
        }

        try
        {
            // 10003 first, then 10004, at the position of the world entry sequence (§5.5, §5.7).
            var visible = await _gate.RunAsync(characterName, () => _repository.GetVisibleAsync(characterName));
            var emitted = CommercialStorageRules.EmittedRows(visible);
            if (emitted.Length != visible.Length)
            {
                _logger.Warning(
                    "Left {dropped} of {visible} commercial storage rows of {characterName} out of the frame " +
                    "(unaddressable id, unusable code, or over {max} lines)",
                    visible.Length - emitted.Length, visible.Length, characterName,
                    CommercialStorageRules.MaxEntries);
            }

            var entries = CommercialStorageRules.BuildEntries(visible);
            var counters = CommercialStorageRules.BuildCounters(emitted);

            client.Connection.Send(
                GameCommercialStoragePackets.BuildCommercialStorageInfo(counters.Total, counters.New));
            client.Connection.Send(GameCommercialStoragePackets.BuildCommercialStorageList(entries));

            _logger.Debug("Commercial storage of {characterName}: {total} lines, {new} new", characterName,
                counters.Total, counters.New);
        }
        catch (Exception exception)
        {
            _logger.Error(exception, "Could not read the commercial storage of {characterName}", characterName);
        }
    }

    public async Task HandleTakeoutAsync(GameClient client, GameActionPackets.TakeoutCommercialItemRequest request)
    {
        var characterName = client.ConnectionInfo.CharacterName;
        if (string.IsNullOrEmpty(characterName))
        {
            _logger.Warning("Refused a commercial item takeout on {clientTag}: no character in session",
                client.ClientTag);
            return;
        }

        // 2. Resolution, read only and owner-scoped: the uid alone never resolves a row (§5.2.3).
        var row = await _gate.RunAsync(characterName, () => _repository.ResolveAsync(characterName, request.Uid));
        if (row is null)
        {
            // Not an error: an unknown, foreign or already emptied uid is one and the same answer.
            _logger.Debug("No commercial storage row {uid} for {characterName}: nothing taken", request.Uid,
                characterName);
            return;
        }

        // 3. Bounds: the requested quantity carries no bound of its own (§3.3).
        if (!CommercialStorageRules.TryTakeable(row, request.Count, out var takeable))
        {
            _logger.Debug("Refused a takeout of {count} from row {uid} of {characterName}: 1..{takeable} takeable",
                request.Count, request.Uid, characterName, takeable);
            return;
        }

        // 4. The same catalogue guard the /item command uses: an unknown code would persist an item the
        // client cannot render and the stat catalogue cannot read (§5.6 point 4).
        if (!_itemCatalog.Contains(row.ItemCode))
        {
            _logger.Warning("Refused a takeout of unknown item code {code} for {characterName}", row.ItemCode,
                characterName);
            return;
        }

        // 5. The goods go to the bag through the ordinary packet of the inventory, never through a 10005.
        var added = await _characterService.AddItemAsync(characterName, row.ItemCode, request.Count);
        if (added is null)
        {
            _logger.Error("Could not add item {code} x{count} of the commercial storage of {characterName}; " +
                          "nothing was taken", row.ItemCode, request.Count, characterName);
            return;
        }

        foreach (var packet in GameCharacterPackets.BuildInventory(new[] { added }))
        {
            client.Connection.Send(packet);
        }

        // 6. Consumption: the line keeps its trace, it is never deleted and confirmed is never written.
        var consumed = await _gate.RunAsync(characterName,
            () => _repository.ConsumeAsync(characterName, request.Uid, request.Count));
        if (consumed.Outcome != CommercialTakeoutOutcome.Consumed)
        {
            // The delivery (5) is not undone: the order keeps the loss on the server's side rather than on
            // the player's, and it is the atomicity the fiche assumes (§5.6, reserve §7h).
            _logger.Error("Item {code} x{count} was handed to {characterName} but its row {uid} could not be " +
                          "consumed ({outcome})", row.ItemCode, request.Count, characterName, request.Uid,
                consumed.Outcome);
        }

        // 7. Refresh, then the log of the takeout.
        await SendContainerAsync(client);

        _logger.Debug("{characterName} took {count} of item {code} from the commercial storage (row {uid})",
            characterName, request.Count, row.ItemCode, request.Uid);
    }
}
