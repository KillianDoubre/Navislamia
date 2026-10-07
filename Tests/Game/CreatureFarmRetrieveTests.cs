using System;
using System.Buffers.Binary;
using System.Text;
using System.Threading.Tasks;
using FakeItEasy;
using FluentAssertions;
using Navislamia.Game.DataAccess.Entities.Enums;
using Navislamia.Game.Network.Clients;
using Navislamia.Game.Network.Packets.Enums;
using Navislamia.Game.Network.Packets.Game;
using Navislamia.Game.Services.Creatures;
using NUnit.Framework;

namespace Tests.Game;

/// <summary>
/// The retrieval of a farmed summon — <c>TM_CS_RETRIEVE_CREATURE</c> (6004) answered with
/// <c>TM_SC_RESULT_RETRIEVE</c> (6005) — and the gesture behind it
/// (<c>StructPlayer::RegainSummon</c>). See docs/packet-specs/6004-retrieve-creature.md §3.3, §5.2-§5.5.
/// </summary>
[TestFixture]
public class CreatureFarmRetrieveTests
{
    private const int HeaderSize = 7;
    private const int ResultLength = HeaderSize + 1;
    private const int ResultOffset = HeaderSize;
    private const uint CardHandle = 4242;
    private const int SummonCode = 2101;

    private readonly DateTime _now = new(2026, 10, 7, 12, 0, 0, DateTimeKind.Utc);

    private static byte Checksum(byte[] packet)
    {
        byte checksum = 0;
        for (var i = 0; i < 6; i++)
        {
            checksum += packet[i];
        }

        return checksum;
    }

    private static byte[] ClientFrame(ushort id, int length, uint? handle = null)
    {
        var packet = new byte[length];
        BinaryPrimitives.WriteUInt32LittleEndian(packet.AsSpan(0, 4), (uint)length);
        BinaryPrimitives.WriteUInt16LittleEndian(packet.AsSpan(4, 2), id);
        if (handle is { } value && length >= HeaderSize + 4)
        {
            BinaryPrimitives.WriteUInt32LittleEndian(packet.AsSpan(HeaderSize, 4), value);
        }

        packet[6] = Checksum(packet);
        return packet;
    }

    private static byte[] RetrieveFrame(uint handle) =>
        ClientFrame((ushort)GamePackets.TM_CS_RETRIEVE_CREATURE, HeaderSize + 4, handle);

    private static FarmedSummon Row(long cardItemId, int maxLevel, bool cracker, DateTime registration, int duration)
        => new(Id: 7, Slot: 0, CardItemId: cardItemId, MaxLevel: maxLevel, IsUsingCracker: cracker, IsCash: false,
            RegistrationTime: registration, Duration: duration, NursingTime: null, Experience: 0,
            Name: "Poulet", CardInfo: default);

    // --- §3.3 / §3.4: the response frame and its offsets -----------------------------------------------

    [Test]
    public void ResultRetrieveFrame_IsEightBytesWithTheResultAtOffsetSeven()
    {
        var packet = GameFarmPackets.BuildResultRetrieve(GameFarmPackets.RetrieveResultAccepted);

        // The 7.3 server builds as many bytes (movl $0x8 / movw $0x1775, 0x14011e7e1-0x14011e7eb) and the
        // client reads one byte at +7 and nothing else (mov 0x7(%ecx),%dl, 0x67231e).
        packet.Length.Should().Be(8);
        GameFarmPackets.ResultRetrieveLength.Should().Be(ResultLength);
        GameFarmPackets.ResultRetrieveOffset.Should().Be(ResultOffset);
        BinaryPrimitives.ReadUInt32LittleEndian(packet.AsSpan(0, 4)).Should().Be(8);
        BinaryPrimitives.ReadUInt16LittleEndian(packet.AsSpan(4, 2)).Should().Be(6005);
        packet[6].Should().Be(Checksum(packet), "the checksum sums bytes 0..5");
        packet[7].Should().Be(1, "result");
    }

