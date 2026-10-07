using System;
using System.Collections.Generic;
using System.Net.Sockets;
using System.Reflection;
using System.Threading;
using FakeItEasy;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Navislamia.Configuration.Options;
using Navislamia.Game.DataAccess.Repositories.Interfaces;
using Navislamia.Game.Network;
using Navislamia.Game.Network.Clients;
using Navislamia.Game.Services;
using Navislamia.Game.Services.Interfaces;

namespace Tests.Game;

/// <summary>
/// The frame-level harness the storage tests share: an in-memory connection that serves plaintext frames a
/// byte at a time and records what the receive loop pushes back, a game client wired to fakes, and the
/// session reader. Same model as the packet-57 tests, which are the only other tests driving
/// <c>GameClient.OnDataReceived</c> for real.
/// </summary>
internal static class StorageTestHarness
{
    public static byte Checksum(byte[] packet)
    {
        byte checksum = 0;
        for (var index = 0; index < 6; index++)
        {
            checksum += packet[index];
        }

        return checksum;
    }

    public static GameClient NewGameClient(Connection connection, IStorageService storageService = null,
        IGmCommandService gmCommandService = null,
        Navislamia.Game.Services.Pets.IPetSummonService petSummonService = null,
        ICharacterService characterService = null,
        ICommercialStorageService commercialStorageService = null,
        IPlayerVisibilityService playerVisibilityService = null,
        IBoothTradeService boothTradeService = null,
        IMarketTradeService marketTradeService = null,
        IMarketSellService marketSellService = null,
        ICraftingSocleService craftingSocleService = null,
        IItemDonateService itemDonateService = null,
        ISkillCardService skillCardService = null,
        ICardSocketService cardSocketService = null,
        Navislamia.Game.Services.Party.IPartyService partyService = null,
        Navislamia.Game.Services.Trade.IPlayerTradeService playerTradeService = null,
        Navislamia.Game.Services.Weight.ICarriedWeightService carriedWeightService = null,
        ICombatService combatService = null,
        Navislamia.Game.Services.Creatures.ICreatureService creatureService = null,
        ISkillCastService skillCastService = null, IDonationRankingService donationRankingService = null,
        Navislamia.Game.Services.Creatures.ICreatureFarmService creatureFarmService = null)
    {
        characterService ??= A.Fake<ICharacterService>();
        playerVisibilityService ??= new PlayerVisibilityService(A.Fake<ILogger<PlayerVisibilityService>>());
        var boothWatchService = new BoothWatchService(characterService);

        var networkService = new NetworkService(
            A.Fake<ILogger<NetworkService>>(),
            Options.Create(new NetworkOptions { CipherKey = "storage-test-key" }),
            characterService,
            A.Fake<IBannedWordsRepository>(),
            A.Fake<IStatService>(),
            Options.Create(new ServerOptions()),
            A.Fake<INpcSpawnService>(),
            A.Fake<INpcDialogService>(),
            A.Fake<IMonsterSpawnService>(),
            combatService ?? A.Fake<ICombatService>(),
            A.Fake<ILevelingService>(),
            A.Fake<ISkillService>(),
            A.Fake<IEquipmentService>(),
            A.Fake<IInventoryService>(),
            A.Fake<IGroundItemService>(),
            skillCastService ?? A.Fake<ISkillCastService>(),
            A.Fake<IFieldPropService>(),
            A.Fake<IItemUseService>(),
            marketSellService ?? A.Fake<IMarketSellService>(),
            A.Fake<IWorldLocationService>(),
            A.Fake<IResurrectionService>(),
            A.Fake<IEventAreaService>(),
            craftingSocleService ?? A.Fake<ICraftingSocleService>(),
            storageService ?? A.Fake<IStorageService>(),
            A.Fake<IQuestService>(),
            gmCommandService ?? A.Fake<IGmCommandService>(),
            petSummonService ?? A.Fake<Navislamia.Game.Services.Pets.IPetSummonService>(),
            boothWatchService,
            commercialStorageService ?? A.Fake<ICommercialStorageService>(),
            playerVisibilityService,
            boothTradeService
                ?? new BoothTradeService(characterService, playerVisibilityService, boothWatchService),
            marketTradeService ?? A.Fake<IMarketTradeService>(),
            A.Fake<ISoulstoneCraftService>(),
            itemDonateService ?? A.Fake<IItemDonateService>(),
            skillCardService ?? A.Fake<ISkillCardService>(),
            cardSocketService ?? A.Fake<ICardSocketService>(),
            partyService ?? new Navislamia.Game.Services.Party.PartyService(playerVisibilityService,
                A.Fake<IStatService>(), A.Fake<IBannedWordsRepository>()),
            playerTradeService ?? new Navislamia.Game.Services.Trade.PlayerTradeService(characterService,
                playerVisibilityService),
            carriedWeightService, creatureService: creatureService, donationRankingService: donationRankingService,
            creatureFarmService: creatureFarmService);

        var socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);

        return new GameClient(socket, networkService) { Connection = connection };
    }

    /// <summary>
    /// <c>Client.ConnectionInfo</c> is internal, so the test assembly reads the session it was given
    /// through reflection instead of widening the production surface for the tests' sake.
    /// </summary>
    public static ConnectionInfo Session(GameClient client)
    {
        var property = typeof(Client).GetProperty("ConnectionInfo",
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

        return (ConnectionInfo)property!.GetValue(client)!;
    }

    /// <summary>
    /// The <see cref="NetworkService"/> a game client was built with. Its
    /// <c>AuthorizedGameClients</c> is how a service reaches the sessions of the other players, and the
    /// field is private: the booth visibility socle scans it to resolve a booth handle.
    /// </summary>
    public static NetworkService Network(GameClient client)
    {
        var field = typeof(Client).GetField("_networkService",
            BindingFlags.Instance | BindingFlags.NonPublic);

        return (NetworkService)field!.GetValue(client)!;
    }

    /// <summary>
    /// The receive loop fires the handler and moves on, so a test that wants to observe what the handler
    /// did has to give the continuation a bounded moment to run.
    /// </summary>
    public static void WaitFor(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow.AddSeconds(2);
        while (!condition() && DateTime.UtcNow < deadline)
        {
            Thread.Sleep(5);
        }
    }

    /// <summary>
    /// In-memory replacement for the socket-backed connection: it serves a plaintext frame byte by byte
    /// and records everything the receive loop pushes back, so a test can tell an ignored packet from an
    /// answered one.
    /// </summary>
    internal sealed class FrameConnection : Connection
    {
        private readonly byte[] _frame;
        private int _offset;

        public FrameConnection(byte[] frame)
            : base(new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp))
        {
            _frame = frame;
        }

        public List<byte[]> Sent { get; } = new();

        public int BytesAvailable => _frame.Length - _offset;

        public override ReadOnlySpan<byte> Peek(int length) => new(_frame, _offset, length);

        public override byte[] Read(int input)
        {
            var length = Math.Min(BytesAvailable, input);
            var read = _frame.AsSpan(_offset, length).ToArray();
            _offset += length;
            return read;
        }

        public override void Send(byte[] buffer) => Sent.Add(buffer);
    }
}
