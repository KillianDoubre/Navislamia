using FakeItEasy;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Navislamia.Game.DataAccess.Contexts;
using Navislamia.Game.DataAccess.Entities.Telecaster;
using Navislamia.Game.DataAccess.Repositories;
using Navislamia.Game.DataAccess.Repositories.Interfaces;
using Navislamia.Game.Network.Packets;
using Navislamia.Game.Services;

namespace Tests.Game;

[TestFixture]
public class CharacterCommandsTests
{
    [TestCase("New", false)] [TestCase("Newname", true)]
    [TestCase("Newname12345678901", true)] [TestCase("Newname123456789012", false)]
    [TestCase("Bad_Name", false)] [TestCase("Bad Name", false)] [TestCase("Élodie", false)]
    public void Name_validation_matches_the_current_ascii_transport(string name, bool allowed)
        => CharacterNameRules.Valid(name).Should().Be(allowed);

    [Test]
    public async Task Rename_is_persisted_free_once_and_rejects_duplicates_invalid_case_and_banned_words()
    {
        var options = new DbContextOptionsBuilder<TelecasterContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        await using (var db = new TelecasterContext(options))
        {
            db.Characters.AddRange(new CharacterEntity { Id = 1, CharacterName = "Original" },
                new CharacterEntity { Id = 2, CharacterName = "Existing" });
            db.AuctionListings.AddRange(
                new AuctionListingEntity { Id = 10, SellerId = 1, SellerName = "Original", HighestBidderId = 2, HighestBidderName = "Existing" },
                new AuctionListingEntity { Id = 11, SellerId = 2, SellerName = "Existing", HighestBidderId = 1, HighestBidderName = "Original" });
            await db.SaveChangesAsync();
        }
        var banned = A.Fake<IBannedWordsRepository>();
        A.CallTo(() => banned.ContainsBannedWord("Banned")).Returns(true);
        var service = new CharacterService(A.Fake<IStarterItemsRepository>(), new CharacterRepositoryFactory(options),
            new CharacterGate(), A.Fake<ILogger<CharacterService>>(), bannedWords: banned);
        (await service.RenameCharacterAsync("Original", "Original")).Should().Be(ResultCode.AlreadyExist);
        (await service.RenameCharacterAsync("Original", "Existing")).Should().Be(ResultCode.AlreadyExist);
        (await service.RenameCharacterAsync("Original", "renamed")).Should().Be(ResultCode.InvalidText);
        (await service.RenameCharacterAsync("Original", "Banned")).Should().Be(ResultCode.InvalidText);
        (await service.RenameCharacterAsync("Original", "Renamed")).Should().Be(ResultCode.Success);
        (await service.RenameCharacterAsync("Renamed", "Another")).Should().Be(ResultCode.AccessDenied);
        await using var verify = new TelecasterContext(options);
        var saved = await verify.Characters.SingleAsync(c => c.Id == 1);
        saved.CharacterName.Should().Be("Renamed"); saved.WasNameChanged.Should().BeTrue();
        (await verify.AuctionListings.FindAsync(10L))!.SellerName.Should().Be("Renamed");
        (await verify.AuctionListings.FindAsync(10L))!.HighestBidderName.Should().Be("Existing");
        (await verify.AuctionListings.FindAsync(11L))!.HighestBidderName.Should().Be("Renamed");
        (await verify.AuctionListings.FindAsync(11L))!.SellerName.Should().Be("Existing");
        (await service.SaveChatBlockTimeAsync("Renamed", 120)).Should().BeTrue();
        await verify.Entry(saved).ReloadAsync(); saved.ChatBlockTime.Should().Be(120);
    }
}