    [Test]
    public void ResultRetrieveFrame_CarriesTheRefusalAsZero()
    {
        GameFarmPackets.RetrieveResultAccepted.Should().Be(1);
        GameFarmPackets.RetrieveResultRefused.Should().Be(0);

        var accepted = GameFarmPackets.BuildResultRetrieve(GameFarmPackets.RetrieveResultAccepted);
        var refused = GameFarmPackets.BuildResultRetrieve(GameFarmPackets.RetrieveResultRefused);

        accepted[7].Should().Be(1);
        refused[7].Should().Be(0);
        refused[6].Should().Be(Checksum(refused), "the refusal changes the result byte, so it changes the checksum");
        refused.AsSpan(0, 6).ToArray().Should().Equal(accepted.AsSpan(0, 6).ToArray(),
            "only the result byte and the checksum it feeds differ between the two answers");
    }

    // --- §5.2-§5.3: the receive loop ---------------------------------------------------------------

    [Test]
    public void RetrieveCreatureRequest_IsAnsweredWithTheAcceptedResult()
    {
        var farm = A.Fake<ICreatureFarmService>();
        A.CallTo(() => farm.RetrieveCreatureAsync(A<GameClient>._, CardHandle)).Returns(true);
        var connection = new StorageTestHarness.FrameConnection(RetrieveFrame(CardHandle));
        var client = StorageTestHarness.NewGameClient(connection, creatureFarmService: farm);

        client.OnDataReceived(connection.BytesAvailable);

        StorageTestHarness.WaitFor(() => connection.Sent.Count > 0);
        connection.Sent.Should().ContainSingle("a well formed 6004 is always answered");
        connection.Sent[0].Length.Should().Be(ResultLength);
        BinaryPrimitives.ReadUInt16LittleEndian(connection.Sent[0].AsSpan(4, 2)).Should().Be(6005);
        connection.Sent[0][7].Should().Be(1, "the card was taken back");
        A.CallTo(() => farm.RetrieveCreatureAsync(A<GameClient>._, CardHandle)).MustHaveHappenedOnceExactly();
    }

    [Test]
    public void RetrieveCreatureRequest_IsAnsweredWithTheRefusalWhenTheGestureRefuses()
    {
        var farm = A.Fake<ICreatureFarmService>();
        A.CallTo(() => farm.RetrieveCreatureAsync(A<GameClient>._, CardHandle)).Returns(false);
        var connection = new StorageTestHarness.FrameConnection(RetrieveFrame(CardHandle));
        var client = StorageTestHarness.NewGameClient(connection, creatureFarmService: farm);

        client.OnDataReceived(connection.BytesAvailable);

        StorageTestHarness.WaitFor(() => connection.Sent.Count > 0);
        connection.Sent.Should().ContainSingle();
        connection.Sent[0][7].Should().Be(0, "the client is told the gesture failed, not left without an answer");
    }

    [Test]
    public void RetrieveCreatureRequest_IsAnsweredWithTheRefusalWithoutAFarmService()
    {
        // The harness of the farm socle carries no farm: nothing can be taken back, and the client still
        // gets its answer.
        var connection = new StorageTestHarness.FrameConnection(RetrieveFrame(CardHandle));
        var client = StorageTestHarness.NewGameClient(connection);

        client.OnDataReceived(connection.BytesAvailable);

        StorageTestHarness.WaitFor(() => connection.Sent.Count > 0);
        connection.Sent.Should().ContainSingle();
        connection.Sent[0][7].Should().Be(0);
    }

    [Test]
    public void MalformedRetrieveCreatureRequest_IsDroppedWithoutAnAnswer()
    {
        // 10 bytes: the client builds only the 11-byte form, so a shorter one is not a request (and answers
        // nothing at all, rather than a refusal of a gesture that was never asked).
        var connection = new StorageTestHarness.FrameConnection(
            ClientFrame((ushort)GamePackets.TM_CS_RETRIEVE_CREATURE, HeaderSize + 3));
        var farm = A.Fake<ICreatureFarmService>();
        var client = StorageTestHarness.NewGameClient(connection, creatureFarmService: farm);

        var receive = () => client.OnDataReceived(connection.BytesAvailable);

        receive.Should().NotThrow();
        connection.Sent.Should().BeEmpty();
        A.CallTo(farm).MustNotHaveHappened();
    }

    [Test]
    public void IncomingResultRetrieve_IsLoggedAndDroppedWithoutThrowing()
    {
        // 6005 is declared in GamePackets, so it needs an arm before the throwing switch even though the 7.3
        // client never sends it: an incoming one is a protocol anomaly.
        var connection = new StorageTestHarness.FrameConnection(
            ClientFrame((ushort)GamePackets.TM_SC_RESULT_RETRIEVE, ResultLength));
        var client = StorageTestHarness.NewGameClient(connection);

        var receive = () => client.OnDataReceived(connection.BytesAvailable);

        receive.Should().NotThrow();
        connection.Sent.Should().BeEmpty();
    }

