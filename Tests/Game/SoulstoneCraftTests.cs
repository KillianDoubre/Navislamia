using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using FakeItEasy;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Navislamia.Game.DataAccess.Entities.Enums;
using Navislamia.Game.DataAccess.Entities.Telecaster;
using Navislamia.Game.DataAccess.Repositories.Interfaces;
using Navislamia.Game.Network.Clients;
using Navislamia.Game.Network.Packets;
using Navislamia.Game.Network.Packets.Enums;
using Navislamia.Game.Network.Packets.Game;
using Navislamia.Game.Services;

namespace Tests.Game;

/// <summary>
/// <c>TM_CS_SOULSTONE_CRAFT</c> (260): the rules the engine applies (what a socketing costs, how many
/// identical stones an item takes), the catalog it reads them from, and the whole chain from the 27-byte
/// frame to the frames it answers with. See docs/packet-specs/260-soulstone-craft.md §5.
/// </summary>
[TestFixture]
public class SoulstoneCraftTests
{
    private const int HeaderSize = 7;
    private const uint CraftHandle = 0x80000020u;
    private const int CraftCode = 100100;
    private const int RedStone = 100200;
    private const int BlueStone = 100300;
    private const string Character = "Killian";

    /// <summary>The stone price the default item table gives the two stones: 100 / 10 is ten gold each.</summary>
    private const int StonePrice = 100;

    // ------------------------------------------------------------------ the rules

    [Test]
    public void CraftCost_DividesEachStoneByTen()
    {
        // NGemity accumulates price / 10 per stone (WorldSession.cpp:1553), so the division happens once
        // per stone and not once on the total: 100 / 10 + 25 / 10 is 12, and 12 it is charged as.
        SoulstoneCraftRules.CraftCost(new[] { 100, 25 }).Should().Be(12);
    }

    [Test]
    public void CraftCost_ChargesNothingForAStoneCheaperThanTen()
    {
        SoulstoneCraftRules.CraftCost(new[] { 9 }).Should().Be(0);
        SoulstoneCraftRules.CraftCost(Array.Empty<int>()).Should().Be(0);
    }

    [TestCase(1, 1)]
    [TestCase(2, 1)]
    [TestCase(3, 1)]
    [TestCase(4, 2)]
    public void DuplicationLimit_TakesTwoIdenticalStonesOnAFourChassisItemOnly(int socketCount, int expected)
    {
        SoulstoneCraftRules.DuplicationLimit(socketCount).Should().Be(expected);
    }

    [Test]
    public void FilledSlots_KeepsTheOrderAndSkipsTheZeroSentinel()
    {
        CraftingSocleRules.FilledSlots(new[] { 0u, 0x80000031u, 0u, 0x80000033u }, 4).Should().Equal(1, 3);
    }

    [Test]
    public void FilledSlots_IgnoresTheSlotsBeyondTheChassis()
    {
        // The frame always names four handles; an item with two chassis only has two of them looked at.
        CraftingSocleRules.FilledSlots(new[] { 0x80000031u, 0x80000032u, 0x80000033u, 0x80000034u }, 2)
            .Should().Equal(0, 1);
    }

    [Test]
    public void FilledSlots_SurvivesARequestWithoutSlots()
    {
        CraftingSocleRules.FilledSlots(null, 4).Should().BeEmpty();
    }

    [Test]
    public void CountIdenticalSockets_IgnoresTheChassisBeingFilled()
    {
        var stone = Profile(baseType0: 1, baseVar0: 12);
        var socketed = new[] { (0, stone), (2, Profile(baseType0: 9, baseVar0: 1)) };

        // Slot 0 carries the same stone, but it is the one being written: it does not count against itself.
        SoulstoneCraftRules.CountIdenticalSockets(stone, socketed, 0).Should().Be(0);
        SoulstoneCraftRules.CountIdenticalSockets(stone, socketed, 1).Should().Be(1);
    }

    [Test]
    public void CountIdenticalSockets_CountsEveryOtherChassis()
    {
        var stone = Profile(baseType0: 1, baseVar0: 12);
        var socketed = new[] { (1, stone), (2, stone), (3, Profile()) };

        SoulstoneCraftRules.CountIdenticalSockets(stone, socketed, 0).Should().Be(2);
    }

