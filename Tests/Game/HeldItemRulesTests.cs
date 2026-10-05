using System;
using System.Buffers.Binary;
using System.Linq;
using System.Threading.Tasks;
using FakeItEasy;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Navislamia.Game.DataAccess.Entities.Enums;
using Navislamia.Game.DataAccess.Entities.Telecaster;
using Navislamia.Game.DataAccess.Repositories.Interfaces;
using Navislamia.Game.Network.Clients;
using Navislamia.Game.Network.Packets.Enums;
using Navislamia.Game.Network.Packets.Game;
using Navislamia.Game.Services;
using Navislamia.Game.Services.Creatures;
using Navislamia.Game.Services.Pets;
using Navislamia.Game.Services.Trade;

namespace Tests.Game;

/// <summary>
/// The session part of the official <c>StructPlayer::IsErasable</c> (<c>StructPlayer.cpp:12498</c>): a formed card, a
/// card whose summon is out, a belt slot and the cage of the pet out cannot leave the bag — by trade, storage or
/// destruction alike. docs/packet-specs/socle-duree-invocations.md §6.
/// </summary>
[TestFixture]
public class HeldItemRulesTests
{
    private const long Card = 60;

    private static (GameClient Client, ConnectionInfo Info) Session(string name = "Ana", uint handle = 1)
    {
        var client = StorageTestHarness.NewGameClient(new StorageTestHarness.FrameConnection(Array.Empty<byte>()));
        var info = StorageTestHarness.Session(client);
        info.CharacterName = name;
        info.CharacterHandle = handle;
        return (client, info);
    }

    private static void SummonOut(ConnectionInfo info, long cardId)
    {
        info.CreatureCards[cardId] = new CreatureCard { ItemId = cardId, SummonHandle = 0x50000001 };
        info.Summons = new[] { new SummonPresence(0x50000001, new SummonWorldEntry(), 0, 0, 0) };
    }

    [Test]
    public void A_held_item_is_not_erasable_and_anything_else_is()
    {
        var (_, info) = Session();
        HeldItemRules.IsErasable(info, Card).Should().BeTrue();

        info.SummonSlots = new long[] { 0, Card, 0, 0, 0, 0 };
        HeldItemRules.IsErasable(info, Card).Should().BeFalse("m_aBindSummonCard: a formed card");
        info.SummonSlots = Array.Empty<long>();

        info.BeltItemIds = new long[] { Card };
        HeldItemRules.IsErasable(info, Card).Should().BeFalse("m_aBeltSlotCard");
        info.BeltItemIds = Array.Empty<long>();

        SummonOut(info, Card);
        HeldItemRules.IsErasable(info, Card).Should().BeFalse("its summon is in the world");
        info.Summons = Array.Empty<SummonPresence>();
        HeldItemRules.IsErasable(info, Card).Should().BeTrue("the summon went back");

        info.ActivePet = new ActivePet(0x60000001, (uint)Card, new PetWorldEntry());
        HeldItemRules.IsErasable(info, Card).Should().BeFalse("the cage of the pet out");
        HeldItemRules.IsErasable(info, Card + 1).Should().BeTrue();
    }