    // --- §5.5: RegainSummon, the gesture itself -----------------------------------------------------

    [Test]
    public async Task RetrieveCreature_GrantsTheFarmedExperienceAndRemovesTheRow()
    {
        var (service, store, creatures, _) = Harness();
        var card = Card();
        var row = Row(CardHandle, maxLevel: 60, cracker: false, registration: _now.AddHours(-10), duration: 86400);
        A.CallTo(() => store.LoadAsync("Farmer"))
            .Returns(Task.FromResult<System.Collections.Generic.IReadOnlyList<FarmedSummon>>(new[] { row }));
        A.CallTo(() => creatures.FindCard(A<ConnectionInfo>._, CardHandle)).Returns(card);
        A.CallTo(() => store.RemoveAsync("Farmer", CardHandle)).Returns(Task.FromResult(true));

        var retrieved = await service.RetrieveCreatureAsync(Client(), CardHandle);

        retrieved.Should().BeTrue();
        // 10 whole hours at 137 700/h (the 7.3 constant), capped by NeedExp(60) - 1 = 999 999.
        A.CallTo(() => creatures.GainExperience(A<GameClient>._, card, 999_999, true)).MustHaveHappenedOnceExactly();
        A.CallTo(() => store.RemoveAsync("Farmer", CardHandle)).MustHaveHappenedOnceExactly();
    }

    [Test]
    public async Task RetrieveCreature_RemovesTheRowEvenWhenTheCapLeavesNothingToGrant()
    {
        var (service, store, creatures, catalog) = Harness();
        var card = Card();
        card.Exp = 999_999;
        var row = Row(CardHandle, maxLevel: 60, cracker: false, registration: _now.AddHours(-10), duration: 86400);
        A.CallTo(() => store.LoadAsync("Farmer"))
            .Returns(Task.FromResult<System.Collections.Generic.IReadOnlyList<FarmedSummon>>(new[] { row }));
        A.CallTo(() => creatures.FindCard(A<ConnectionInfo>._, CardHandle)).Returns(card);
        A.CallTo(() => store.RemoveAsync("Farmer", CardHandle)).Returns(Task.FromResult(true));

        var retrieved = await service.RetrieveCreatureAsync(Client(), CardHandle);

        retrieved.Should().BeTrue("the card is given back even when the summon's curve has nothing left to give");
        A.CallTo(() => catalog.NeedExp(60)).MustHaveHappened();
        A.CallTo(creatures).Where(call => call.Method.Name == nameof(ICreatureService.GainExperience))
            .MustNotHaveHappened();
        A.CallTo(() => store.RemoveAsync("Farmer", CardHandle)).MustHaveHappenedOnceExactly();
    }

    [Test]
    public async Task RetrieveCreature_UsesTheGrowthRateAndTheGrowthFormCap()
    {
        var (service, store, creatures, catalog) = Harness(form: 2, needExp: 5_000_000);
        var card = Card();
        var row = Row(CardHandle, maxLevel: 100, cracker: false, registration: _now.AddHours(-10), duration: 86400);
        A.CallTo(() => store.LoadAsync("Farmer"))
            .Returns(Task.FromResult<System.Collections.Generic.IReadOnlyList<FarmedSummon>>(new[] { row }));
        A.CallTo(() => creatures.FindCard(A<ConnectionInfo>._, CardHandle)).Returns(card);
        A.CallTo(() => store.RemoveAsync("Farmer", CardHandle)).Returns(Task.FromResult(true));

        var retrieved = await service.RetrieveCreatureAsync(Client(), CardHandle);

        retrieved.Should().BeTrue();
        // 10 whole hours at 347 264/h, capped by NeedExp(min(100, 100, 115)) - 1 = 9 999 999.
        A.CallTo(() => catalog.NeedExp(100)).MustHaveHappened();
        A.CallTo(() => creatures.GainExperience(A<GameClient>._, card, 3_472_640, true)).MustHaveHappenedOnceExactly();
    }