    // ------------------------------------------------------------------ the catalog

    [Test]
    public void Catalog_RecognisesASoulStoneOnAllThreeAxes()
    {
        var catalog = Catalog(Resource(RedStone), Resource(999, soulstone: false,
            baseType: ItemBaseType.Soulstone, group: ItemGroup.Etc, type: ItemType.Etc));

        catalog.TryGetResource((int)RedStone, out var stone).Should().BeTrue();
        stone.IsSoulstone.Should().BeTrue();

        // base_type 7 alone does not make a soul stone: group 93 and type 401 are read too.
        catalog.TryGetResource(999, out var half).Should().BeTrue();
        half.IsSoulstone.Should().BeFalse();

        catalog.TryGetResource(4242, out _).Should().BeFalse();
    }

    [Test]
    public void Catalog_CarriesTheChassisCountAndThePrice()
    {
        var catalog = Catalog(Resource(CraftCode, socketCount: 3, price: 7000));

        catalog.TryGetResource((int)CraftCode, out var item).Should().BeTrue();
        item.SocketCount.Should().Be(3);
        item.Price.Should().Be(7000);
    }

    [Test]
    public void Catalog_ReadsTheFourAxesOfTheFirstValueColumn()
    {
        var catalog = Catalog(Resource(RedStone,
            baseTypes: new short[] { 15, 11, 0, 0 }, baseVar1: new decimal[] { 40, 12, 0, 0 },
            optTypes: new short[] { 96, 0, 0, 0 }, optVar1: new decimal[] { 384, 0, 0, 0 }));

        catalog.TryGetResource((int)RedStone, out var stone).Should().BeTrue();
        stone.Profile.Should().Be(new SoulstoneProfile(15, 11, 0, 0, 40, 12, 0, 0, 96, 0, 0, 0, 384, 0, 0, 0));
    }

    [Test]
    public void Catalog_PadsAShortOrAbsentAxisWithZeroes()
    {
        // The reference compares zero-initialised arrays: a short or absent axis reads as zero, not as a
        // comparison that cannot be made.
        var catalog = Catalog(Resource(RedStone, baseTypes: new short[] { 15 }, baseVar1: null,
            optTypes: Array.Empty<short>(), optVar1: new decimal[] { 384 }));

        catalog.TryGetResource((int)RedStone, out var stone).Should().BeTrue();
        stone.Profile.Should().Be(new SoulstoneProfile(15, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 384, 0, 0, 0));
    }

    [Test]
    public void Catalog_IgnoresTheAxesBeyondTheFourthSlot()
    {
        var catalog = Catalog(Resource(RedStone, baseTypes: new short[] { 15, 11, 0, 0, 99 },
            baseVar1: new decimal[] { 40, 12, 0, 0, 99 }));

        catalog.TryGetResource((int)RedStone, out var stone).Should().BeTrue();
        stone.Profile.Should().Be(new SoulstoneProfile(15, 11, 0, 0, 40, 12, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0));
    }

    // ------------------------------------------------------------------ the chain, from the frame to the answer

    [Test]
    public async Task Handle_FillsAChassisSpendsTheStoneAndBillsTheGold()
    {
        var harness = Build(characterGold: 1000);
        var craft = harness.Item(CraftHandle, CraftCode, sockets: new long[4]);
        var stone = harness.Item(0x80000030u, RedStone, amount: 1);

        await harness.Service.HandleAsync(harness.Client, Frame(CraftHandle, 0x80000030u, 0u, 0u, 0u));

        craft.SocketItemIds.Should().Equal(RedStone, 0, 0, 0);
        harness.Character.Items.Should().NotContain(stone);
        A.CallTo(() => harness.Repository.DeleteItem(stone)).MustHaveHappenedOnceExactly();
        harness.Session.CharacterGold.Should().Be(1000 - StonePrice / 10);

        harness.Ids().Should().Equal(
            (ushort)GamePackets.TM_SC_GOLD_UPDATE,
            (ushort)GamePackets.TM_SC_DESTROY_ITEM,
            (ushort)GamePackets.TM_SC_INVENTORY,
            (ushort)GamePackets.TM_SC_RESULT);

        ResultOf(harness.Connection.Sent[3]).Result.Should().Be((ushort)ResultCode.Success);
        ResultOf(harness.Connection.Sent[3]).RequestMsgID.Should().Be(260);
        ResultOf(harness.Connection.Sent[3]).Value.Should().Be(0);
        BinaryPrimitives.ReadUInt64LittleEndian(harness.Connection.Sent[0].AsSpan(HeaderSize, 8))
            .Should().Be((ulong)harness.Session.CharacterGold);
        A.CallTo(() => harness.Repository.SaveChangesAsync()).MustHaveHappenedOnceExactly();
    }

