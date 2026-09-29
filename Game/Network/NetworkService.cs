using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Net.Sockets;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Navislamia.Configuration.Options;
using Navislamia.Game.DataAccess.Repositories.Interfaces;
using Navislamia.Game.Network.Clients;
using Navislamia.Game.Network.Interfaces;
using Navislamia.Game.Network.Packets;
using Navislamia.Game.Network.Packets.Interfaces;
using Navislamia.Game.Services;
using Navislamia.Game.Services.Interfaces;

namespace Navislamia.Game.Network;

public class NetworkService : INetworkService
{
    private readonly ILogger<NetworkService> _logger;
    public readonly ICharacterService CharacterService;
    public readonly IBannedWordsRepository BannedWordsRepository;
    public readonly IStatService StatService;
    public readonly INpcSpawnService NpcSpawnService;
    public readonly INpcDialogService NpcDialogService;
    public readonly IMonsterSpawnService MonsterSpawnService;
    public readonly ICombatService CombatService;
    public readonly ILevelingService LevelingService;
    public readonly ISkillService SkillService;
    public readonly IEquipmentService EquipmentService;
    public readonly IInventoryService InventoryService;
    public readonly IItemUseService ItemUseService;
    public readonly IStorageService StorageService;
    public readonly IQuestService QuestService;
    public readonly IGmCommandService GmCommandService;
    public readonly IGroundItemService GroundItemService;
    public readonly ICraftingSocleService CraftingSocleService;
    public readonly IBoothWatchService BoothWatchService;
    public readonly IFieldPropService FieldPropService;
    public readonly ISkillCastService SkillCastService;
    public readonly IEventAreaService EventAreaService;
    public readonly IResurrectionService ResurrectionService;
    public readonly IWorldLocationService WorldLocationService;
    public readonly Navislamia.Game.Services.Pets.IPetSummonService PetSummonService;
    public readonly NetworkOptions NetworkOptions;
    public readonly ServerOptions ServerOptions;

    public AuthClient AuthClient { get; set; }

    public UploadClient UploadClient { get; set; }

    public Dictionary<string, GameClient> UnauthorizedGameClients { get; set; } = new();

    public ConcurrentDictionary<string, GameClient> AuthorizedGameClients { get; set; } = new();

    public NetworkService(ILogger<NetworkService> logger, IOptions<NetworkOptions> networkOptions,
        ICharacterService characterService, IBannedWordsRepository bannedWordsRepository,
        IStatService statService, IOptions<ServerOptions> serverOptions,
        INpcSpawnService npcSpawnService, INpcDialogService npcDialogService,
        IMonsterSpawnService monsterSpawnService,
        ICombatService combatService, ILevelingService levelingService, ISkillService skillService,
        IEquipmentService equipmentService, IInventoryService inventoryService,
        IGroundItemService groundItemService, ISkillCastService buffService,
        IFieldPropService fieldPropService, IItemUseService itemUseService,
        IWorldLocationService worldLocationService,
        IResurrectionService resurrectionService,
        IEventAreaService eventAreaService,
        ICraftingSocleService craftingSocleService,
        IStorageService storageService,
        IQuestService questService,
        IGmCommandService gmCommandService,
        Navislamia.Game.Services.Pets.IPetSummonService petSummonService,
        IBoothWatchService boothWatchService)
    {
        PetSummonService = petSummonService;
        _logger = logger;
        CharacterService = characterService;
        BannedWordsRepository = bannedWordsRepository;
        StatService = statService;
        NpcSpawnService = npcSpawnService;
        NpcDialogService = npcDialogService;
        MonsterSpawnService = monsterSpawnService;
        CombatService = combatService;
        LevelingService = levelingService;
        SkillService = skillService;
        EquipmentService = equipmentService;
        InventoryService = inventoryService;
        ItemUseService = itemUseService;
        StorageService = storageService;
        QuestService = questService;
        GmCommandService = gmCommandService;
        GroundItemService = groundItemService;
        CraftingSocleService = craftingSocleService;
        BoothWatchService = boothWatchService;
        FieldPropService = fieldPropService;
        SkillCastService = buffService;
        EventAreaService = eventAreaService;
        ResurrectionService = resurrectionService;
        WorldLocationService = worldLocationService;
        NetworkOptions = networkOptions.Value;
        ServerOptions = serverOptions.Value;
    }

    public bool IsReady()
    {
        return AuthClient.Ready && UploadClient.Ready;
    }

    public void SendMessageToAuth(IPacket packet)
    {
        AuthClient.SendMessage(packet);
    }

    public void SendMessageToUpload(IPacket packet)
    {
        UploadClient.SendMessage(packet);
    }

    public void CreateAuthClient()
    {
        if (AuthClient != null)
        {
            _logger.LogWarning("AuthClient already exists. Skipping creation");
            return;
        }

        AuthClient = new AuthClient(this);
    }

    public void CreateUploadClient()
    {
        if (UploadClient != null)
        {
            _logger.LogWarning("Client already exists. Skipping creation");
            return;
        }

        UploadClient = new UploadClient(this);
    }

    public GameClient CreateGameClient(Socket socket)
    {
        var client = new GameClient(socket, this);

        client.CreateClientConnection();

        return client;
    }

}
