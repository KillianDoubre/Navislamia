using System.Collections.Generic;

namespace Navislamia.Configuration.Options;

/// <summary>
/// The item shop: the web page the 7.3 client opens on a <c>TM_SC_OPEN_ITEM_SHOP</c> (10001) and the points it is paid
/// with (docs/packet-specs/10000-open-item-shop.md, *Boutique livrée*). <see cref="Enabled"/>, <see cref="Url"/> and
/// the lifetimes come from the <c>ItemShop</c> section of <c>appsettings</c>; <see cref="Entries"/> from
/// <c>DevConsole/item-shop.73.json</c>, read with System.Text.Json at startup like the other catalogues.
/// </summary>
public class ItemShopOptions
{
    /// <summary>Whether <c>/cshop</c> answers and the web page listens.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Where the web page listens (Kestrel). The client is told nothing of it: its own <c>shop.url</c> option
    /// (<c>launch-client.ps1 -ShopUrl</c>) must point here.
    /// </summary>
    public string Url { get; set; } = "http://127.0.0.1:4680";

    /// <summary>How long the one-time password of a 10001 can open the page (the official keeps it in the database).</summary>
    public int TicketLifetimeSeconds { get; set; } = 300;

    /// <summary>How long an opened page stays logged in without a click.</summary>
    public int SessionMinutes { get; set; } = 30;

    public List<ItemShopEntryOptions> Entries { get; set; } = new();
}

/// <summary>One article: <see cref="Count"/> units of item <see cref="Code"/> for <see cref="Price"/> points.</summary>
public class ItemShopEntryOptions
{
    public int Id { get; set; }
    public string Category { get; set; } = string.Empty;
    public int Code { get; set; }
    public int Count { get; set; } = 1;
    public long Price { get; set; }
    public string Name { get; set; } = string.Empty;
}