    [Test]
    public async Task Handle_AnswersTheItemItJustSocketedWithTheCodesInTheirChassis()
    {
        var harness = Build(characterGold: 500);
        var craft = harness.Item(CraftHandle, CraftCode, sockets: new long[4]);
        harness.Item(0x80000030u, RedStone, amount: 1);
        harness.Item(0x80000032u, BlueStone, amount: 1);

        await harness.Service.HandleAsync(harness.Client, Frame(CraftHandle, 0x80000030u, 0u, 0x80000032u, 0u));

        craft.SocketItemIds.Should().Equal(RedStone, 0, BlueStone, 0);

        var inventory = harness.Connection.Sent.Single(packet => IdOf(packet) == (ushort)GamePackets.TM_SC_INVENTORY);
        BinaryPrimitives.ReadUInt16LittleEndian(inventory.AsSpan(HeaderSize, 2)).Should().Be(1);
        BinaryPrimitives.ReadUInt32LittleEndian(inventory.AsSpan(HeaderSize + 2, 4)).Should().Be(CraftHandle);
        for (var slot = 0; slot < 4; slot++)
        {
            var expected = slot switch { 0 => RedStone, 2 => BlueStone, _ => 0 };
            BinaryPrimitives.ReadInt32LittleEndian(inventory.AsSpan(HeaderSize + 2 + 38 + slot * 4, 4))
                .Should().Be((int)expected, "socket {0} of the answered item", slot);
        }
    }

    [Test]
    public async Task Handle_BillsEveryStoneOfTheRequest()
    {
        var harness = Build(characterGold: 1000, Resource(RedStone, price: 100), Resource(BlueStone, price: 25));
        harness.Item(CraftHandle, CraftCode, sockets: new long[4]);
        harness.Item(0x80000030u, RedStone, amount: 1);
        harness.Item(0x80000031u, BlueStone, amount: 1);

        await harness.Service.HandleAsync(harness.Client, Frame(CraftHandle, 0x80000030u, 0x80000031u, 0u, 0u));

        harness.Session.CharacterGold.Should().Be(1000 - 12);
    }

    [Test]
    public async Task Handle_TakesOneUnitOffAStackAndKeepsTheRest()
    {
        var harness = Build(characterGold: 1000);
        harness.Item(CraftHandle, CraftCode, sockets: new long[4]);
        var stone = harness.Item(0x80000030u, RedStone, amount: 3);

        await harness.Service.HandleAsync(harness.Client, Frame(CraftHandle, 0x80000030u, 0u, 0u, 0u));

        stone.Amount.Should().Be(2);
        A.CallTo(() => harness.Repository.DeleteItem(stone)).MustNotHaveHappened();

        var count = harness.Connection.Sent.Single(packet =>
            IdOf(packet) == (ushort)GamePackets.TM_SC_UPDATE_ITEM_COUNT);
        BinaryPrimitives.ReadUInt32LittleEndian(count.AsSpan(HeaderSize, 4)).Should().Be(0x80000030u);
        BinaryPrimitives.ReadInt64LittleEndian(count.AsSpan(HeaderSize + 4, 8)).Should().Be(2);
    }

