using System.Buffers.Binary;
using FakeItEasy;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Navislamia.Game.DataAccess.Contexts;
using Navislamia.Game.DataAccess.Entities.Enums;
using Navislamia.Game.DataAccess.Entities.Telecaster;
using Navislamia.Game.Network.Packets;
using Navislamia.Game.Network.Packets.Game;
using Navislamia.Game.Services;
using System.Reflection;

namespace Tests.Game;

[TestFixture]
public class DonationRankingTests
{
    [Test]
    public async Task Packet_5000_reads_the_persistent_source_and_sends_5001()
    {
        var options = Options(); var clock = new Clock();
        await using (var db = new TelecasterContext(options))
        {
            db.Characters.Add(new CharacterEntity { Id = 1, CharacterName = "Donor" });
            db.DonationScores.Add(new DonationScoreEntity { CharacterId = 1, Period = 202610, Score = 123.4567m });
            await db.SaveChangesAsync();
        }
        var connection = new StorageTestHarness.FrameConnection(Array.Empty<byte>());
        var client = StorageTestHarness.NewGameClient(connection, donationRankingService: new DonationRankingService(options, clock));
        StorageTestHarness.Session(client).CharacterHandle = 1;
        typeof(Navislamia.Game.Network.Clients.GameClient).GetMethod("HandleRankingTopRecord", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(client, new object[] { GameGuildPackets.Frame(5000, 8) });
        for (var i = 0; i < 100 && connection.Sent.Count == 0; i++) await Task.Delay(10);
        var packet = connection.Sent.Single(); BinaryPrimitives.ReadUInt16LittleEndian(packet.AsSpan(4)).Should().Be(5001);
        BinaryPrimitives.ReadInt64LittleEndian(packet.AsSpan(10)).Should().Be(1_234_567);
    }
    private sealed class Clock : TimeProvider
    {
        public DateTimeOffset Now = new(2026, 10, 4, 12, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => Now;
    }
    private static DbContextOptions<TelecasterContext> Options() => new DbContextOptionsBuilder<TelecasterContext>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString(), o => o.EnableNullChecks(false)).Options;

    [Test]
    public async Task Actual_donations_are_atomic_ranked_and_survive_a_new_service()
    {
        var options = Options(); var clock = new Clock();
        var client = StorageTestHarness.NewGameClient(new StorageTestHarness.FrameConnection(Array.Empty<byte>()));
        var info = StorageTestHarness.Session(client); info.CharacterHandle = 1; info.CharacterName = "Donor";
        info.CharacterGold = 2_000_000; info.CharacterJp = 50;
        await using (var db = new TelecasterContext(options))
        {
            db.Characters.Add(new CharacterEntity { Id = 1, CharacterName = "Donor" });
            db.Items.Add(new ItemEntity { Id = 10, CharacterId = 1, ItemResourceId = 7, Amount = 3, WearInfo = ItemWearType.None });
            await db.SaveChangesAsync();
        }
        var catalog = A.Fake<IItemSellCatalog>();
        ItemSellTemplate ignored;
        A.CallTo(() => catalog.TryGetTemplate(7, out ignored)).Returns(true)
            .AssignsOutAndRefParameters(new ItemSellTemplate(1, 200_000));
        var store = new DonationStore(options, new CharacterGate(), catalog, clock);
        var invalid = new GameActionPackets.DonateItemRequest(1_000_000, 10,
            new[] { new GameActionPackets.DonateItemEntry(10, 1), new GameActionPackets.DonateItemEntry(99, 1) });
        (await store.DonateAsync(client, invalid)).Should().Be(ResultCode.NotExist);
        info.CharacterGold.Should().Be(2_000_000); info.CharacterJp.Should().Be(50);
        await using (var db = new TelecasterContext(options))
        { (await db.Items.SingleAsync()).Amount.Should().Be(3); (await db.DonationScores.CountAsync()).Should().Be(0); }
        var valid = new GameActionPackets.DonateItemRequest(1_000_000, 10,
            new[] { new GameActionPackets.DonateItemEntry(10, 2) });
        (await store.DonateAsync(client, valid)).Should().Be(ResultCode.Success);
        await using (var db = new TelecasterContext(options))
        {
            (await db.Items.SingleAsync()).Amount.Should().Be(1);
            (await db.DonationScores.SingleAsync()).Score.Should().Be(140m);
            var character = await db.Characters.SingleAsync(); character.Gold.Should().Be(1_000_000);
            character.Jp.Should().Be(40); character.ImmoralPoint.Should().Be(-140m);
        }
        // Spending moral points or changing PK morality cannot erase a donation.
        info.ImmoralPoint = 500;
        var packet = await new DonationRankingService(options, clock).GetAsync(1, 0);
        BinaryPrimitives.ReadUInt16LittleEndian(packet.AsSpan(8)).Should().Be(1);
        BinaryPrimitives.ReadInt64LittleEndian(packet.AsSpan(10)).Should().Be(1_400_000);
        BinaryPrimitives.ReadUInt16LittleEndian(packet.AsSpan(18)).Should().Be(1);
        clock.Now = clock.Now.AddMonths(1);
        var next = await new DonationRankingService(options, clock).GetAsync(1, 0);
        BinaryPrimitives.ReadUInt16LittleEndian(next.AsSpan(8)).Should().Be(ushort.MaxValue);
        BinaryPrimitives.ReadInt64LittleEndian(next.AsSpan(10)).Should().Be(0);
        await using var old = new TelecasterContext(options); (await old.DonationScores.SingleAsync()).Score.Should().Be(140m);
    }

    [Test]
    public async Task Top_ten_threshold_global_requester_rank_ties_and_invalid_type()
    {
        var options = Options(); var clock = new Clock();
        await using (var db = new TelecasterContext(options))
        {
            for (var id = 1; id <= 13; id++)
            {
                db.Characters.Add(new CharacterEntity { Id = id, CharacterName = "Player" + id });
                db.DonationScores.Add(new DonationScoreEntity { CharacterId = id, Period = 202610, Score = id == 13 ? .0001m : 200 });
            }
            await db.SaveChangesAsync();
        }
        var service = new DonationRankingService(options, clock);
        var packet = await service.GetAsync(13, 0);
        BinaryPrimitives.ReadUInt16LittleEndian(packet.AsSpan(8)).Should().Be(13);
        BinaryPrimitives.ReadInt64LittleEndian(packet.AsSpan(10)).Should().Be(1);
        BinaryPrimitives.ReadUInt16LittleEndian(packet.AsSpan(18)).Should().Be(10);
        packet.Length.Should().Be(430);
        (await service.GetAsync(1, 1)).Should().BeNull();
        (await service.GetAsync(1, -1)).Should().BeNull();
    }
}
