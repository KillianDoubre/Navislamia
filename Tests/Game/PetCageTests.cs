using System.Buffers.Binary;
using System.Text;
using System.Text.Json;
using FakeItEasy;
using FluentAssertions;
using Microsoft.Extensions.Options;
using Navislamia.Configuration.Options;
using Navislamia.Game.DataAccess.Entities.Telecaster;
using Navislamia.Game.DataAccess.Repositories.Interfaces;
using Navislamia.Game.Network.Clients;
using Navislamia.Game.Network.Packets;
using Navislamia.Game.Network.Packets.Enums;
using Navislamia.Game.Services;
using Navislamia.Game.Services.Pets;

namespace Tests.Game;

/// <summary>
/// Calling a pet with its cage and naming it (docs/packet-specs/socle-familier-pet.md §15-17): the cage → pet
/// link of the 7.3 client table, the toggle, the frames sent through <see cref="PetWorldService"/>, and the
/// 353/354 name exchange.
/// </summary>
[TestFixture]
public class PetCageTests
{
    private const long Crab = 690401;
    private const long Rabbit = 690411;
    private const uint CrabCage = 55;
    private const uint RabbitCage = 56;

    private ICharacterService _characters = null!;
    private IBannedWordsRepository _bannedWords = null!;

    [SetUp]
    public void SetUp()
    {
        _characters = A.Fake<ICharacterService>();
        _bannedWords = A.Fake<IBannedWordsRepository>();
        Named(true);
        A.CallTo(() => _characters.RenamePetAsync(A<string>._, A<long>._, A<string>._)).Returns(true);
    }

    private void Named(bool wasNameChanged, string name = "Helmet Crab") =>
        A.CallTo(() => _characters.GetOrCreatePetAsync(A<string>._, A<long>._, A<int>._, A<long>._, A<int>._,
            A<string>._)).Returns(new PetRecord(name, wasNameChanged));

    private static PetCatalog Catalog() => new(Options.Create(new PetCatalogOptions
    {
        Pets =
        {
            new PetResourceRow { Id = 1, CageId = (int)Crab, Name = "Helmet Crab", CollectRadius = 5 },
            new PetResourceRow { Id = 11, CageId = (int)Rabbit, Name = "Rabbit" }
        }
    }));

    private (PetSummonService Service, GameClient Client, StorageTestHarness.FrameConnection Connection) NewSession()
    {
        var connection = new StorageTestHarness.FrameConnection(Array.Empty<byte>());
        var client = StorageTestHarness.NewGameClient(connection);
        var info = StorageTestHarness.Session(client);
        info.CharacterHandle = 0x80000001;
        info.CharacterName = "Tester";
        info.X = info.DestinationX = 1000;
        info.Y = info.DestinationY = 2000;
        info.Z = 10;
        info.Layer = 0;
        return (new PetSummonService(Catalog(), new PetWorldService(), _characters, _bannedWords), client,
            connection);
    }

    private static ushort Id(byte[] packet) => BinaryPrimitives.ReadUInt16LittleEndian(packet.AsSpan(4, 2));

    private static uint EnterHandle(byte[] enter) => BinaryPrimitives.ReadUInt32LittleEndian(enter.AsSpan(8, 4));

    private static float EnterX(byte[] enter) => BinaryPrimitives.ReadSingleLittleEndian(enter.AsSpan(12, 4));

    private static string EnterName(byte[] enter) => Encoding.ASCII.GetString(enter, 76, 19).TrimEnd('\0');

    [Test]
    public void Catalog_ResolvesACageToItsPetAndItsCollectRange()
    {
        Catalog().TryGetByCage(Crab, out var pet).Should().BeTrue();
        pet.PetId.Should().Be(1);
        pet.Name.Should().Be("Helmet Crab");
        pet.CollectRange.Should().Be(60f, "5 m of the Collect Items skill, × 12 units per meter");

        Catalog().TryGetByCage(Rabbit, out var rabbit).Should().BeTrue();
        rabbit.CollectRange.Should().Be(0f, "a pet without the skill collects nothing");

        Catalog().TryGetByCage(690574, out _).Should().BeFalse("a cage the client table does not list calls nothing");
    }

    [Test]
    public void ShippedCatalog_IsTheClientTableWithOneCagePerPet()
    {
        var path = Path.Combine(RepositoryRoot(), "DevConsole", "pet-catalog.73.json");
        using var document = JsonDocument.Parse(File.ReadAllText(path));
        var pets = document.RootElement.GetProperty("PetCatalog").GetProperty("Pets")
            .Deserialize<List<PetResourceRow>>()!;

        pets.Should().HaveCount(106, "db_pet.rdb of the 7.3 client lists 106 pets");
        pets.Select(pet => pet.CageId).Should().OnlyHaveUniqueItems(
            "the 7.3 table links each cage to one pet, unlike the 9.4 export");
        pets.Single(pet => pet.CageId == Crab).CollectRadius.Should().Be(5, "the crab cage's tooltip says 5 meters");
        pets.Single(pet => pet.CageId == 690407).CollectRadius.Should().Be(15);
    }