    [Test]
    public async Task Handle_LeavesTheChassisBeyondTheItemOnesUntouched()
    {
        var harness = Build(characterGold: 1000, Resource(CraftCode, socketCount: 1));
        var craft = harness.Item(CraftHandle, CraftCode, sockets: new long[4]);
        var first = harness.Item(0x80000030u, RedStone, amount: 1);
        var third = harness.Item(0x80000032u, BlueStone, amount: 1);

        await harness.Service.HandleAsync(harness.Client, Frame(CraftHandle, 0x80000030u, 0u, 0x80000032u, 0u));

        craft.SocketItemIds.Should().Equal(RedStone, 0, 0, 0);
        harness.Character.Items.Should().Contain(third, "the slot past the chassis is never read");
        A.CallTo(() => harness.Repository.DeleteItem(first)).MustHaveHappenedOnceExactly();
    }

    [Test]
    public async Task Handle_DoesNotResolveAnEmptySlot()
    {
        var harness = Build(characterGold: 1000, Resource(CraftCode, socketCount: 2));
        harness.Item(CraftHandle, CraftCode, sockets: new long[4]);
        harness.Item(0x80000030u, RedStone, amount: 1);

        await harness.Service.HandleAsync(harness.Client, Frame(CraftHandle, 0x80000030u, 0u, 0u, 0u));

        // One read for the craft item, one for the stone, one for the mutation: a zero slot is never resolved
        // into a read of its own.
        A.CallTo(() => harness.Repository.GetCharacterByNameWithItemsAsync(Character))
            .MustHaveHappened(3, Times.Exactly);
    }

    [Test]
    public async Task Handle_ReplacesWhateverTheChassisAlreadyHeld()
    {
        // NGemity writes the code of the provided stone into the socket, whatever was there: the stone that
        // occupied it is not given back (WorldSession.cpp:1568). See the fiche §5.5.
        var harness = Build(characterGold: 1000);
        var craft = harness.Item(CraftHandle, CraftCode, sockets: new long[] { BlueStone, 0, 0, 0 });
        harness.Item(0x80000030u, RedStone, amount: 1);

        await harness.Service.HandleAsync(harness.Client, Frame(CraftHandle, 0x80000030u, 0u, 0u, 0u));

        craft.SocketItemIds.Should().Equal(RedStone, 0, 0, 0);
    }

    // ------------------------------------------------------------------ the refusals

    [Test]
    public async Task Handle_RefusesAMalformedFrame()
    {
        var harness = Build(characterGold: 1000);

        await harness.Service.HandleAsync(harness.Client, new byte[26]);

        harness.Ids().Should().Equal((ushort)GamePackets.TM_SC_RESULT);
        ResultOf(harness.Connection.Sent[0]).Result.Should().Be((ushort)ResultCode.InvalidArgument);
        A.CallTo(() => harness.Repository.GetCharacterByNameWithItemsAsync(A<string>._)).MustNotHaveHappened();
    }

    [Test]
    public async Task Handle_IgnoresARequestFromOutsideTheWorld()
    {
        var harness = Build(characterGold: 1000);
        harness.Session.CharacterHandle = 0;

        await harness.Service.HandleAsync(harness.Client, Frame(CraftHandle, 0x80000030u, 0u, 0u, 0u));

        harness.Connection.Sent.Should().BeEmpty();
    }

    [Test]
    public async Task Handle_RefusesACraftItemTheCharacterDoesNotHave()
    {
        var harness = Build(characterGold: 1000);

        await harness.Service.HandleAsync(harness.Client, Frame(0x80000029u, 0x80000030u, 0u, 0u, 0u));

        var result = ResultOf(harness.Connection.Sent.Single());
        result.Result.Should().Be((ushort)ResultCode.NotExist);
        result.Value.Should().Be(unchecked((int)0x80000029u));
    }

    [TestCase(0, TestName = "Handle_RefusesAnItemWithoutAChassis")]
    [TestCase(5, TestName = "Handle_RefusesAChassisCountBeyondFour")]
    public async Task Handle_RefusesAnItemWhoseChassisCountIsOutOfRange(int socketCount)
    {
        var harness = Build(characterGold: 1000, Resource(CraftCode, socketCount: socketCount));
        harness.Item(CraftHandle, CraftCode, sockets: new long[4]);
        harness.Item(0x80000030u, RedStone, amount: 1);

        await harness.Service.HandleAsync(harness.Client, Frame(CraftHandle, 0x80000030u, 0u, 0u, 0u));

        var result = ResultOf(harness.Connection.Sent.Single());
        result.Result.Should().Be((ushort)ResultCode.AccessDenied);
        result.Value.Should().Be(unchecked((int)CraftHandle));
    }

