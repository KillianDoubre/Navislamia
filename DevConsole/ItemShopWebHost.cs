using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Navislamia.Configuration.Options;
using Navislamia.Game.Services.ItemShop;

namespace DevConsole;

/// <summary>
/// The item shop page (docs/packet-specs/10000-open-item-shop.md, *Boutique livrée*): a small Kestrel host on
/// <see cref="ItemShopOptions.Url"/> whose three routes hand everything to <see cref="IItemShopService"/>. The client
/// opens <c>shop.url?server=…&amp;id=…</c>; any path is accepted for that login so the client's default
/// <c>login.aspx</c> works as well as a bare host.
/// </summary>
public sealed class ItemShopWebHost : IHostedService
{
    private readonly IItemShopService _shop;
    private readonly ItemShopOptions _options;
    private readonly ILogger<ItemShopWebHost> _logger;
    private WebApplication _app;

    public ItemShopWebHost(IItemShopService shop, IOptions<ItemShopOptions> options, ILogger<ItemShopWebHost> logger)
    {
        _shop = shop;
        _options = options.Value;
        _logger = logger;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        if (!_options.Enabled || string.IsNullOrWhiteSpace(_options.Url))
        {
            return;
        }

        try
        {
            var builder = WebApplication.CreateSlimBuilder();
            builder.Logging.ClearProviders();
            builder.WebHost.UseUrls(_options.Url);
            _app = builder.Build();

            _app.MapGet(ItemShopPages.ShopPath, (HttpContext context) => Send(context,
                _shop.ShowAsync(context.Request.Query["s"], context.Request.Query["c"], context.Request.Query["m"])));
            _app.MapPost(ItemShopPages.BuyPath, async (HttpContext context) =>
            {
                var form = await context.Request.ReadFormAsync();
                await Send(context, _shop.BuyAsync(form["s"], form["id"], form["c"]));
            });
            // "{*path}" and not MapFallback(handler), whose pattern skips a path with a dot: login.aspx is the client default.
            _app.MapFallback("{*path}", (HttpContext context) => Send(context, _shop.LoginAsync(context.Request.Query["id"])));

            await _app.StartAsync(cancellationToken);
            _logger.LogInformation("Item shop listening on {url}", _options.Url);
        }
        catch (Exception exception)
        {
            // The game must not fall because the shop port is taken.
            _logger.LogError(exception, "The item shop could not listen on {url}", _options.Url);
            _app = null;
        }
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        if (_app is not null)
        {
            await _app.StopAsync(cancellationToken);
            await _app.DisposeAsync();
        }
    }

    private static async Task Send(HttpContext context, Task<ItemShopWebResponse> pending)
    {
        var response = await pending;
        context.Response.Headers.CacheControl = "no-store";
        if (response.Location is not null)
        {
            context.Response.StatusCode = StatusCodes.Status303SeeOther;
            context.Response.Headers.Location = response.Location;
            return;
        }

        context.Response.StatusCode = response.Status;
        context.Response.ContentType = "text/html; charset=utf-8";
        await context.Response.WriteAsync(response.Html ?? string.Empty);
    }
}
