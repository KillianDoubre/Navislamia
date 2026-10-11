using System;
using System.Buffers.Binary;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using FakeItEasy;
using FluentAssertions;
using Microsoft.Extensions.Options;
using Navislamia.Configuration.Options;
using Navislamia.Game.DataAccess.Repositories.Interfaces;
using Navislamia.Game.Network.Packets.Game;
using Navislamia.Game.Services;
using Navislamia.Game.Services.GmCommands;
using Navislamia.Game.Services.ItemShop;

namespace Tests.Game;

/// <summary>
/// The item shop (docs/packet-specs/10000-open-item-shop.md, *Boutique livrée*): the 10001 frame, the URL id the client
/// builds from it, the one-time passwords, the catalogue, the points and the page.
/// </summary>
[TestFixture]
public class ItemShopTests
{
    private const uint ClientSeed = 0x16ee69e8;

    [Test]
    public void OpenItemShop_Is51BytesWithTheThreeNumbersAndTheZeroPaddedName()
    {
        var packet = GameCommercialStoragePackets.BuildOpenItemShop(1234, 56, 0x00abcdef, "Navislamia");

        packet.Length.Should().Be(51);
        BinaryPrimitives.ReadUInt32LittleEndian(packet.AsSpan(0, 4)).Should().Be(51);
        BinaryPrimitives.ReadUInt16LittleEndian(packet.AsSpan(4, 2)).Should().Be(10001);
        packet[6].Should().Be(StorageTestHarness.Checksum(packet));
        BinaryPrimitives.ReadInt32LittleEndian(packet.AsSpan(7, 4)).Should().Be(1234, "client_id @7");
        BinaryPrimitives.ReadInt32LittleEndian(packet.AsSpan(11, 4)).Should().Be(56, "account_id @11");
        BinaryPrimitives.ReadInt32LittleEndian(packet.AsSpan(15, 4)).Should().Be(0x00abcdef, "one_time_password @15");
        Encoding.ASCII.GetString(packet, 19, 10).Should().Be("Navislamia");
        packet.Skip(29).Should().OnlyContain(b => b == 0);
    }

    [Test]
    public void OpenItemShop_CutsALongNameAndKeepsItsZero()
    {
        var packet = GameCommercialStoragePackets.BuildOpenItemShop(1, 2, 3, new string('x', 40));

        packet.Skip(19).Take(31).Should().OnlyContain(b => b == (byte)'x');
        packet[50].Should().Be(0, "the client copies the name with strcpy");
    }

    [Test]
    public void TheUrlId_DecodesWhatTheClientEncodes()
    {
        // getItemShopURL(n1 = one_time_password, n2 = client_id, n3 = account_id) with the client's constant seed.
        var id = ItemShopRules.EncodeId(ClientSeed, 0x00123456, 4242, 77);

        id.Should().HaveLength(40).And.StartWith("16EE69E8");
        ItemShopRules.TryDecodeId(id, out var login).Should().BeTrue();
        login.Should().Be(new ItemShopLogin(0x00123456, 4242, 77));
    }

    [Test]
    public void TheUrlId_MatchesGalaLabsEncoderByHand()
    {
        // Form1.button1_Click: n1 ^= seed, n2 ^= n1, n3 ^= n2, checksum ^= n3, then the four keys.
        uint n1 = 5, n2 = 6, n3 = 7, seed = 0x01020304;
        var checksum = n1 + n2 + n3;
        n1 ^= seed; n2 ^= n1; n3 ^= n2; checksum ^= n3;
        var expected = seed.ToString("X8") + (n1 ^ 0xD8FB51A9).ToString("X8") + (n2 ^ 0x9DC720AC).ToString("X8")
                       + (n3 ^ 0x31F42CB7).ToString("X8") + (checksum ^ 0x7F9B3D2E).ToString("X8");

        ItemShopRules.EncodeId(seed, 5, 6, 7).Should().Be(expected);
    }

    [TestCase("")]
    [TestCase("16EE69E8")]
    [TestCase("ZZEE69E8C1F3B2A0C1F3B2A0C1F3B2A0C1F3B2A0")]
    public void TheUrlId_RefusesWhatIsNotFortyHexDigits(string id)
    {
        ItemShopRules.TryDecodeId(id, out _).Should().BeFalse();
    }

    [Test]
    public void TheUrlId_RefusesABrokenChecksum()
    {
        var id = ItemShopRules.EncodeId(ClientSeed, 1, 2, 3);
        var forged = id[..39] + (id[39] == '0' ? '1' : '0');

        ItemShopRules.TryDecodeId(forged, out _).Should().BeFalse();
    }