    [Test]
    public async Task Handle_RefusesACraftItemWhoseResourceIsUnknown()
    {
        var harness = Build(characterGold: 1000, Resource(CraftCode, socketCount: 0));
        harness.Character.Items.Add(new ItemEntity { Id = CraftHandle, ItemResourceId = 987654, Amount = 1, Idx = 0 });

        await harness.Service.HandleAsync(harness.Client, Frame(CraftHandle, 0x80000030u, 0u, 0u, 0u));

        var result = ResultOf(harness.Connection.Sent.Single());
        result.Result.Should().Be((ushort)ResultCode.AccessDenied);
        result.Value.Should().Be(unchecked((int)CraftHandle));
    }

    [Test]
    public async Task Handle_RefusesAStoneTheCharacterDoesNotHave()
    {
        var harness = Build(characterGold: 1000);
        harness.Item(CraftHandle, CraftCode, sockets: new long[4]);

        await harness.Service.HandleAsync(harness.Client, Frame(CraftHandle, 0u, 0x80000031u, 0u, 0u));

        var result = ResultOf(harness.Connection.Sent.Single());
        result.Result.Should().Be((ushort)ResultCode.AccessDenied,
            "NGemity answers ACCESS_DENIED for a slot handle (:1521) where the craft item itself gets NOT_EXIST");
        result.Value.Should().Be(unchecked((int)0x80000031u));
    }

    [Test]
    public async Task Handle_RefusesAnItemThatIsNotASoulStone()
    {
        var harness = Build(characterGold: 1000, Resource(100400, soulstone: false));
        harness.Item(CraftHandle, CraftCode, sockets: new long[4]);
        harness.Item(0x80000033u, 100400, amount: 1);

        await harness.Service.HandleAsync(harness.Client, Frame(CraftHandle, 0x80000033u, 0u, 0u, 0u));

        var result = ResultOf(harness.Connection.Sent.Single());
        result.Result.Should().Be((ushort)ResultCode.NotActable);
        result.Value.Should().Be(unchecked((int)0x80000033u));
    }

    [Test]
    public async Task Handle_RefusesARequestWithNoStoneAtAll()
    {
        var harness = Build(characterGold: 1000);
        harness.Item(CraftHandle, CraftCode, sockets: new long[4]);

        await harness.Service.HandleAsync(harness.Client, Frame(CraftHandle, 0u, 0u, 0u, 0u));

        var result = ResultOf(harness.Connection.Sent.Single());
        result.Result.Should().Be((ushort)ResultCode.InvalidArgument);
        result.Value.Should().Be(0);
    }

    [Test]
    public async Task Handle_RefusesASecondIdenticalStoneOnATwoChassisItem()
    {
        var harness = Build(characterGold: 1000, Resource(CraftCode, socketCount: 2));
        var craft = harness.Item(CraftHandle, CraftCode, sockets: new long[] { RedStone, 0, 0, 0 });
        var stone = harness.Item(0x80000030u, RedStone, amount: 1);

        await harness.Service.HandleAsync(harness.Client, Frame(CraftHandle, 0u, 0x80000030u, 0u, 0u));

        var result = ResultOf(harness.Connection.Sent.Single());
        result.Result.Should().Be((ushort)ResultCode.AlreadyExist);
        result.Value.Should().Be(0);
        craft.SocketItemIds.Should().Equal(RedStone, 0, 0, 0);
        harness.Character.Items.Should().Contain(stone);
        harness.Session.CharacterGold.Should().Be(1000);
        A.CallTo(() => harness.Repository.SaveChangesAsync()).MustNotHaveHappened();
    }