    [Test]
    public async Task RetrieveCreature_MultipliesTheGainByTheCracker()
    {
        var (service, store, creatures, _) = Harness(needExp: 5_000_000);
        var card = Card();
        var row = Row(CardHandle, maxLevel: 60, cracker: true, registration: _now.AddHours(-10), duration: 86400);
        A.CallTo(() => store.LoadAsync("Farmer"))
            .Returns(Task.FromResult<System.Collections.Generic.IReadOnlyList<FarmedSummon>>(new[] { row }));
        A.CallTo(() => creatures.FindCard(A<ConnectionInfo>._, CardHandle)).Returns(card);
        A.CallTo(() => store.RemoveAsync("Farmer", CardHandle)).Returns(Task.FromResult(true));

        var retrieved = await service.RetrieveCreatureAsync(Client(), CardHandle);

        retrieved.Should().BeTrue();
        A.CallTo(() => creatures.GainExperience(A<GameClient>._, card, 2_065_500, true))
            .MustHaveHappenedOnceExactly(); // 10 x 137 700 x 1.5
    }

    [Test]
    public async Task RetrieveCreature_RefusesACardThatIsNotFarmed()
    {
        var (service, store, creatures, _) = Harness();
        var card = Card();
        card.Flag = ItemFlag.None;
        A.CallTo(() => store.LoadAsync("Farmer"))
            .Returns(Task.FromResult<System.Collections.Generic.IReadOnlyList<FarmedSummon>>(
                new[] { Row(CardHandle, 60, false, _now.AddHours(-10), 86400) }));
        A.CallTo(() => creatures.FindCard(A<ConnectionInfo>._, CardHandle)).Returns(card);

        var retrieved = await service.RetrieveCreatureAsync(Client(), CardHandle);

        retrieved.Should().BeFalse();
        A.CallTo(store).Where(call => call.Method.Name == nameof(ICreatureFarmStore.RemoveAsync))
            .MustNotHaveHappened();
        A.CallTo(creatures).Where(call => call.Method.Name == nameof(ICreatureService.GainExperience))
            .MustNotHaveHappened();
    }

    [Test]
    public async Task RetrieveCreature_RefusesACardNoFarmRowNames()
    {
        var (service, store, creatures, _) = Harness();
        A.CallTo(() => store.LoadAsync("Farmer"))
            .Returns(Task.FromResult<System.Collections.Generic.IReadOnlyList<FarmedSummon>>(
                new[] { Row(CardHandle + 1, 60, false, _now.AddHours(-10), 86400) }));

        var retrieved = await service.RetrieveCreatureAsync(Client(), CardHandle);

        retrieved.Should().BeFalse();
        A.CallTo(creatures).MustNotHaveHappened();
        A.CallTo(store).Where(call => call.Method.Name == nameof(ICreatureFarmStore.RemoveAsync))
            .MustNotHaveHappened();
    }

    [Test]
    public async Task RetrieveCreature_RefusesAnEvolvedForm()
    {
        // RegainSummon has no evolution branch in 7.3: GetTransformLevel() yields 1 or 2, and anything else
        // returns false without touching the row (0x1400d605b).
        var (service, store, creatures, _) = Harness(form: 3);
        A.CallTo(() => store.LoadAsync("Farmer"))
            .Returns(Task.FromResult<System.Collections.Generic.IReadOnlyList<FarmedSummon>>(
                new[] { Row(CardHandle, 60, false, _now.AddHours(-10), 86400) }));
        A.CallTo(() => creatures.FindCard(A<ConnectionInfo>._, CardHandle)).Returns(Card());

        var retrieved = await service.RetrieveCreatureAsync(Client(), CardHandle);

        retrieved.Should().BeFalse();
        A.CallTo(store).Where(call => call.Method.Name == nameof(ICreatureFarmStore.RemoveAsync))
            .MustNotHaveHappened();
    }

    // --- §5.8: an entry whose ticket expired is taken back before the 6001 -------------------------------