    [Test]
    public void APassword_OpensOnePageOnce()
    {
        var sessions = new ItemShopSessions(TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(30), draw: () => 999);
        var password = sessions.Issue(42, 7, "Killian");

        sessions.TryRedeem(new ItemShopLogin(password, 42, 7), out var session).Should().BeTrue();
        session.AccountId.Should().Be(7);
        session.CharacterName.Should().Be("Killian");
        sessions.TryGet(session.Token, out _).Should().BeTrue();
        sessions.TryRedeem(new ItemShopLogin(password, 42, 7), out _).Should().BeFalse("a password is spent once");
    }

    [Test]
    public void APassword_MustMatchItsCharacterAndAccount()
    {
        var sessions = new ItemShopSessions(TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(30), draw: () => 999);
        sessions.Issue(42, 7, "Killian");

        sessions.TryRedeem(new ItemShopLogin(998, 42, 7), out _).Should().BeFalse();
        sessions.TryRedeem(new ItemShopLogin(999, 43, 7), out _).Should().BeFalse();
        sessions.TryRedeem(new ItemShopLogin(999, 42, 8), out _).Should().BeFalse();
    }

    [Test]
    public void PasswordsAndPages_Expire()
    {
        var now = new DateTime(2026, 10, 11, 12, 0, 0, DateTimeKind.Utc);
        var sessions = new ItemShopSessions(TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(30), () => now, () => 5);
        sessions.Issue(1, 1, "A");
        now = now.AddMinutes(6);
        sessions.TryRedeem(new ItemShopLogin(5, 1, 1), out _).Should().BeFalse("the password expired");

        sessions.Issue(1, 1, "A");
        sessions.TryRedeem(new ItemShopLogin(5, 1, 1), out var session).Should().BeTrue();
        now = now.AddMinutes(29);
        sessions.TryGet(session.Token, out _).Should().BeTrue("a click pushes the expiry back");
        now = now.AddMinutes(31);
        sessions.TryGet(session.Token, out _).Should().BeFalse();
    }

    [Test]
    public void TheCatalogue_KeepsOnlyValidArticlesOfKnownItems()
    {
        var catalog = new ItemShopCatalog(new[]
        {
            Entry(1, "Utility", 910006, 5, 50),
            Entry(1, "Utility", 910007, 5, 50),
            Entry(2, "Pets", 690403, 1, 800),
            Entry(3, "Pets", 123, 1, 800),
            Entry(4, "Pets", 690412, 0, 800),
            Entry(5, "Pets", 690412, 1, 0)
        }, code => code != 123);

        catalog.Entries.Select(e => e.Id).Should().Equal(1, 2);
        catalog.Entries[0].Code.Should().Be(910006, "an id seen twice keeps its first article");
        catalog.Categories.Should().Equal("Utility", "Pets");
    }