    [Test]
    public void Decide_TogglesWithTheSameCageAndSwapsWithAnother()
    {
        PetSummonRules.Decide(null, CrabCage).Should().Be(PetCageAction.Summon);

        var active = new ActivePet(0x40000001, CrabCage, new PetWorldEntry());
        PetSummonRules.Decide(active, CrabCage).Should().Be(PetCageAction.Dismiss);
        PetSummonRules.Decide(active, RabbitCage).Should().Be(PetCageAction.Swap);
    }

    [TestCase("Crabby", true)]
    [TestCase("Rex2", true)]
    [TestCase("abc", false)]
    [TestCase("TwentyCharactersLong", false)]
    [TestCase("Two Words", false)]
    [TestCase("Crab!", false)]
    public void IsValidName_FollowsTheCharacterNameRule(string name, bool expected) =>
        PetSummonRules.IsValidName(name).Should().Be(expected);

    private static uint InfoHandle(byte[] addInfo) => BinaryPrimitives.ReadUInt32LittleEndian(addInfo.AsSpan(11, 4));

    [Test]
    public async Task UsingACageNotYetRegistered_AnnouncesThePetThenPutsItInTheWorldUnderTheSameHandle()
    {
        var (service, client, connection) = NewSession();

        (await service.TryUseCageAsync(client, Crab, CrabCage)).Should().BeTrue();

        connection.Sent.Select(Id).Should().Equal(
            new[] { (ushort)GamePackets.TM_SC_ADD_PET_INFO, (ushort)GamePackets.TM_SC_ENTER },
            "StructPlayer::AddPet registers the pet (351), SummonPet only adds it to the world");
        var addInfo = connection.Sent[0];
        var enter = connection.Sent[1];
        addInfo.Should().HaveCount(42);
        enter.Should().HaveCount(95);
        BinaryPrimitives.ReadUInt32LittleEndian(addInfo.AsSpan(7, 4)).Should().Be(CrabCage);
        InfoHandle(addInfo).Should().Be(EnterHandle(enter), "the client links the ENTER to the pet it registered");
        BinaryPrimitives.ReadInt32LittleEndian(addInfo.AsSpan(34, 4)).Should().Be(1, "code = GetPetCode()");
        EnterX(enter).Should().Be(1000f);

        var active = StorageTestHarness.Session(client).ActivePet!;
        active.Handle.Should().Be(EnterHandle(enter));
        active.CollectRange.Should().Be(60f);
    }

    [Test]
    public async Task ThePetsOfTheBag_AreAnnouncedAtLoginAndCalledWithoutASecond351()
    {
        var (service, client, connection) = NewSession();
        var items = new[]
        {
            new ItemEntity { Id = CrabCage, ItemResourceId = Crab, Amount = 1 },
            new ItemEntity { Id = 70, ItemResourceId = 603002, Amount = 1 }
        };

        await service.SendPetInfoAsync(client, items);

        connection.Sent.Select(Id).Should().Equal(new[] { (ushort)GamePackets.TM_SC_ADD_PET_INFO },
            "one 351 per cage, nothing for another item");
        var registered = InfoHandle(connection.Sent[0]);
        connection.Sent.Clear();

        await service.TryUseCageAsync(client, Crab, CrabCage);

        connection.Sent.Select(Id).Should().Equal((ushort)GamePackets.TM_SC_ENTER);
        EnterHandle(connection.Sent[0]).Should().Be(registered);
    }

    [Test]
    public async Task AnUnnamedPet_OpensTheNameBoxInsteadOfComingOut()
    {
        Named(false);
        var (service, client, connection) = NewSession();

        await service.TryUseCageAsync(client, Crab, CrabCage);

        connection.Sent.Select(Id).Should().Equal(
            (ushort)GamePackets.TM_SC_ADD_PET_INFO, (ushort)GamePackets.TM_SC_SHOW_SET_PET_NAME);
        var show = connection.Sent[1];
        show.Should().HaveCount(11);
        BinaryPrimitives.ReadUInt32LittleEndian(show.AsSpan(7, 4)).Should().Be(InfoHandle(connection.Sent[0]),
            "354 echoes the handle 353 carried, so it must be the pet's");
        StorageTestHarness.Session(client).ActivePet.Should().BeNull("ITEM_EFFECT_INSTANT SUMMON_PET names, it does not call");
        A.CallTo(() => _characters.GetOrCreatePetAsync("Tester", 0x80000001, A<int>._, CrabCage, 1, "Helmet Crab"))
            .MustHaveHappenedOnceExactly();
    }

