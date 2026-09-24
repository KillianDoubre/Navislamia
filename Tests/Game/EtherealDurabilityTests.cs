using System;
using System.Buffers.Binary;
using System.IO;
using System.Threading.Tasks;
using FakeItEasy;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Navislamia.Game.DataAccess.Contexts;
using Navislamia.Game.DataAccess.Entities.Arcadia;
using Navislamia.Game.DataAccess.Entities.Enums;
using Navislamia.Game.DataAccess.Entities.Telecaster;
using Navislamia.Game.DataAccess.Repositories.Interfaces;
using Navislamia.Game.Network.Clients;
using Navislamia.Game.Network.Packets;
using Navislamia.Game.Network.Packets.Enums;
using Navislamia.Game.Network.Packets.Game;
using Navislamia.Game.Services;
using NUnit.Framework;

namespace Tests.Game;

/// <summary>
/// The sacrifice guard of <c>TM_CS_TRANSMIT_ETHEREAL_DURABILITY</c> (263): the object the one handle names
/// is judged — a wearable resource that carries ethereal durability, a copy that still has some — and the
/// answer names the object at the offsets of <c>TM_SC_RESULT</c>. What the gesture would then do (the
/// amount, the ceiling, the object consumed, the code each of the client's two clauses maps to) is not
/// established and is not invented here. See docs/packet-specs/263-transmit-ethereal-durability.md §5.4.
/// </summary>
[TestFixture]
public class EtherealDurabilityTests
{
    private const int HeaderSize = 7;
    private const int FrameLength = HeaderSize + 4;
    private const int ResultFrameLength = HeaderSize + 8;

    private const uint Handle = 0x80000A13u;
    private const string Character = "Killian";
    private const int ResourceId = 1234;

    private sealed record Harness(CraftingSocleService Service, ICharacterService Characters, GameClient Client,
        StorageTestHarness.FrameConnection Connection, ConnectionInfo Session);

    private static Harness Build(ItemEtherealFields[] resources = null)
    {
        var characters = A.Fake<ICharacterService>();
        var connection = new StorageTestHarness.FrameConnection(Array.Empty<byte>());
        var client = StorageTestHarness.NewGameClient(connection);
        var session = StorageTestHarness.Session(client);
        session.CharacterName = Character;
        session.CharacterHandle = 77;

        var repository = A.Fake<IItemResourceRepository>();
        A.CallTo(() => repository.GetEtherealFields()).Returns(resources ?? Array.Empty<ItemEtherealFields>());

        return new Harness(new CraftingSocleService(characters, new EtherealSacrificeCatalog(repository)),
            characters, client, connection, session);
    }

    private static ItemEntity Item(int etherealDurability, long resourceId = ResourceId)
        => new() { Id = Handle, ItemResourceId = resourceId, EtherealDurability = etherealDurability };

    private static void Resolves(Harness harness, ItemEntity? item)
        => A.CallTo(() => harness.Characters.GetItemByHandleAsync(Character, Handle)).Returns(Task.FromResult(item));

    private static byte[] TransmitFrame(uint handle, int length = FrameLength)
    {
        var frame = new byte[length];
        BinaryPrimitives.WriteUInt32LittleEndian(frame.AsSpan(0, 4), (uint)length);
        BinaryPrimitives.WriteUInt16LittleEndian(frame.AsSpan(4, 2),
            (ushort)GamePackets.TM_CS_TRANSMIT_ETHEREAL_DURABILITY);
        frame[6] = Checksum(frame);

        if (length >= FrameLength)
        {
            BinaryPrimitives.WriteUInt32LittleEndian(frame.AsSpan(HeaderSize, 4), handle);
        }

        return frame;
    }

    private static Task Send(Harness harness, uint handle = Handle, int length = FrameLength)
        => harness.Service.HandleAsync(harness.Client, (ushort)GamePackets.TM_CS_TRANSMIT_ETHEREAL_DURABILITY,
            TransmitFrame(handle, length));

    private static TS_SC_RESULT ResultOf(byte[] packet)
        => new Packet<TS_SC_RESULT>(packet).GetDataStruct<TS_SC_RESULT>();

    private static byte Checksum(byte[] packet)
    {
        byte checksum = 0;
        for (var index = 0; index < 6; index++)
        {
            checksum += packet[index];
        }

        return checksum;
    }

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Navislamia.sln")))
        {
            directory = directory.Parent;
        }

        directory.Should().NotBeNull("the repository root (Navislamia.sln) must be reachable from the test output");