    [Test]
    public async Task Handle_AcceptsADifferentStoneInTheOtherChassis()
    {
        var harness = Build(characterGold: 1000, Resource(CraftCode, socketCount: 2),
            Resource(RedStone, baseTypes: new short[] { 1, 0, 0, 0 }),
            Resource(BlueStone, baseTypes: new short[] { 2, 0, 0, 0 }));
        var craft = harness.Item(CraftHandle, CraftCode, sockets: new long[] { RedStone, 0, 0, 0 });
        harness.Item(0x80000030u, BlueStone, amount: 1);

        await harness.Service.HandleAsync(harness.Client, Frame(CraftHandle, 0u, 0x80000030u, 0u, 0u));

        craft.SocketItemIds.Should().Equal(RedStone, BlueStone, 0, 0);
        ResultOf(harness.Connection.Sent[^1]).Result.Should().Be((ushort)ResultCode.Success);
    }

    [Test]
    public async Task Handle_TakesTwoIdenticalStonesOnAFourChassisItem()
    {
        var harness = Build(characterGold: 1000, Resource(CraftCode, socketCount: 4));
        var craft = harness.Item(CraftHandle, CraftCode, sockets: new long[4]);
        harness.Item(0x80000030u, RedStone, amount: 1);
        harness.Item(0x80000031u, RedStone, amount: 1);

        await harness.Service.HandleAsync(harness.Client, Frame(CraftHandle, 0x80000030u, 0x80000031u, 0u, 0u));

        craft.SocketItemIds.Should().Equal(RedStone, RedStone, 0, 0);
        ResultOf(harness.Connection.Sent[^1]).Result.Should().Be((ushort)ResultCode.Success);
    }

    [Test]
    public async Task Handle_RefusesWhenTheGoldIsShort()
    {
        var harness = Build(characterGold: 9);
        var craft = harness.Item(CraftHandle, CraftCode, sockets: new long[] { 0, 0, 0, 0 });
        var stone = harness.Item(0x80000030u, RedStone, amount: 1);

        await harness.Service.HandleAsync(harness.Client, Frame(CraftHandle, 0x80000030u, 0u, 0u, 0u));

        ResultOf(harness.Connection.Sent.Single()).Result.Should().Be((ushort)ResultCode.NotEnoughMoney);
        craft.SocketItemIds.Should().AllBeEquivalentTo(0);
        harness.Character.Items.Should().Contain(stone);
        harness.Session.CharacterGold.Should().Be(9);
        A.CallTo(() => harness.Repository.SaveChangesAsync()).MustNotHaveHappened();
    }

    [Test]
    public async Task Handle_AnswersADatabaseFailure()
    {
        var harness = Build(characterGold: 1000);
        A.CallTo(() => harness.Repository.GetCharacterByNameWithItemsAsync(Character))
            .ThrowsAsync(new InvalidOperationException("no database"));

        await harness.Service.HandleAsync(harness.Client, Frame(CraftHandle, 0x80000030u, 0u, 0u, 0u));

        ResultOf(harness.Connection.Sent.Single()).Result.Should().Be((ushort)ResultCode.DBError);
    }

    // ------------------------------------------------------------------ harness

    /// <summary>
    /// One character, one repository and the real chain: the catalog over a fake item resource table, the
    /// real <see cref="CharacterService"/> over a fake repository, and the engine between them.
    /// </summary>
    private sealed record Harness(SoulstoneCraftService Service, GameClient Client,
        StorageTestHarness.FrameConnection Connection, ConnectionInfo Session, ICharacterService Characters,
        ICharacterRepository Repository, CharacterEntity Character)
    {
        public ItemEntity Item(uint handle, long code, long amount = 1, long[] sockets = null)
        {
            var item = new ItemEntity
            {
                Id = handle,
                ItemResourceId = code,
                Amount = amount,
                Idx = Character.Items.Count,
                SocketItemIds = sockets,
            };

            Character.Items.Add(item);
            return item;
        }

        public List<ushort> Ids() => Connection.Sent.Select(IdOf).ToList();
    }