    [Test]
    public async Task UsingTheSameCageAgain_PutsThePetAway()
    {
        var (service, client, connection) = NewSession();
        await service.TryUseCageAsync(client, Crab, CrabCage);
        var handle = StorageTestHarness.Session(client).ActivePet!.Handle;
        connection.Sent.Clear();

        (await service.TryUseCageAsync(client, Crab, CrabCage)).Should().BeTrue();

        connection.Sent.Select(Id).Should().Equal(
            (ushort)GamePackets.TM_SC_UNSUMMON_PET, (ushort)GamePackets.TM_SC_LEAVE);
        BinaryPrimitives.ReadUInt32LittleEndian(connection.Sent[0].AsSpan(7, 4)).Should().Be(handle);
        StorageTestHarness.Session(client).ActivePet.Should().BeNull();
    }

    [Test]
    public async Task UsingAnotherCage_SwapsThePets()
    {
        var (service, client, connection) = NewSession();
        await service.TryUseCageAsync(client, Crab, CrabCage);
        connection.Sent.Clear();

        (await service.TryUseCageAsync(client, Rabbit, RabbitCage)).Should().BeTrue();

        connection.Sent.Select(Id).Should().Equal(
            (ushort)GamePackets.TM_SC_ADD_PET_INFO, (ushort)GamePackets.TM_SC_UNSUMMON_PET,
            (ushort)GamePackets.TM_SC_LEAVE, (ushort)GamePackets.TM_SC_ENTER);
        StorageTestHarness.Session(client).ActivePet!.CageHandle.Should().Be(RabbitCage);
    }

    [Test]
    public async Task UsingAnItemThatIsNoCage_DoesNothing()
    {
        var (service, client, connection) = NewSession();

        (await service.TryUseCageAsync(client, 603002, 70)).Should().BeFalse();

        connection.Sent.Should().BeEmpty();
        StorageTestHarness.Session(client).ActivePet.Should().BeNull();
    }

    /// <summary>Uses the cage of an unnamed pet, which offers its name; returns the pet's handle.</summary>
    private async Task<uint> OfferedName(PetSummonService service, GameClient client,
        StorageTestHarness.FrameConnection connection)
    {
        Named(false);
        await service.TryUseCageAsync(client, Crab, CrabCage);
        var handle = InfoHandle(connection.Sent[0]);
        connection.Sent.Clear();
        return handle;
    }

    [Test]
    public async Task AValidName_IsStoredAndAnnouncedByAChangeOfName()
    {
        var (service, client, connection) = NewSession();
        var handle = await OfferedName(service, client, connection);

        await service.RenameAsync(client, handle, "Crabby");

        A.CallTo(() => _characters.RenamePetAsync("Tester", CrabCage, "Crabby")).MustHaveHappenedOnceExactly();
        connection.Sent.Select(Id).Should().Equal(
            (ushort)GamePackets.TM_SC_CHANGE_NAME, (ushort)GamePackets.TM_SC_RESULT);
        var change = connection.Sent[0];
        change.Should().HaveCount(30, "TS_SC_CHANGE_NAME: handle @7, name[19] @11");
        BinaryPrimitives.ReadUInt32LittleEndian(change.AsSpan(7, 4)).Should().Be(handle);
        Encoding.ASCII.GetString(change, 11, 19).TrimEnd('\0').Should().Be("Crabby");
        ResultOf(connection.Sent[1]).Should().Be(((ushort)GamePackets.TM_CS_SET_PET_NAME, (ushort)ResultCode.Success));

        connection.Sent.Clear();
        await service.RenameAsync(client, handle, "Again");
        connection.Sent.Should().BeEmpty("the offer is spent");
    }

    [Test]
    public async Task ATooShortName_AnswersTheOfficialLineAndStoresNothing()
    {
        var (service, client, connection) = NewSession();
        var handle = await OfferedName(service, client, connection);

        await service.RenameAsync(client, handle, "No");

        A.CallTo(() => _characters.RenamePetAsync(A<string>._, A<long>._, A<string>._)).MustNotHaveHappened();
        connection.Sent.Select(Id).Should().Equal((ushort)GamePackets.TM_SC_CHAT, (ushort)GamePackets.TM_SC_RESULT);
        ResultOf(connection.Sent[1]).Should().Be(((ushort)GamePackets.TM_CS_SET_PET_NAME, (ushort)ResultCode.LimitMin));
    }

