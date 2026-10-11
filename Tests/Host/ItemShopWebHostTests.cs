using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using DevConsole;
using FakeItEasy;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Navislamia.Configuration.Options;
using Navislamia.Game.Services.ItemShop;

namespace Tests.Host;

/// <summary>
/// The item shop page over a real socket: the client's default <c>login.aspx</c> reaches the login, the page and the
/// purchase form reach the service, and a redirection travels as a 303.
/// </summary>
[TestFixture]
public class ItemShopWebHostTests
{
    [Test]
    public async Task TheRoutes_ReachTheService()
    {
        var shop = A.Fake<IItemShopService>();
        A.CallTo(() => shop.LoginAsync("ABC")).Returns(Task.FromResult(ItemShopWebResponse.SeeOther("/shop?s=T")));
        A.CallTo(() => shop.ShowAsync("T", "Pets", null)).Returns(Task.FromResult(ItemShopWebResponse.Page("<p>ok</p>")));
        A.CallTo(() => shop.BuyAsync("T", "3", "Pets"))
            .Returns(Task.FromResult(ItemShopWebResponse.SeeOther("/shop?s=T&m=bought")));

        var port = FreePort();
        var host = new ItemShopWebHost(shop, Options.Create(new ItemShopOptions { Url = $"http://127.0.0.1:{port}" }),
            NullLogger<ItemShopWebHost>.Instance);
        await host.StartAsync(CancellationToken.None);
        try
        {
            using var http = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false })
            {
                BaseAddress = new Uri($"http://127.0.0.1:{port}")
            };

            var login = await http.GetAsync("/login.aspx?server=Navislamia&id=ABC");
            login.StatusCode.Should().Be(HttpStatusCode.SeeOther);
            login.Headers.Location!.ToString().Should().Be("/shop?s=T");

            var page = await http.GetAsync("/shop?s=T&c=Pets");
            page.StatusCode.Should().Be(HttpStatusCode.OK);
            (await page.Content.ReadAsStringAsync()).Should().Be("<p>ok</p>");
            page.Content.Headers.ContentType!.MediaType.Should().Be("text/html");

            var buy = await http.PostAsync("/buy", new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["s"] = "T", ["id"] = "3", ["c"] = "Pets"
            }));
            buy.StatusCode.Should().Be(HttpStatusCode.SeeOther);
            buy.Headers.Location!.ToString().Should().Contain("m=bought");
        }
        finally
        {
            await host.StopAsync(CancellationToken.None);
        }
    }

    private static int FreePort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }
}