    [Test]
    public async Task FarmInfo_RetrievesAnExpiredEntryAndLeavesItOutOfTheAnswer()
    {
        var (service, store, creatures, _) = Harness(needExp: 5_000_000);
        var expired = Row(CardHandle, 60, false, _now.AddHours(-48), 3600);
        var live = Row(CardHandle + 1, 60, false, _now.AddHours(-1), 86400);
        A.CallTo(() => store.LoadAsync("Farmer"))
            .Returns(Task.FromResult<System.Collections.Generic.IReadOnlyList<FarmedSummon>>(new[] { expired, live }));
        A.CallTo(() => creatures.FindCard(A<ConnectionInfo>._, CardHandle)).Returns(Card());
        A.CallTo(() => store.RemoveAsync("Farmer", CardHandle)).Returns(Task.FromResult(true));
        var connection = new StorageTestHarness.FrameConnection(Array.Empty<byte>());
        var client = StorageTestHarness.NewGameClient(connection, creatureService: creatures,
            creatureFarmService: service);
        StorageTestHarness.Session(client).CharacterName = "Farmer";

        var sent = await service.SendFarmInfoAsync(client);

        sent.Should().BeTrue();
        A.CallTo(() => store.RemoveAsync("Farmer", CardHandle)).MustHaveHappenedOnceExactly();
        connection.Sent.Should().HaveCount(2);

        // The notice the reference prints (PrintfChatMessage with @1158 on CHAT_NOTICE), then the window.
        var notice = connection.Sent[0];
        BinaryPrimitives.ReadUInt16LittleEndian(notice.AsSpan(4, 2)).Should().Be((ushort)GamePackets.TM_SC_CHAT);
        Encoding.ASCII.GetString(notice, 7, 7).Should().Be("@NOTICE");
        notice[30].Should().Be((byte)ChatType.Notice);
        Encoding.ASCII.GetString(notice, 31, notice.Length - 31 - 1).Should().Be("@1158");

        var farm = connection.Sent[1];
        BinaryPrimitives.ReadUInt16LittleEndian(farm.AsSpan(4, 2)).Should().Be(6001);
        farm.Length.Should().Be(8 + 120, "only the entry whose ticket is still running is listed");
        farm[7].Should().Be(1, "summons");
    }

    [Test]
    public async Task FarmInfo_KeepsTheEntriesWhoseTicketIsStillRunning()
    {
        var (service, store, _, _) = Harness();
        A.CallTo(() => store.LoadAsync("Farmer"))
            .Returns(Task.FromResult<System.Collections.Generic.IReadOnlyList<FarmedSummon>>(
                new[] { Row(CardHandle, 60, false, _now.AddHours(-1), 86400) }));
        var connection = new StorageTestHarness.FrameConnection(Array.Empty<byte>());
        var client = StorageTestHarness.NewGameClient(connection);
        StorageTestHarness.Session(client).CharacterName = "Farmer";

        await service.SendFarmInfoAsync(client);

        connection.Sent.Should().ContainSingle();
        connection.Sent[0][7].Should().Be(1);
        A.CallTo(store).Where(call => call.Method.Name == nameof(ICreatureFarmStore.RemoveAsync))
            .MustNotHaveHappened();
    }

    // --- helpers -------------------------------------------------------------------------------------

    private GameClient Client()
    {
        var client = StorageTestHarness.NewGameClient(new StorageTestHarness.FrameConnection(Array.Empty<byte>()));
        StorageTestHarness.Session(client).CharacterName = "Farmer";
        return client;
    }

    private static CreatureCard Card() => new()
    {
        ItemId = CardHandle,
        Code = 540014,
        Flag = CreatureFarmRules.WithFarmedSummon(ItemFlag.None),
        SummonCode = SummonCode,
        SummonId = 77,
        Level = 1,
        Exp = 0,
        MaxReachedLevel = 1
    };

    /// <summary>One row per test: the store, the creature service and the catalogue are all fakes.</summary>
    private (ICreatureFarmService Service, ICreatureFarmStore Store, ICreatureService Creatures,
        ICreatureCatalog Catalog) Harness(int form = 1, long needExp = 1_000_000)
    {
        var store = A.Fake<ICreatureFarmStore>();
        var creatures = A.Fake<ICreatureService>();
        var catalog = A.Fake<ICreatureCatalog>();
        var summon = new SummonResourceInfo(SummonCode, "Poulet", 0, 1, form, 540014, 100, 1f, 1f, 1f, null);
        A.CallTo(() => catalog.TryGetSummon(SummonCode, out summon)).Returns(true);
        A.CallTo(() => catalog.NeedExp(A<int>._)).Returns(needExp);

        var service = new CreatureFarmService(store, () => _now, creatures, catalog);
        return (service, store, creatures, catalog);
    }
}
