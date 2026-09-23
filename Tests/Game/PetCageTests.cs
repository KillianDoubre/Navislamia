using System.Buffers.Binary;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Options;
using Navislamia.Configuration.Options;
using Navislamia.Game.Network.Packets.Enums;
using Navislamia.Game.Services;
using Navislamia.Game.Services.Pets;

namespace Tests.Game;

/// <summary>
/// Calling a pet with its cage (docs/packet-specs/socle-familier-pet.md §15): the cage → pet link of the
/// 7.3 client table, the toggle, and the frames the service sends through <see cref="PetWorldService"/>.
/// </summary>
[TestFixture]
public class PetCageTests
{
    private const long Crab = 690401;
    private const long Rabbit = 690411;
    private const uint CrabCage = 55;
    private const uint RabbitCage = 56;

    private static PetCatalog Catalog() => new(Options.Create(new PetCatalogOptions
    {
        Pets =
        {
            new PetResourceRow { Id = 1, CageId = (int)Crab, Name = "Helmet Crab" },
            new PetResourceRow { Id = 11, CageId = (int)Rabbit, Name = "Rabbit" }
        }
    }));

    private static (PetSummonService Service, Navislamia.Game.Network.Clients.GameClient Client,
        StorageTestHarness.FrameConnection Connection) NewSession()
    {
        var connection = new StorageTestHarness.FrameConnection(Array.Empty<byte>());
        var client = StorageTestHarness.NewGameClient(connection);
        var info = StorageTestHarness.Session(client);
        info.CharacterHandle = 0x80000001;
        info.X = 1000;
        info.Y = 2000;
        info.Z = 10;
        info.Layer = 0;
        return (new PetSummonService(Catalog(), new PetWorldService()), client, connection);
    }

    private static ushort Id(byte[] packet) => BinaryPrimitives.ReadUInt16LittleEndian(packet.AsSpan(4, 2));

    private static uint EnterHandle(byte[] enter) => BinaryPrimitives.ReadUInt32LittleEndian(enter.AsSpan(8, 4));

    private static float EnterX(byte[] enter) => BinaryPrimitives.ReadSingleLittleEndian(enter.AsSpan(12, 4));

    [Test]
    public void Catalog_ResolvesACageToItsPet()
    {
        Catalog().TryGetByCage(Crab, out var pet).Should().BeTrue();
        pet.PetId.Should().Be(1);
        pet.Name.Should().Be("Helmet Crab");

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
        pets.Single(pet => pet.CageId == Crab).Id.Should().Be(1);
    }

    [Test]
    public void Decide_TogglesWithTheSameCageAndSwapsWithAnother()
    {
        PetSummonRules.Decide(null, CrabCage).Should().Be(PetCageAction.Summon);

        var active = new ActivePet(0x40000001, CrabCage, new PetWorldEntry());
        PetSummonRules.Decide(active, CrabCage).Should().Be(PetCageAction.Dismiss);
        PetSummonRules.Decide(active, RabbitCage).Should().Be(PetCageAction.Swap);
    }

    [Test]
    public void BuildEntry_PlacesThePetAtItsMasterWithTheNamedDefaults()
    {
        var entry = PetSummonRules.BuildEntry(new PetDefinition(1, (int)Crab, "Helmet Crab"), CrabCage,
            1000, 2000, 10, 3, isFirstEnter: true);

        entry.CageHandle.Should().Be(CrabCage, "the cage the player used is the item the pet is stored in");
        entry.PetCode.Should().Be(1u);
        (entry.X, entry.Y, entry.Z, entry.Layer).Should().Be((1000f, 2000f, 10f, (byte)3));
        entry.Hp.Should().Be(PetSummonDefaults.MaxHp);
        entry.MaxHp.Should().Be(PetSummonDefaults.MaxHp);
        entry.Code.Should().Be(PetSummonDefaults.Code, "code is not unified with pet_code");
        entry.IsFirstEnter.Should().BeTrue();
    }

