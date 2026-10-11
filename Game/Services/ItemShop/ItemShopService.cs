using System;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;
using Navislamia.Configuration.Options;
using Navislamia.Game.DataAccess.Repositories.Interfaces;
using Navislamia.Game.Network.Clients;
using Navislamia.Game.Network.Packets.Enums;
using Navislamia.Game.Network.Packets.Game;
using Navislamia.Game.Services.Interfaces;
using Serilog;

namespace Navislamia.Game.Services.ItemShop;

public interface IItemShopService
{
    bool Enabled { get; }

    /// <summary><c>/cshop</c> and 10000: a 10001 with a fresh one-time password (<c>onOpenItemShop</c>).</summary>
    void OpenShop(GameClient client);

    Task<long> GetPointsAsync(long accountId);

    /// <summary>Adds points (negative to take); <c>null</c> when the balance would leave its bounds.</summary>
    Task<long?> AddPointsAsync(long accountId, long delta);

    /// <summary>The page the client opens: <c>?server=…&amp;id=…</c>, the id of <see cref="ItemShopRules.EncodeId"/>.</summary>
    Task<ItemShopWebResponse> LoginAsync(string id);

    Task<ItemShopWebResponse> ShowAsync(string token, string category, string notice);

    Task<ItemShopWebResponse> BuyAsync(string token, string entryId, string category);
}

/// <summary>
/// The item shop of the 7.3 client (docs/packet-specs/10000-open-item-shop.md, *Boutique livrée*). The official game
/// server only handed the client its credentials (<c>onOpenItemShop</c>) and refreshed the Item Box when the auth server
/// relayed a purchase (<c>onItemPurchased</c>/<c>onItemSupplied</c>); the shop itself was Gala Lab's web site. Here the
/// page is served by this process (<c>DevConsole</c>'s <c>ItemShopWebHost</c>), sells the articles of
/// <c>item-shop.73.json</c> for account points, and writes each purchase as an account-wide <c>PaidItems</c> line, which
/// the Item Box (10003/10004/10005) already serves.
/// </summary>
public sealed class ItemShopService : IItemShopService
{
    /// <summary>The line a player in game reads when a purchase lands in the Item Box.</summary>
    public const string PurchaseNotice = "Your Item Shop purchase is waiting in the Item Box of your inventory.";

    private readonly ILogger _logger = Log.ForContext<ItemShopService>();
    private readonly ItemShopOptions _options;
    private readonly string _serverName;
    private readonly ItemShopCatalog _catalog;
    private readonly ItemShopSessions _sessions;
    private readonly IItemShopRepository _repository;
    private readonly ICommercialStorageService _storage;
    private readonly IPlayerVisibilityService _players;
    private readonly SemaphoreSlim[] _accountGates = Enumerable.Range(0, 32).Select(_ => new SemaphoreSlim(1, 1)).ToArray();

    public ItemShopService(IOptions<ItemShopOptions> options, IOptions<ServerOptions> server, ItemShopCatalog catalog,
        IItemShopRepository repository, ICommercialStorageService storage, IPlayerVisibilityService players = null,
        ItemShopSessions sessions = null)
    {
        _options = options.Value;
        _serverName = server.Value?.Name ?? string.Empty;
        _catalog = catalog;
        _repository = repository;
        _storage = storage;
        _players = players;
        _sessions = sessions ?? new ItemShopSessions(TimeSpan.FromSeconds(Math.Max(10, _options.TicketLifetimeSeconds)),
            TimeSpan.FromMinutes(Math.Max(1, _options.SessionMinutes)));
    }

    public bool Enabled => _options.Enabled;

    public void OpenShop(GameClient client)
    {
        var info = client.ConnectionInfo;
        if (!Enabled || info.CharacterHandle == 0)
        {
            _logger.Debug("Item shop request of {clientTag} ignored (shop off or no character)", client.ClientTag);
            return;
        }

        var clientId = (int)info.CharacterHandle;
        var password = _sessions.Issue(clientId, info.AccountId, info.CharacterName);
        client.Connection.Send(GameCommercialStoragePackets.BuildOpenItemShop(clientId, info.AccountId, password, _serverName));
        _logger.Debug("Item shop opened for {characterName} ({clientTag})", info.CharacterName, client.ClientTag);
    }

    public Task<long> GetPointsAsync(long accountId) => _repository.GetPointsAsync(accountId);