    [Test]
    public async Task A_formed_card_or_one_whose_summon_is_out_is_not_traded_but_a_tamed_card_is()
    {
        var visibility = new PlayerVisibilityService(A.Fake<ILogger<PlayerVisibilityService>>());
        var characters = A.Fake<ICharacterService>();
        var trade = new PlayerTradeService(characters, visibility);
        var ana = StorageTestHarness.NewGameClient(new StorageTestHarness.FrameConnection(Array.Empty<byte>()),
            playerVisibilityService: visibility);
        var bo = StorageTestHarness.NewGameClient(new StorageTestHarness.FrameConnection(Array.Empty<byte>()),
            playerVisibilityService: visibility);
        foreach (var (client, handle, name) in new[] { (ana, 1u, "Ana"), (bo, 2u, "Bo") })
        {
            var session = StorageTestHarness.Session(client);
            session.CharacterHandle = handle;
            session.CharacterName = name;
            session.X = 100;
            session.Y = 100;
            visibility.Registry.Register(handle, client);
        }

        var info = StorageTestHarness.Session(ana);
        foreach (var id in new long[] { Card, Card + 1 })
        {
            var item = new ItemEntity { Id = id, ItemResourceId = 540001, Amount = 1, WearInfo = ItemWearType.None,
                Flag = CreatureRules.WithSummonFlag(ItemFlag.None) };
            A.CallTo(() => characters.GetItemByHandleAsync("Ana", (uint)id)).Returns(item);
        }

        await trade.HandleAsync(ana, new PlayerTradePackets.TradeRequest(2, TradeMode.Request, 0, 0));
        await trade.HandleAsync(bo, new PlayerTradePackets.TradeRequest(1, TradeMode.Accept, 0, 0));
        var sent = ((StorageTestHarness.FrameConnection)ana.Connection).Sent;
        sent.Clear();

        info.SummonSlots = new long[] { Card, 0, 0, 0, 0, 0 };
        await trade.HandleAsync(ana, new PlayerTradePackets.TradeRequest(2, TradeMode.AddItem, Card, 1));
        Result(sent.Last()).Should().Be((ushort)Navislamia.Game.Network.Packets.ResultCode.NotActable);

        info.SummonSlots = Array.Empty<long>();
        SummonOut(info, Card);
        await trade.HandleAsync(ana, new PlayerTradePackets.TradeRequest(2, TradeMode.AddItem, Card, 1));
        Result(sent.Last()).Should().Be((ushort)Navislamia.Game.Network.Packets.ResultCode.NotActable);

        sent.Clear();
        await trade.HandleAsync(ana, new PlayerTradePackets.TradeRequest(2, TradeMode.AddItem, Card + 1, 1));
        sent.Select(Id).Should().Contain((ushort)GamePackets.TM_TRADE, "a tamed card neither formed nor out is offered");
    }

    [Test]
    public async Task A_held_item_stays_in_the_bag_silently_when_stored()
    {
        var repository = A.Fake<IStorageRepository>();
        var (client, info) = Session();
        info.StorageSecurityCheck = true;
        info.SummonSlots = new long[] { Card, 0, 0, 0, 0, 0 };
        var service = new StorageService(repository, new CharacterGate());

        await service.HandleAsync(client, new GameActionPackets.StorageRequest((uint)Card, 0, 1));

        A.CallTo(() => repository.MoveAsync(A<string>._, A<uint>._, A<bool>._, A<long>._)).MustNotHaveHappened();
        ((StorageTestHarness.FrameConnection)client.Connection).Sent.Should().BeEmpty("onStorage ignores the refusal");
    }

    [Test]
    public async Task A_held_item_is_not_destroyed()
    {
        var characters = A.Fake<ICharacterService>();
        var (client, info) = Session();
        SummonOut(info, Card);
        var service = new InventoryService(characters, A.Fake<IItemSortCatalog>());

        await service.EraseAsync(client, new[] { new GameActionPackets.EraseItemRequest((uint)Card, 1) });

        A.CallTo(() => characters.EraseItemsAsync(A<string>._, A<GameActionPackets.EraseItemRequest[]>._))
            .MustNotHaveHappened();
        Result(((StorageTestHarness.FrameConnection)client.Connection).Sent.Last())
            .Should().Be((ushort)Navislamia.Game.Network.Packets.ResultCode.NotExist);
    }

    [Test]
    public async Task Unwearing_an_item_left_by_a_departed_summon_clears_its_summon_too()
    {
        var item = new ItemEntity { Id = 5, ItemResourceId = 690001, WearInfo = 0, EquippedBySummonId = 77 };
        var character = new CharacterEntity { CharacterName = "Ana", Items = new System.Collections.Generic.List<ItemEntity> { item } };
        var repository = A.Fake<ICharacterRepository>();
        A.CallTo(() => repository.GetCharacterByNameWithItemsAsync("Ana")).Returns(character);
        var factory = A.Fake<ICharacterRepositoryFactory>();
        A.CallTo(() => factory.Create()).Returns(repository);
        var service = new CharacterService(A.Fake<IStarterItemsRepository>(), factory, new CharacterGate(),
            A.Fake<ILogger<CharacterService>>());

        await service.UnwearItemsAsync("Ana", new long[] { 5 });

        item.WearInfo.Should().Be(ItemWearType.None);
        item.EquippedBySummonId.Should().BeNull();
        A.CallTo(() => repository.SaveChangesAsync()).MustHaveHappenedOnceExactly();
    }

    private static ushort Id(byte[] frame) => BinaryPrimitives.ReadUInt16LittleEndian(frame.AsSpan(4, 2));

    private static ushort Result(byte[] frame) => BinaryPrimitives.ReadUInt16LittleEndian(frame.AsSpan(9, 2));
}