    [Test]
    public void TheShippedCatalogue_IsValid()
    {
        var root = new DirectoryInfo(TestContext.CurrentContext.TestDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "DevConsole", "item-shop.73.json")))
        {
            root = root.Parent;
        }

        var path = Path.Combine(root!.FullName, "DevConsole", "item-shop.73.json");
        using var document = JsonDocument.Parse(File.ReadAllText(path));
        var options = document.RootElement.GetProperty("ItemShop").Deserialize<ItemShopOptions>();

        var catalog = new ItemShopCatalog(options.Entries, _ => true);
        catalog.Entries.Should().HaveCount(options.Entries.Count, "every shipped article is valid");
        catalog.Entries.Should().OnlyContain(e => !string.IsNullOrWhiteSpace(e.Name));
    }

    [TestCase(0, 10, true, 10)]
    [TestCase(10, -10, true, 0)]
    [TestCase(10, -11, false, 10)]
    [TestCase(ItemShopRules.MaxPoints, 1, false, ItemShopRules.MaxPoints)]
    public void Points_NeverLeaveTheirBounds(long balance, long delta, bool accepted, long result)
    {
        ItemShopRules.TryAdd(balance, delta, out var after).Should().Be(accepted);
        after.Should().Be(result);
    }

    [Test]
    public void ATakeout_StacksAJoinableItemAndSplitsTheOthers()
    {
        CommercialStorageRules.DeliveryStacks(5, joinable: true).Should().Equal(5L);
        CommercialStorageRules.DeliveryStacks(3, joinable: false).Should().Equal(1L, 1L, 1L);
        CommercialStorageRules.DeliveryStacks(0, joinable: false).Should().BeEmpty();
    }

    [Test]
    public void TheShopCommands_AreOpenToEveryPlayerButTheCredit()
    {
        GmCommandCatalog.TryResolve("cshop", 0, out _).Should().BeTrue();
        GmCommandCatalog.TryResolve("cstorage", 0, out _).Should().BeTrue();
        GmCommandCatalog.TryResolve("cash", 0, out _).Should().BeTrue();
        GmCommandCatalog.TryResolve("addcash", 0, out _).Should().BeFalse();
        GmCommandCatalog.TryResolve("addcash", GmCommandRules.GmPermission, out _).Should().BeTrue();
    }

    [Test]
    public async Task TheLogin_RedirectsToThePageAndTheLinkCannotBeReused()
    {
        var harness = Service();
        var password = harness.Sessions.Issue(42, 7, "Killian");
        var id = ItemShopRules.EncodeId(ClientSeed, password, 42, 7);

        var first = await harness.Service.LoginAsync(id);
        first.Status.Should().Be(303);
        first.Location.Should().StartWith("/shop?s=");

        (await harness.Service.LoginAsync(id)).Status.Should().Be(403);
    }

    [Test]
    public async Task ThePage_ShowsTheArticlesAndTheBalance()
    {
        var harness = Service();
        A.CallTo(() => harness.Repository.GetPointsAsync(7)).Returns(Task.FromResult(1234L));
        var token = await Login(harness);

        var page = await harness.Service.ShowAsync(token, "Utility", null);

        page.Status.Should().Be(200);
        page.Html.Should().Contain("Feather of Return").And.Contain("1,234").And.Contain("Killian");
    }

    [Test]
    public async Task APurchase_WritesTheLineAndTellsThePlayer()
    {
        var harness = Service();
        A.CallTo(() => harness.Repository.PurchaseAsync(A<ItemShopPurchase>._))
            .Returns(Task.FromResult(new ItemShopPurchaseResult(ItemShopPurchaseOutcome.Bought, 950)));
        var token = await Login(harness);

        var answer = await harness.Service.BuyAsync(token, "1", "Utility");

        answer.Status.Should().Be(303);
        answer.Location.Should().Contain("m=bought");
        A.CallTo(() => harness.Repository.PurchaseAsync(A<ItemShopPurchase>.That.Matches(p =>
            p.AccountId == 7 && p.Code == 910006 && p.Count == 5 && p.Price == 50 && p.ServerName == "Navislamia")))
            .MustHaveHappenedOnceExactly();
    }

    [Test]
    public async Task APurchase_WithoutEnoughPoints_SaysSo()
    {
        var harness = Service();
        A.CallTo(() => harness.Repository.PurchaseAsync(A<ItemShopPurchase>._))
            .Returns(Task.FromResult(new ItemShopPurchaseResult(ItemShopPurchaseOutcome.NotEnoughPoints, 10)));
        var token = await Login(harness);

        (await harness.Service.BuyAsync(token, "1", "Utility")).Location.Should().Contain("m=points");
        (await harness.Service.BuyAsync(token, "99", "Utility")).Location.Should().Contain("m=unknown");
        A.CallTo(() => harness.Repository.PurchaseAsync(A<ItemShopPurchase>._)).MustHaveHappenedOnceExactly();
    }

    [Test]
    public async Task AnUnknownToken_IsRefused()
    {
        var harness = Service();

        (await harness.Service.ShowAsync("nope", null, null)).Status.Should().Be(403);
        (await harness.Service.BuyAsync("nope", "1", null)).Status.Should().Be(403);
        A.CallTo(() => harness.Repository.PurchaseAsync(A<ItemShopPurchase>._)).MustNotHaveHappened();
    }

    private sealed record Harness(ItemShopService Service, IItemShopRepository Repository, ItemShopSessions Sessions);

    private static Harness Service()
    {
        var repository = A.Fake<IItemShopRepository>();
        var sessions = new ItemShopSessions(TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(30));
        var options = Options.Create(new ItemShopOptions());
        var catalog = new ItemShopCatalog(new[] { Entry(1, "Utility", 910006, 5, 50, "Feather of Return") }, _ => true);
        var service = new ItemShopService(options, Options.Create(new ServerOptions { Name = "Navislamia" }), catalog,
            repository, A.Fake<ICommercialStorageService>(), null, sessions);
        return new Harness(service, repository, sessions);
    }

    private static async Task<string> Login(Harness harness)
    {
        var password = harness.Sessions.Issue(42, 7, "Killian");
        var answer = await harness.Service.LoginAsync(ItemShopRules.EncodeId(ClientSeed, password, 42, 7));
        return answer.Location["/shop?s=".Length..];
    }

    private static ItemShopEntryOptions Entry(int id, string category, int code, int count, long price,
        string name = "Article") => new()
    {
        Id = id, Category = category, Code = code, Count = count, Price = price, Name = name
    };
}