        return directory!.FullName;
    }

    // ---- the two frames on the wire -------------------------------------------------------------

    [Test]
    public void Frame_IsElevenBytesAndCarriesOnlyTheHandleAtSeven()
    {
        var frame = TransmitFrame(Handle);

        frame.Length.Should().Be(FrameLength);
        BinaryPrimitives.ReadUInt32LittleEndian(frame.AsSpan(0, 4)).Should().Be((uint)FrameLength);
        BinaryPrimitives.ReadUInt16LittleEndian(frame.AsSpan(4, 2)).Should().Be(263);
        frame[6].Should().Be(Checksum(frame));

        GameActionPackets.TryReadTransmitEtherealDurability(frame, out var request).Should().BeTrue();
        request.Handle.Should().Be(Handle);
    }

    [Test]
    public async Task Answer_IsATsScResultNaming263AndTheObjectAtTheOffsetsOfTheFrame()
    {
        var harness = Build(new[] { new ItemEtherealFields(ResourceId, ItemWearType.Weapon, 300) });
        Resolves(harness, Item(etherealDurability: 0));

        await Send(harness);

        var frame = harness.Connection.Sent[0];
        frame.Length.Should().Be(ResultFrameLength, "the 7-byte header plus TS_SC_RESULT's 8 bytes");
        BinaryPrimitives.ReadUInt32LittleEndian(frame.AsSpan(0, 4)).Should().Be(ResultFrameLength);
        BinaryPrimitives.ReadUInt16LittleEndian(frame.AsSpan(4, 2)).Should().Be((ushort)GamePackets.TM_SC_RESULT);
        BinaryPrimitives.ReadUInt16LittleEndian(frame.AsSpan(7, 2)).Should().Be(263, "request_msg_id at 7");
        BinaryPrimitives.ReadUInt16LittleEndian(frame.AsSpan(9, 2))
            .Should().Be((ushort)ResultCode.NotActable, "result at 9");
        BinaryPrimitives.ReadInt32LittleEndian(frame.AsSpan(11, 4))
            .Should().Be(unchecked((int)Handle), "value at 11, the object the refusal names");
    }

    // ---- the resource the catalog reads ---------------------------------------------------------

    [Test]
    public void ItemResourceEntity_MapsTheTwoColumnsTheCatalogReads()
    {
        var options = new DbContextOptionsBuilder<ArcadiaContext>()
            .UseNpgsql("Host=localhost;Database=arcadia;Username=postgres;Password=postgres")
            .Options;

        using var context = new ArcadiaContext(options);
        var entity = context.Model.FindEntityType(typeof(ItemResourceEntity));

        entity.Should().NotBeNull();

        var wearType = entity!.FindProperty("WearType");
        var etherealDurability = entity.FindProperty("EtherealDurability");

        wearType.Should().NotBeNull("the nature clause reads wear_type");
        etherealDurability.Should().NotBeNull("both clauses read ethereal_durability");

        var wearColumn = wearType!;
        var etherealColumn = etherealDurability!;

        wearColumn.GetColumnName().Should().Be("WearType");
        etherealColumn.GetColumnName().Should().Be("EtherealDurability");
        wearColumn.GetColumnType().Should().Be("integer");
        etherealColumn.GetColumnType().Should().Be("integer");
        context.ItemResources.Should().NotBeNull();
    }

    [Test]
    public void Catalog_KeepsTheWearTypeAndTheCapacityOfAResource()
    {
        var catalog = Catalog(new ItemEtherealFields(ResourceId, ItemWearType.Weapon, 300),
            new ItemEtherealFields(ResourceId + 1, ItemWearType.CantWear, 0));

        catalog.TryGet(ResourceId, out var weapon).Should().BeTrue();
        weapon.WearType.Should().Be(ItemWearType.Weapon);
        weapon.EtherealDurability.Should().Be(300);

        catalog.TryGet(ResourceId + 1, out var material).Should().BeTrue();
        material.WearType.Should().Be(ItemWearType.CantWear);
        material.EtherealDurability.Should().Be(0);
    }

    [TestCase(0, TestName = "Catalog_LeavesAnUnusableResourceIdUnjudged_Zero")]
    [TestCase(-1, TestName = "Catalog_LeavesAnUnusableResourceIdUnjudged_Negative")]
    [TestCase(ResourceId + 99, TestName = "Catalog_LeavesAnUnusableResourceIdUnjudged_Unknown")]
    public void Catalog_LeavesAnUnusableResourceIdUnjudged(long resourceId)
    {
        Catalog(new ItemEtherealFields(ResourceId, ItemWearType.Weapon, 300)).TryGet(resourceId, out _)
            .Should().BeFalse();
    }

    private static EtherealSacrificeCatalog Catalog(params ItemEtherealFields[] fields)
    {
        var repository = A.Fake<IItemResourceRepository>();
        A.CallTo(() => repository.GetEtherealFields()).Returns(fields);
        return new EtherealSacrificeCatalog(repository);
    }

    // ---- the two clauses ------------------------------------------------------------------------

    [Test]
    public void Judge_AcceptsAWearableResourceWithCapacityAndACopyThatStillCarriesSome()
    {
        var resource = new ItemEtherealFields(ResourceId, ItemWearType.Armor, 120);

        EtherealDurabilityRules.Judge(resource, 1).Should().Be(EtherealSacrificeGate.Accepted);
        EtherealDurabilityRules.Judge(resource, 120).Should().Be(EtherealSacrificeGate.Accepted);
    }

    [TestCase(ItemWearType.CantWear, TestName = "Judge_RefusesANonWearableResource_CantWear")]
    [TestCase(ItemWearType.None, TestName = "Judge_RefusesANonWearableResource_None")]
    public void Judge_RefusesANonWearableResource(ItemWearType wearType)
    {
        var resource = new ItemEtherealFields(ResourceId, wearType, 300);

        EtherealDurabilityRules.Judge(resource, 300).Should().Be(EtherealSacrificeGate.CannotBeSacrificed);
    }

    [Test]
    public void Judge_RefusesAWearableResourceThatCarriesNoEtherealDurabilityAtAll()
    {
        var resource = new ItemEtherealFields(ResourceId, ItemWearType.Weapon, 0);

        EtherealDurabilityRules.Judge(resource, 300).Should().Be(EtherealSacrificeGate.CannotBeSacrificed);
    }

    [TestCase(0, TestName = "Judge_RefusesACopyWhoseDurabilityIsSpent_Zero")]
    [TestCase(-3, TestName = "Judge_RefusesACopyWhoseDurabilityIsSpent_Negative")]
    public void Judge_RefusesACopyWhoseDurabilityIsSpent(int etherealDurability)
    {
        var resource = new ItemEtherealFields(ResourceId, ItemWearType.Weapon, 300);

        EtherealDurabilityRules.Judge(resource, etherealDurability).Should()
            .Be(EtherealSacrificeGate.NoDurabilityToSacrifice);
    }

    [Test]
    public void Judge_ReportsTheNatureClauseFirstWhenBothClausesFail()
    {
        var resource = new ItemEtherealFields(ResourceId, ItemWearType.CantWear, 0);

        EtherealDurabilityRules.Judge(resource, 0).Should().Be(EtherealSacrificeGate.CannotBeSacrificed);
    }

    [Test]
    public void Judge_LeavesAnUnknownResourceUngatedAndStillJudgesTheCopy()
    {
        EtherealDurabilityRules.Judge(null, 5).Should().Be(EtherealSacrificeGate.Accepted);
        EtherealDurabilityRules.Judge(null, 0).Should().Be(EtherealSacrificeGate.NoDurabilityToSacrifice);
    }

    [Test]
    public void Judge_DoesNotInventACeilingOnTheCopy()
    {
        var resource = new ItemEtherealFields(ResourceId, ItemWearType.Weapon, 300);

        EtherealDurabilityRules.Judge(resource, 301).Should().Be(EtherealSacrificeGate.Accepted,
            "the copy's ceiling against its resource is not established");
    }

    [Test]
    public void RefusalCode_AnswersNotActableForBothClausesAndSuccessForTheAcceptedGate()
    {
        EtherealDurabilityRules.RefusalCode(EtherealSacrificeGate.Accepted).Should().Be(ResultCode.Success);
        EtherealDurabilityRules.RefusalCode(EtherealSacrificeGate.CannotBeSacrificed).Should()
            .Be(ResultCode.NotActable);
        EtherealDurabilityRules.RefusalCode(EtherealSacrificeGate.NoDurabilityToSacrifice).Should()
            .Be(ResultCode.NotActable);
    }

    [Test]
    public void IsEquipment_TreatsTheTwoNonWearableNamesAsOneValue()
    {
        ((int)ItemWearType.None).Should().Be((int)ItemWearType.CantWear);
        EtherealDurabilityRules.IsEquipment(ItemWearType.CantWear).Should().BeFalse();
        EtherealDurabilityRules.IsEquipment(ItemWearType.None).Should().BeFalse();
        EtherealDurabilityRules.IsEquipment(ItemWearType.Weapon).Should().BeTrue();
        EtherealDurabilityRules.IsEquipment(ItemWearType.Ring).Should().BeTrue();
    }

    [Test]
    public void Describe_NamesTheClauseTheClientItselfNames()
    {
        EtherealDurabilityRules.Describe(EtherealSacrificeGate.CannotBeSacrificed).Should()
            .Be("cannot be sacrificed");
        EtherealDurabilityRules.Describe(EtherealSacrificeGate.NoDurabilityToSacrifice).Should()
            .Be("has no durability to sacrifice");
        EtherealDurabilityRules.Describe(EtherealSacrificeGate.Accepted).Should().Be("can be sacrificed");
    }

    // ---- the conduct of the socle ---------------------------------------------------------------

    [Test]
    public async Task Handle_RefusesASacrificeableObjectWithTheSoclesGenericRefusal()
    {
        var harness = Build(new[] { new ItemEtherealFields(ResourceId, ItemWearType.Weapon, 300) });
        Resolves(harness, Item(etherealDurability: 300));

        await Send(harness);

        harness.Connection.Sent.Should().ContainSingle();
        var result = ResultOf(harness.Connection.Sent[0]);
        result.RequestMsgID.Should().Be(263);
        result.Result.Should().Be((ushort)ResultCode.InvalidArgument);
        result.Value.Should().Be(0, "the object was not refused");
    }

    [Test]
    public async Task Handle_ReadsTheObjectOnceAndOnlyUnderTheSessionCharacter()
    {
        var harness = Build(new[] { new ItemEtherealFields(ResourceId, ItemWearType.Weapon, 300) });
        Resolves(harness, Item(etherealDurability: 300));

        await Send(harness);

        A.CallTo(() => harness.Characters.GetItemByHandleAsync(Character, Handle)).MustHaveHappenedOnceExactly();
    }

    [TestCase(ItemWearType.CantWear, TestName = "Handle_RefusesANonWearableObjectWithNotActable_CantWear")]
    [TestCase(ItemWearType.None, TestName = "Handle_RefusesANonWearableObjectWithNotActable_None")]
    public async Task Handle_RefusesANonWearableObjectWithNotActable(ItemWearType wearType)
    {
        var harness = Build(new[] { new ItemEtherealFields(ResourceId, wearType, 300) });
        Resolves(harness, Item(etherealDurability: 300));

        await Send(harness);

        var result = ResultOf(harness.Connection.Sent[0]);
        result.RequestMsgID.Should().Be(263);
        result.Result.Should().Be((ushort)ResultCode.NotActable);
        result.Value.Should().Be(unchecked((int)Handle), "the refusal names the object the player offered");
    }

    [Test]
    public async Task Handle_DoesNotRefuseAWearableNonEquipmentPositionForItsNature()
    {
        // A skill card (wear_type 100) declares a wear position without being an equipment. The nature
        // clause only turns on the resource's own non-wearable declaration, so the frame keeps the generic
        // refusal: the 7.3 client's exact boundary between "equipment" and those positions is not
        // established (docs/packet-specs/263-transmit-ethereal-durability.md §7).
        var harness = Build(new[] { new ItemEtherealFields(ResourceId, ItemWearType.Skill, 300) });
        Resolves(harness, Item(etherealDurability: 300));

        await Send(harness);

        ResultOf(harness.Connection.Sent[0]).Result.Should().Be((ushort)ResultCode.InvalidArgument);
    }

    [Test]
    public async Task Handle_RefusesAWearableResourceThatCarriesNoEtherealDurability()
    {
        var harness = Build(new[] { new ItemEtherealFields(ResourceId, ItemWearType.Armor, 0) });
        Resolves(harness, Item(etherealDurability: 40));

        await Send(harness);

        ResultOf(harness.Connection.Sent[0]).Result.Should().Be((ushort)ResultCode.NotActable);
    }

    [Test]
    public async Task Handle_RefusesACopyWhoseEtherealDurabilityIsSpent()
    {
        var harness = Build(new[] { new ItemEtherealFields(ResourceId, ItemWearType.Armor, 120) });
        Resolves(harness, Item(etherealDurability: 0));

        await Send(harness);

        var result = ResultOf(harness.Connection.Sent[0]);
        result.Result.Should().Be((ushort)ResultCode.NotActable);
        result.Value.Should().Be(unchecked((int)Handle));
    }

    [Test]
    public async Task Handle_LeavesAnUnknownResourceUngated()
    {
        var harness = Build(new[] { new ItemEtherealFields(ResourceId, ItemWearType.Weapon, 300) });
        Resolves(harness, Item(etherealDurability: 7, resourceId: ResourceId + 5));

        await Send(harness);

        ResultOf(harness.Connection.Sent[0]).Result.Should().Be((ushort)ResultCode.InvalidArgument,
            "an object the catalog cannot judge is not refused for its nature");
    }

    [Test]
    public async Task Handle_RefusesAnUnknownHandleWithNotExist()
    {
        var harness = Build();
        Resolves(harness, null);

        await Send(harness);

        var result = ResultOf(harness.Connection.Sent[0]);
        result.Result.Should().Be((ushort)ResultCode.NotExist);
        result.Value.Should().Be(unchecked((int)Handle));
    }

    [Test]
    public async Task Handle_AnswersDBErrorWhenTheObjectCannotBeRead()
    {
        var harness = Build();
        A.CallTo(() => harness.Characters.GetItemByHandleAsync(A<string>._, A<uint>._))
            .Throws(new InvalidOperationException("the item table is unreachable"));

        await Send(harness);

        ResultOf(harness.Connection.Sent[0]).Result.Should().Be((ushort)ResultCode.DBError);
    }

    [Test]
    public async Task Handle_KeepsTheGenericRefusalForAFrameNamingNoObject()
    {
        var harness = Build();

        await Send(harness, handle: 0);

        ResultOf(harness.Connection.Sent[0]).Result.Should().Be((ushort)ResultCode.InvalidArgument);
        A.CallTo(() => harness.Characters.GetItemByHandleAsync(A<string>._, A<uint>._)).MustNotHaveHappened();
    }

    [Test]
    public async Task Handle_RefusesAMalformedFrameWithoutJudgingAnyObject()
    {
        var harness = Build();

        await Send(harness, length: FrameLength - 1);

        ResultOf(harness.Connection.Sent[0]).Result.Should().Be((ushort)ResultCode.InvalidArgument);
        A.CallTo(() => harness.Characters.GetItemByHandleAsync(A<string>._, A<uint>._)).MustNotHaveHappened();
    }

    [Test]
    public async Task Handle_AnswersNothingBeforeTheCharacterIsInTheWorld()
    {
        var harness = Build();
        harness.Session.CharacterHandle = 0;

        await Send(harness);

        harness.Connection.Sent.Should().BeEmpty();
        A.CallTo(() => harness.Characters.GetItemByHandleAsync(A<string>._, A<uint>._)).MustNotHaveHappened();
    }

    // ---- the dispatch ---------------------------------------------------------------------------

    [Test]
    public void Arm_IsReachedBeforeTheFinalDispatchSwitch()
    {
        var source = File.ReadAllText(Path.Combine(RepositoryRoot(), "Game", "Network", "Clients",
            "GameClient.cs"));

        var armIndex = source.IndexOf("(ushort)GamePackets.TM_CS_TRANSMIT_ETHEREAL_DURABILITY or",
            StringComparison.Ordinal);
        var switchIndex = source.IndexOf("IPacket msg = header.ID switch", StringComparison.Ordinal);

        armIndex.Should().BeGreaterThan(-1, "GameClient must route 263 through the crafting socle");
        switchIndex.Should().BeGreaterThan(-1, "the final dispatch switch must still be the last resort");
        armIndex.Should().BeLessThan(switchIndex,
            "otherwise the frame would reach the final switch and throw on an unknown packet type");
    }

    [Test]
    public void Socle_JudgesThe263ObjectBeforeItsGenericRefusal()
    {
        var source = File.ReadAllText(Path.Combine(RepositoryRoot(), "Game", "Services",
            "CraftingSocleService.cs"));

        var caseIndex = source.IndexOf("case (ushort)GamePackets.TM_CS_TRANSMIT_ETHEREAL_DURABILITY:",
            StringComparison.Ordinal);
        var guardIndex = source.IndexOf("RefuseEtherealSacrifice(client, packetId, etherealHandle",
            StringComparison.Ordinal);
        var genericIndex = source.IndexOf("is well formed and resolvable but the crafting engine is not",
            StringComparison.Ordinal);

        caseIndex.Should().BeGreaterThan(-1, "the socle must declare the 263 case");
        guardIndex.Should().BeGreaterThan(caseIndex, "the 263 arm must hand its object to the guard");
        genericIndex.Should().BeGreaterThan(guardIndex,
            "the generic refusal must stay the answer for a frame the guard accepts");
    }

    [Test]
    public void Enum_Declares263AndItsSibling264()
    {
        ((ushort)GamePackets.TM_CS_TRANSMIT_ETHEREAL_DURABILITY).Should().Be(263);
        ((ushort)GamePackets.TM_CS_TRANSMIT_ETHEREAL_DURABILITY_TO_EQUIPMENT).Should().Be(264);
        Enum.IsDefined(typeof(GamePackets), (ushort)263).Should().BeTrue();
    }
}