    public async Task<long?> AddPointsAsync(long accountId, long delta)
    {
        var gate = Gate(accountId);
        await gate.WaitAsync();
        try
        {
            return await _repository.AddPointsAsync(accountId, delta);
        }
        finally
        {
            gate.Release();
        }
    }

    public Task<ItemShopWebResponse> LoginAsync(string id)
    {
        if (!Enabled)
        {
            return Task.FromResult(ItemShopWebResponse.Refused(ItemShopPages.Message("Item Shop", "The Item Shop is closed.")));
        }

        if (!ItemShopRules.TryDecodeId(id, out var login) || !_sessions.TryRedeem(login, out var session))
        {
            _logger.Information("Refused an item shop login with an unknown or spent id");
            return Task.FromResult(ItemShopWebResponse.Refused(ItemShopPages.Message("Item Shop",
                "This link has expired. Type /cshop in the game to open the shop again.")));
        }

        _logger.Information("Item shop opened by {characterName} (account {accountId})", session.CharacterName,
            session.AccountId);
        return Task.FromResult(ItemShopWebResponse.SeeOther($"{ItemShopPages.ShopPath}?s={session.Token}"));
    }

    public async Task<ItemShopWebResponse> ShowAsync(string token, string category, string notice)
    {
        if (!_sessions.TryGet(token, out var session))
        {
            return Expired();
        }

        var balance = await _repository.GetPointsAsync(session.AccountId);
        return ItemShopWebResponse.Page(ItemShopPages.Shop(session, _catalog, category, balance, NoticeText(notice)));
    }

    public async Task<ItemShopWebResponse> BuyAsync(string token, string entryId, string category)
    {
        if (!_sessions.TryGet(token, out var session))
        {
            return Expired();
        }

        string Back(string notice) =>
            $"{ItemShopPages.ShopPath}?s={session.Token}&c={Uri.EscapeDataString(category ?? string.Empty)}&m={notice}";

        if (!int.TryParse(entryId, NumberStyles.None, CultureInfo.InvariantCulture, out var id)
            || !_catalog.TryGet(id, out var entry))
        {
            return ItemShopWebResponse.SeeOther(Back("unknown"));
        }

        ItemShopPurchaseResult result;
        var gate = Gate(session.AccountId);
        await gate.WaitAsync();
        try
        {
            result = await _repository.PurchaseAsync(new ItemShopPurchase(session.AccountId, session.CharacterName,
                entry.Code, entry.Count, entry.Price, _serverName));
        }
        finally
        {
            gate.Release();
        }

        if (result.Outcome != ItemShopPurchaseOutcome.Bought)
        {
            return ItemShopWebResponse.SeeOther(Back("points"));
        }

        _logger.Information("{characterName} (account {accountId}) bought {count} x item {code} for {price} points",
            session.CharacterName, session.AccountId, entry.Count, entry.Code, entry.Price);
        await NotifyAsync(session.AccountId);
        return ItemShopWebResponse.SeeOther(Back("bought"));
    }

    /// <summary>
    /// <c>onItemSupplied</c> (GameMessage.cpp:11648-11669): the character of the account in game hears of it and gets
    /// its counters again; its Item Box window, if open, asks for the list itself.
    /// </summary>
    private async Task NotifyAsync(long accountId)
    {
        var clients = _players?.Registry?.Clients;
        if (clients is null) return;
        foreach (var client in clients.Where(c => c.ConnectionInfo.CharacterHandle != 0
                                                  && c.ConnectionInfo.AccountId == accountId))
        {
            client.Connection.Send(GameChatPackets.BuildChat("@NOTICE", (byte)ChatType.Notice, PurchaseNotice));
            await _storage.SendInfoAsync(client);
        }
    }

    private static string NoticeText(string notice) => notice switch
    {
        "bought" => "Thank you! Your purchase is in the Item Box of your inventory.",
        "points" => "You do not have enough points for this article.",
        "unknown" => "This article is no longer sold.",
        _ => null
    };

    private static ItemShopWebResponse Expired() => ItemShopWebResponse.Refused(ItemShopPages.Message("Item Shop",
        "Your session has expired. Type /cshop in the game to open the shop again."));

    private SemaphoreSlim Gate(long accountId) => _accountGates[(int)((ulong)accountId % (ulong)_accountGates.Length)];
}