    [Test]
    public void UsingACage_CallsItsPetWithTheObjectThenTheCreatureWindow()
    {
        var (service, client, connection) = NewSession();

        service.TryUseCage(client, Crab, CrabCage).Should().BeTrue();

        connection.Sent.Select(Id).Should().Equal(
            new[] { (ushort)GamePackets.TM_SC_ENTER, (ushort)GamePackets.TM_SC_ADD_PET_INFO },
            "351 before the object crashes the 7.3 client");
        var enter = connection.Sent[0];
        var addInfo = connection.Sent[1];
        addInfo.Should().HaveCount(42);
        enter.Should().HaveCount(95);
        BinaryPrimitives.ReadUInt32LittleEndian(addInfo.AsSpan(7, 4)).Should().Be(CrabCage);
        BinaryPrimitives.ReadUInt32LittleEndian(addInfo.AsSpan(11, 4)).Should().Be(EnterHandle(enter),
            "the creature window and the object carry one handle");
        EnterX(enter).Should().Be(1000f);

        var active = StorageTestHarness.Session(client).ActivePet;
        active.Should().NotBeNull();
        active!.Handle.Should().Be(EnterHandle(enter));
        active.CageHandle.Should().Be(CrabCage);
    }

    [Test]
    public void UsingTheSameCageAgain_PutsThePetAway()
    {
        var (service, client, connection) = NewSession();
        service.TryUseCage(client, Crab, CrabCage);
        var handle = StorageTestHarness.Session(client).ActivePet!.Handle;
        connection.Sent.Clear();

        service.TryUseCage(client, Crab, CrabCage).Should().BeTrue();

        connection.Sent.Select(Id).Should().Equal(
            (ushort)GamePackets.TM_SC_UNSUMMON_PET, (ushort)GamePackets.TM_SC_LEAVE);
        BinaryPrimitives.ReadUInt32LittleEndian(connection.Sent[0].AsSpan(7, 4)).Should().Be(handle);
        StorageTestHarness.Session(client).ActivePet.Should().BeNull();
    }

    [Test]
    public void UsingAnotherCage_SwapsThePets()
    {
        var (service, client, connection) = NewSession();
        service.TryUseCage(client, Crab, CrabCage);
        connection.Sent.Clear();

        service.TryUseCage(client, Rabbit, RabbitCage).Should().BeTrue();

        connection.Sent.Select(Id).Should().Equal(
            (ushort)GamePackets.TM_SC_UNSUMMON_PET, (ushort)GamePackets.TM_SC_LEAVE,
            (ushort)GamePackets.TM_SC_ENTER, (ushort)GamePackets.TM_SC_ADD_PET_INFO);
        StorageTestHarness.Session(client).ActivePet!.CageHandle.Should().Be(RabbitCage);
    }

    [Test]
    public void UsingAnItemThatIsNoCage_DoesNothing()
    {
        var (service, client, connection) = NewSession();

        service.TryUseCage(client, 603002, 70).Should().BeFalse();

        connection.Sent.Should().BeEmpty();
        StorageTestHarness.Session(client).ActivePet.Should().BeNull();
    }

    [Test]
    public void AWarp_BringsThePetToTheNewPlace()
    {
        var (service, client, connection) = NewSession();
        service.TryUseCage(client, Crab, CrabCage);
        var info = StorageTestHarness.Session(client);
        var before = info.ActivePet!.Handle;
        connection.Sent.Clear();
        info.X = 5000;

        service.FollowWarp(client);

        connection.Sent.Select(Id).Should().Equal(
            (ushort)GamePackets.TM_SC_UNSUMMON_PET, (ushort)GamePackets.TM_SC_LEAVE,
            (ushort)GamePackets.TM_SC_ENTER, (ushort)GamePackets.TM_SC_ADD_PET_INFO);
        BinaryPrimitives.ReadUInt32LittleEndian(connection.Sent[0].AsSpan(7, 4)).Should().Be(before);
        EnterX(connection.Sent[2]).Should().Be(5000f);
        info.ActivePet!.CageHandle.Should().Be(CrabCage);
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
    public void ReturningToTheLobby_ForgetsThePet()
    {
        var (service, client, _) = NewSession();
        service.TryUseCage(client, Crab, CrabCage);

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