    /// <summary>
    /// The default item table: the item being socketed (four chassis = 4) and the two stones at
    /// <see cref="StonePrice"/>. An override replaces the entry of the same id, which is how a test asks for
    /// an item with another chassis count, another price or another profile.
    /// </summary>
    private static Harness Build(long characterGold, params ItemSoulstoneCraftFields[] overrides)
    {
        var table = new List<ItemSoulstoneCraftFields>
        {
            Resource(CraftCode, socketCount: 4),
            Resource(RedStone, price: StonePrice),
            Resource(BlueStone, price: StonePrice),
        };
        table.AddRange(overrides);

        var catalogRepository = A.Fake<IItemResourceRepository>();
        A.CallTo(() => catalogRepository.GetSoulstoneCraftFields()).Returns(table);
        var catalog = new SoulstoneCraftCatalog(catalogRepository);

        var character = new CharacterEntity
        {
            CharacterName = Character,
            Gold = characterGold,
            Items = new List<ItemEntity>(),
        };
        var repository = A.Fake<ICharacterRepository>();
        A.CallTo(() => repository.GetCharacterByNameWithItemsAsync(Character)).Returns(character);

        var factory = A.Fake<ICharacterRepositoryFactory>();
        A.CallTo(() => factory.Create()).Returns(repository);

        var characters = new CharacterService(A.Fake<IStarterItemsRepository>(), factory, new CharacterGate(),
            A.Fake<ILogger<CharacterService>>());

        var connection = new StorageTestHarness.FrameConnection(Array.Empty<byte>());
        var client = StorageTestHarness.NewGameClient(connection);
        var session = StorageTestHarness.Session(client);
        session.CharacterName = Character;
        session.CharacterHandle = 42;
        session.CharacterGold = characterGold;

        return new Harness(new SoulstoneCraftService(characters, catalog), client, connection, session, characters,
            repository, character);
    }

    private static ISoulstoneCraftCatalog Catalog(params ItemSoulstoneCraftFields[] fields)
    {
        var repository = A.Fake<IItemResourceRepository>();
        A.CallTo(() => repository.GetSoulstoneCraftFields()).Returns(fields);
        return new SoulstoneCraftCatalog(repository);
    }

    private static ItemSoulstoneCraftFields Resource(int id, int socketCount = 4, int price = 0, bool soulstone = true,
        ItemBaseType? baseType = null, ItemGroup? group = null, ItemType? type = null,
        short[] baseTypes = null, decimal[] baseVar1 = null, short[] optTypes = null, decimal[] optVar1 = null)
    {
        return new ItemSoulstoneCraftFields(id,
            baseType ?? (soulstone ? ItemBaseType.Soulstone : ItemBaseType.Armor),
            group ?? (soulstone ? ItemGroup.Soulstone : ItemGroup.Armor),
            type ?? (soulstone ? ItemType.Soulstone : ItemType.Etc),
            socketCount, price,
            baseTypes ?? new short[4], baseVar1 ?? new decimal[4], optTypes ?? new short[4],
            optVar1 ?? new decimal[4]);
    }

    private static SoulstoneProfile Profile(short baseType0 = 0, decimal baseVar0 = 0)
        => new(baseType0, 0, 0, 0, baseVar0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0);

    private static byte[] Frame(uint craftItemHandle, params uint[] slots)
    {
        var packet = new byte[HeaderSize + 4 + 4 * 4];
        BinaryPrimitives.WriteUInt32LittleEndian(packet.AsSpan(0, 4), (uint)packet.Length);
        BinaryPrimitives.WriteUInt16LittleEndian(packet.AsSpan(4, 2), (ushort)GamePackets.TM_CS_SOULSTONE_CRAFT);
        BinaryPrimitives.WriteUInt32LittleEndian(packet.AsSpan(HeaderSize, 4), craftItemHandle);

        for (var i = 0; i < slots.Length; i++)
        {
            BinaryPrimitives.WriteUInt32LittleEndian(packet.AsSpan(HeaderSize + 4 + i * 4, 4), slots[i]);
        }

        return packet;
    }

    private static ushort IdOf(byte[] packet) => BinaryPrimitives.ReadUInt16LittleEndian(packet.AsSpan(4, 2));

    private static TS_SC_RESULT ResultOf(byte[] packet)
        => new Packet<TS_SC_RESULT>(packet).GetDataStruct<TS_SC_RESULT>();
}