    [Test]
    public async Task AnInvalidOrBannedName_IsRefusedAsAccessDenied()
    {
        A.CallTo(() => _bannedWords.ContainsBannedWord("Rudeword")).Returns(true);
        var (service, client, connection) = NewSession();
        var handle = await OfferedName(service, client, connection);

        await service.RenameAsync(client, handle, "Crab!");
        await service.RenameAsync(client, handle, "Rudeword");

        A.CallTo(() => _characters.RenamePetAsync(A<string>._, A<long>._, A<string>._)).MustNotHaveHappened();
        connection.Sent.Select(Id).Should().Equal(
            (ushort)GamePackets.TM_SC_CHAT, (ushort)GamePackets.TM_SC_RESULT,
            (ushort)GamePackets.TM_SC_CHAT, (ushort)GamePackets.TM_SC_RESULT);
        ResultOf(connection.Sent[3]).Should().Be(((ushort)GamePackets.TM_CS_SET_PET_NAME, (ushort)ResultCode.AccessDenied));
    }

    private static (ushort Request, ushort Result) ResultOf(byte[] result) => (
        BinaryPrimitives.ReadUInt16LittleEndian(result.AsSpan(7, 2)),
        BinaryPrimitives.ReadUInt16LittleEndian(result.AsSpan(9, 2)));

    [Test]
    public async Task ANameNobodyOffered_IsIgnored()
    {
        var (service, client, connection) = NewSession();
        await service.TryUseCageAsync(client, Crab, CrabCage);
        var handle = StorageTestHarness.Session(client).ActivePet!.Handle;
        connection.Sent.Clear();

        await service.RenameAsync(client, handle, "Crabby");
        await service.RenameAsync(client, handle + 1, "Crabby");

        A.CallTo(() => _characters.RenamePetAsync(A<string>._, A<long>._, A<string>._)).MustNotHaveHappened();
        connection.Sent.Should().BeEmpty();
    }

    [Test]
    public async Task OfferRename_OpensTheBoxOnlyWithAPetOut()
    {
        var (service, client, connection) = NewSession();

        service.HasPetOut(client).Should().BeFalse();
        service.OfferRename(client);
        connection.Sent.Should().BeEmpty();

        await service.TryUseCageAsync(client, Crab, CrabCage);
        connection.Sent.Clear();
        service.OfferRename(client);

        connection.Sent.Select(Id).Should().Equal((ushort)GamePackets.TM_SC_SHOW_SET_PET_NAME);
    }

    [Test]
    public void SetPickupFilter_KeepsTheRawValue()
    {
        var (service, client, _) = NewSession();

        service.SetPickupFilter(client, 0x40000001, 0x2A);

        StorageTestHarness.Session(client).PetPickupFilter.Should().Be(0x2Au);
    }

    [Test]
    public async Task AWarp_BringsThePetToTheNewPlace()
    {
        var (service, client, connection) = NewSession();
        await service.TryUseCageAsync(client, Crab, CrabCage);
        var info = StorageTestHarness.Session(client);
        var before = info.ActivePet!.Handle;
        connection.Sent.Clear();
        info.X = 5000;

        service.FollowWarp(client);

        connection.Sent.Select(Id).Should().Equal(
            (ushort)GamePackets.TM_SC_UNSUMMON_PET, (ushort)GamePackets.TM_SC_LEAVE, (ushort)GamePackets.TM_SC_ENTER);
        BinaryPrimitives.ReadUInt32LittleEndian(connection.Sent[0].AsSpan(7, 4)).Should().Be(before);
        EnterHandle(connection.Sent[2]).Should().Be(before, "the registered pet keeps its handle");
        EnterX(connection.Sent[2]).Should().Be(5000f);
        info.ActivePet!.CollectRange.Should().Be(60f, "the pet keeps collecting after a warp");
        info.ActivePet.Entry.IsFirstEnter.Should().BeFalse("the pet re-enters, it is not called again");
    }

    [Test]
    public void AWarpWithoutAPet_SendsNothing()
    {
        var (service, client, connection) = NewSession();

        service.FollowWarp(client);

        connection.Sent.Should().BeEmpty();
    }

    [Test]
    public async Task ReturningToTheLobby_ForgetsThePet()
    {
        var (service, client, _) = NewSession();
        await service.TryUseCageAsync(client, Crab, CrabCage);

        StorageTestHarness.Session(client).ClearCharacterSession();

        StorageTestHarness.Session(client).ActivePet.Should().BeNull();
    }

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null && !File.Exists(Path.Combine(directory.FullName, "Navislamia.sln")))
        {
            directory = directory.Parent;
        }

        return directory!.FullName;
    }
}
