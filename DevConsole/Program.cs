using System.Threading.Tasks;
using System.IO;
using System.Text.Json;
using DevConsole.Properties;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Navislamia.Configuration.Options;
using Navislamia.Game;
using Navislamia.Game.DataAccess.Contexts;
using Navislamia.Game.DataAccess.Extensions;
using Navislamia.Game.DataAccess.Repositories;
using Navislamia.Game.DataAccess.Repositories.Interfaces;
using Navislamia.Game.Services.Buffs;
using Navislamia.Game.Services.Stats;
using Navislamia.Game.Maps;
using Navislamia.Game.Network;
using Navislamia.Game.Network.Interfaces;
using Navislamia.Game.Scripting;
using Navislamia.Game.Services.MonsterSkills;
using Navislamia.Game.Services;
using Navislamia.Game.Services.Interfaces;
using Navislamia.Game.Services.GmCommands;
using Navislamia.Game.Services.Props;
using Navislamia.Game.Services.Pets;
using Navislamia.Game.Services.Rates;
using Serilog;
using Serilog.Exceptions;

namespace DevConsole;

public class Program
{
    public static async Task Main(string[] args)
    {
        var host = CreateHostBuilder(args).Build();

        Log.Logger.Information($"\n{Resources.arcadia}");
        Log.Logger.Information("Navislamia starting...");

        var scopeFactory = host.Services.GetRequiredService<IServiceScopeFactory>();
        using (var scope = scopeFactory.CreateScope())
        {
            var arcadia = scope.ServiceProvider.GetRequiredService<ArcadiaContext>();
            var telecaster = scope.ServiceProvider.GetRequiredService<TelecasterContext>();
            await arcadia.Database.MigrateAsync();
            await telecaster.Database.MigrateAsync();

            Log.Logger.Verbose("Applied Arcadia migrations: {Migrations}\n", await arcadia.Database.GetAppliedMigrationsAsync());
            Log.Logger.Verbose("Applied Telecaster migrations: {Migrations}\n", await telecaster.Database.GetAppliedMigrationsAsync());
        }

        host.Services.GetRequiredService<MonsterMovementService>();
        host.Services.GetRequiredService<MonsterAiService>();
        host.Services.GetRequiredService<ISkillCastService>();
        host.Services.GetRequiredService<RateEventTicker>();
        host.Services.GetRequiredService<PetBehaviorService>();

        await host.RunAsync();
        await Log.CloseAndFlushAsync();
    }

    private static IHostBuilder CreateHostBuilder(string[] args)
    {
        return Host.CreateDefaultBuilder(args)
            .ConfigureAppConfiguration((context, configuration) =>
            {
                var env = context.HostingEnvironment.EnvironmentName;
                configuration.AddJsonFile("appsettings.json", optional: false, reloadOnChange: true);
                configuration.AddJsonFile($"appsettings.{env}.json", optional: true, reloadOnChange: true);
                configuration.AddEnvironmentVariables();
            })
            .ConfigureServices((context, services) =>
            {
                services.AddHostedService<Application>();
                ConfigureOptions(services, context);
                ConfigureServices(services);
                ConfigureDataAccess(services);
            })
            .UseSerilog((context, configuration) =>
            {
                configuration.ReadFrom.Configuration(context.Configuration)
                    .Enrich.With(new SourceContextEnricher())
                    .Enrich.WithExceptionDetails();
            });
    }

    private static void ConfigureOptions(IServiceCollection services, HostBuilderContext context)
    {
        services.Configure<DatabaseOptions>(context.Configuration.GetSection("Database"));
        services.Configure<NetworkOptions>(context.Configuration.GetSection("Network"));
        services.Configure<AuthOptions>(context.Configuration.GetSection("Network:Auth"));
        services.Configure<GameOptions>(context.Configuration.GetSection("Network:Game"));
        services.Configure<GameRuleOptions>(context.Configuration.GetSection("GameRules"));
        services.Configure<DungeonOptions>(context.Configuration.GetSection("Dungeons"));
        services.Configure<UploadOptions>(context.Configuration.GetSection("Network:Upload"));
        services.Configure<ScriptOptions>(context.Configuration.GetSection("Script"));
        services.Configure<MapOptions>(context.Configuration.GetSection("Map"));
        services.Configure<ServerOptions>(context.Configuration.GetSection("Server"));
        ConfigureRates(services, context);
        ConfigureMonsterSpawns(services, context);
        ConfigureNpcDialogs(services, context);
        ConfigureSkillCatalog(services, context);
        ConfigureMonsterDrops(services, context);
        ConfigureMonsterSkills(services, context);
        ConfigureFieldProps(services, context);
        ConfigureMarketCatalog(services, context);
        ConfigurePetCatalog(services, context);
        ConfigureJobLevelCosts(services, context);
        ConfigureCreatureCatalog(services, context);
        ConfigureHuntaholicCatalog(services, context);
        ConfigureAuctionCatalog(services, context);
    }

    /// <summary>
    /// The auction catalogue (<c>auction-catalog.73.json</c>, <c>tools/export_auction_catalog.py</c>): categories and
    /// item names for the search. Without it every item is uncategorised and the keyword search finds nothing.
    /// </summary>
    private static void ConfigureAuctionCatalog(IServiceCollection services, HostBuilderContext context)
    {
        var catalogPath = Path.Combine(context.HostingEnvironment.ContentRootPath, "auction-catalog.73.json");
        if (!File.Exists(catalogPath))
        {
            services.Configure<AuctionCatalogOptions>(_ => { });
            return;
        }

        using var stream = File.OpenRead(catalogPath);
        using var document = JsonDocument.Parse(stream);
        var catalog = document.RootElement.GetProperty("AuctionCatalog")
            .Deserialize<AuctionCatalogOptions>() ?? new AuctionCatalogOptions();
        services.Configure<AuctionCatalogOptions>(options =>
        {
            options.Categories = catalog.Categories;
            options.Items = catalog.Items;
        });
    }

    /// <summary>
    /// The HuntaHolic catalogue (<c>huntaholic-catalog.73.json</c>, <c>tools/export_huntaholic_catalog.py</c>): Bear
    /// Road's lobby, dungeon, tiers and respawns. Without it there is no HuntaHolic.
    /// </summary>
    private static void ConfigureHuntaholicCatalog(IServiceCollection services, HostBuilderContext context)
    {
        var catalogPath = Path.Combine(context.HostingEnvironment.ContentRootPath, "huntaholic-catalog.73.json");
        if (!File.Exists(catalogPath))
        {
            services.Configure<HuntaholicCatalogOptions>(_ => { });
            return;
        }

        using var stream = File.OpenRead(catalogPath);
        using var document = JsonDocument.Parse(stream);
        var catalog = document.RootElement.GetProperty("HuntaholicCatalog")
            .Deserialize<HuntaholicCatalogOptions>() ?? new HuntaholicCatalogOptions();
        services.Configure<HuntaholicCatalogOptions>(options => options.Huntaholics = catalog.Huntaholics);
    }

    /// <summary>
    /// The pets the 7.3 client knows (<c>tools/export_pet_catalog.py</c> from its <c>db_pet.rdb</c>). Without
    /// the file, no cage calls a pet and using one is only acknowledged.
    /// </summary>
    /// <summary>
    /// The JP cost of a job level at each job depth (<c>job-level-costs.73.json</c>, 64-bit because the
    /// master-class tier overflows the database's <c>integer[]</c>). Without the file only the base tier works,
    /// from the database.
    /// </summary>
    private static void ConfigureJobLevelCosts(IServiceCollection services, HostBuilderContext context)
    {
        var catalogPath = Path.Combine(context.HostingEnvironment.ContentRootPath, "job-level-costs.73.json");
        if (!File.Exists(catalogPath))
        {
            services.Configure<JobLevelCostOptions>(_ => { });
            return;
        }

        using var stream = File.OpenRead(catalogPath);
        using var document = JsonDocument.Parse(stream);
        var catalog = document.RootElement.GetProperty("JobLevelCosts")
            .Deserialize<JobLevelCostOptions>() ?? new JobLevelCostOptions();

        services.Configure<JobLevelCostOptions>(options => options.Depths = catalog.Depths);
    }

    /// <summary>
    /// The creature catalogue (<c>creature-catalog.73.json</c>, <c>tools/export_creature_catalog.py</c>): summons with
    /// their stats, name parts, tamable monster names. Without it nothing can be tamed nor summoned.
    /// </summary>
    private static void ConfigureCreatureCatalog(IServiceCollection services, HostBuilderContext context)
    {
        var catalogPath = Path.Combine(context.HostingEnvironment.ContentRootPath, "creature-catalog.73.json");
        if (!File.Exists(catalogPath))
        {
            services.Configure<CreatureCatalogOptions>(_ => { });
            return;
        }

        using var stream = File.OpenRead(catalogPath);
        using var document = JsonDocument.Parse(stream);
        var catalog = document.RootElement.GetProperty("CreatureCatalog")
            .Deserialize<CreatureCatalogOptions>() ?? new CreatureCatalogOptions();

        services.Configure<CreatureCatalogOptions>(options =>
        {
            options.Summons = catalog.Summons;
            options.NamePrefixes = catalog.NamePrefixes;
            options.NamePostfixes = catalog.NamePostfixes;
            options.TamableMonsterNames = catalog.TamableMonsterNames;
        });
    }

    private static void ConfigurePetCatalog(IServiceCollection services, HostBuilderContext context)
    {
        var catalogPath = Path.Combine(context.HostingEnvironment.ContentRootPath, "pet-catalog.73.json");
        if (!File.Exists(catalogPath))
        {
            services.Configure<PetCatalogOptions>(_ => { });
            return;
        }

        using var stream = File.OpenRead(catalogPath);
        using var document = JsonDocument.Parse(stream);
        var catalog = document.RootElement.GetProperty("PetCatalog")
            .Deserialize<PetCatalogOptions>() ?? new PetCatalogOptions();

        services.Configure<PetCatalogOptions>(options => options.Pets = catalog.Pets);
    }

    /// <summary>
    /// The <c>Rates</c> section of the environment's settings file, read through <c>IOptionsMonitor</c>: both
    /// settings files are loaded with <c>reloadOnChange</c>, so an edit applies without a restart. The event
    /// state file is resolved against the content root, like the catalogues.
    /// </summary>
    private static void ConfigureRates(IServiceCollection services, HostBuilderContext context)
    {
        var contentRoot = context.HostingEnvironment.ContentRootPath;
        services.Configure<RatesOptions>(context.Configuration.GetSection("Rates"));
        services.Configure<CraftingOptions>(context.Configuration.GetSection("Crafting"));
        services.PostConfigure<RatesOptions>(options =>
        {
            if (!string.IsNullOrWhiteSpace(options.EventStatePath) && !Path.IsPathRooted(options.EventStatePath))
            {
                options.EventStatePath = Path.Combine(contentRoot, options.EventStatePath);
            }
        });
    }

    /// <summary>
    /// Read directly with System.Text.Json like the spawn and drop catalogs: flattening these arrays
    /// through the configuration provider costs tens of seconds at startup.
    /// </summary>
    private static void ConfigureFieldProps(IServiceCollection services, HostBuilderContext context)
    {
        var catalogPath = Path.Combine(context.HostingEnvironment.ContentRootPath, "field-props.73.json");
        if (!File.Exists(catalogPath))
        {
            services.Configure<FieldPropOptions>(_ => { });
            return;
        }

        using var stream = File.OpenRead(catalogPath);
        using var document = JsonDocument.Parse(stream);
        var catalog = document.RootElement.GetProperty("FieldPropCatalog")
            .Deserialize<FieldPropOptions>() ?? new FieldPropOptions();

        services.Configure<FieldPropOptions>(options =>
        {
            options.Templates = catalog.Templates;
            options.Spawns = catalog.Spawns;
            options.Dungeons = catalog.Dungeons;
        });
    }

    private static void ConfigureMonsterDrops(IServiceCollection services, HostBuilderContext context)
    {
        var catalogPath = Path.Combine(context.HostingEnvironment.ContentRootPath, "monster-drops.73.json");
        if (!File.Exists(catalogPath))
        {
            services.Configure<MonsterDropOptions>(_ => { });
            return;
        }

        using var stream = File.OpenRead(catalogPath);
        using var document = JsonDocument.Parse(stream);
        var catalog = document.RootElement.GetProperty("MonsterDropCatalog")
            .Deserialize<MonsterDropOptions>() ?? new MonsterDropOptions();

        services.Configure<MonsterDropOptions>(options =>
        {
            options.Tables = catalog.Tables;
            options.Groups = catalog.Groups;
            options.Monsters = catalog.Monsters;
        });
    }

    private static void ConfigureMonsterSkills(IServiceCollection services, HostBuilderContext context)
    {
        var catalogPath = Path.Combine(context.HostingEnvironment.ContentRootPath, "monster-skills.73.json");
        if (!File.Exists(catalogPath))
        {
            services.Configure<MonsterSkillOptions>(_ => { });
            return;
        }

        using var stream = File.OpenRead(catalogPath);
        using var document = JsonDocument.Parse(stream);
        var catalog = document.RootElement.GetProperty("MonsterSkillCatalog")
            .Deserialize<MonsterSkillOptions>() ?? new MonsterSkillOptions();

        services.Configure<MonsterSkillOptions>(options => options.Links = catalog.Links);
    }

    private static void ConfigureMonsterSpawns(IServiceCollection services, HostBuilderContext context)
    {
        services.Configure<MonsterSpawnOptions>(context.Configuration.GetSection("MonsterSpawns"));
        var catalogPath = Path.Combine(context.HostingEnvironment.ContentRootPath, "monster-spawns.73.json");

        if (!File.Exists(catalogPath))
        {
            return;
        }

        using var stream = File.OpenRead(catalogPath);
        using var document = JsonDocument.Parse(stream);
        var catalog = document.RootElement.GetProperty("MonsterSpawnCatalog")
            .Deserialize<MonsterSpawnOptions>() ?? new MonsterSpawnOptions();

        services.Configure<MonsterSpawnOptions>(options =>
        {
            options.Spawns = catalog.Spawns;
            options.Areas = catalog.Areas;
        });
    }

    private static void ConfigureNpcDialogs(IServiceCollection services, HostBuilderContext context)
    {
        var catalogPath = Path.Combine(context.HostingEnvironment.ContentRootPath, "npc-dialogs.73.json");
        if (!File.Exists(catalogPath))
        {
            services.Configure<NpcDialogOptions>(_ => { });
            return;
        }

        using var stream = File.OpenRead(catalogPath);
        using var document = JsonDocument.Parse(stream);
        var catalog = document.RootElement.GetProperty("NpcDialogCatalog")
            .Deserialize<NpcDialogOptions>() ?? new NpcDialogOptions();

        services.Configure<NpcDialogOptions>(options =>
        {
            options.Npcs = catalog.Npcs;
            options.Dialogs = catalog.Dialogs;
        });
    }

    /// <summary>
    /// The merchant catalogue. Like the dialog catalogue it is a versioned JSON export rather than the
    /// reference's SQL Server MarketResource table; the file may legitimately hold no row (no export
    /// available yet), in which case every merchant refuses to open instead of showing an empty window.
    /// </summary>
    private static void ConfigureMarketCatalog(IServiceCollection services, HostBuilderContext context)
    {
        var catalogPath = Path.Combine(context.HostingEnvironment.ContentRootPath, "market-catalog.73.json");
        if (!File.Exists(catalogPath))
        {
            services.Configure<MarketCatalogOptions>(_ => { });
            return;
        }

        using var stream = File.OpenRead(catalogPath);
        using var document = JsonDocument.Parse(stream);
        var catalog = document.RootElement.GetProperty("MarketCatalog")
            .Deserialize<MarketCatalogOptions>() ?? new MarketCatalogOptions();

        services.Configure<MarketCatalogOptions>(options => options.Markets = catalog.Markets);
    }

    private static void ConfigureSkillCatalog(IServiceCollection services, HostBuilderContext context)
    {
        var catalogPath = Path.Combine(context.HostingEnvironment.ContentRootPath, "skill-catalog.73.json");
        if (!File.Exists(catalogPath))
        {
            services.Configure<SkillCatalogOptions>(_ => { });
            return;
        }

        using var stream = File.OpenRead(catalogPath);
        using var document = JsonDocument.Parse(stream);
        var catalog = document.RootElement.GetProperty("SkillCatalog")
            .Deserialize<SkillCatalogOptions>() ?? new SkillCatalogOptions();

        services.Configure<SkillCatalogOptions>(options => options.Jobs = catalog.Jobs);
    }

    private static void ConfigureServices(IServiceCollection services)
    {
        services.AddSingleton<IGameModule, GameModule>();
        services.AddSingleton<IWorldRepository, WorldRepository>();
        services.AddSingleton<ICharacterRepositoryFactory, CharacterRepositoryFactory>();
        services.AddSingleton<CharacterGate>();
        services.AddSingleton<IStarterItemsRepository, StarterItemsRepository>();
        services.AddSingleton<IStorageRepository, StorageRepository>();
        services.AddSingleton<IPaidItemRepository, PaidItemRepository>();
        services.AddSingleton<IStatResourceRepository, StatResourceRepository>();
        services.AddSingleton<IJobResourceRepository, JobResourceRepository>();
        services.AddSingleton<IJobLevelBonusRepository, JobLevelBonusRepository>();
        services.AddSingleton<IStatCatalog, StatCatalog>();
        services.AddSingleton<IItemStatCatalog, ItemStatCatalog>();
        services.AddSingleton<ISkillResourceRepository, SkillResourceRepository>();
        services.AddSingleton<ISkillPassiveCatalog, SkillPassiveCatalog>();
        services.AddSingleton<IStateResourceRepository, StateResourceRepository>();
        services.AddSingleton<IStateCatalog, StateCatalog>();
        services.AddSingleton<IBuffCatalog, BuffCatalog>();
        services.AddSingleton<Navislamia.Game.Services.Casting.CastInterrupts>();
        services.AddSingleton<Navislamia.Game.Services.Casting.ICastInterrupts>(provider =>
            provider.GetRequiredService<Navislamia.Game.Services.Casting.CastInterrupts>());
        services.AddSingleton<ISkillCastService, SkillCastService>();
        services.AddSingleton<IBuffPersistence, BuffPersistence>();
        services.AddSingleton<INpcResourceRepository, NpcResourceRepository>();
        services.AddSingleton<INpcSpawnService, NpcSpawnService>();
        services.AddSingleton<Navislamia.Game.Services.Jobs.IJobChangeService,
            Navislamia.Game.Services.Jobs.JobChangeService>();
        services.AddSingleton<Navislamia.Game.Services.Creatures.ICreatureDialogService,
            Navislamia.Game.Services.Creatures.CreatureDialogService>();
        services.AddSingleton<INpcDialogService, NpcDialogService>();
        services.AddSingleton<IPkModeService, PkModeService>();
        services.AddSingleton<IMarketCatalog, MarketCatalog>();
        services.AddSingleton<IMarketService, MarketService>();
        services.AddSingleton<IMarketTradeService, MarketTradeService>();
        services.AddSingleton<IMonsterResourceRepository, MonsterResourceRepository>();
        services.AddSingleton<ILevelResourceRepository, LevelResourceRepository>();
        services.AddSingleton<IAuctionCateryResourceRepository, AuctionCateryResourceRepository>();
        services.AddSingleton<IQuestCatalogueRepository, QuestCatalogueRepository>();
        services.AddSingleton<IWorldLocationRepository, WorldLocationRepository>();
        services.AddSingleton<IWorldLocationService, WorldLocationService>();
        services.AddSingleton<Navislamia.Game.Services.ReturnPoints.IReturnPointService>(provider =>
            new Navislamia.Game.Services.ReturnPoints.ReturnPointService(provider.GetRequiredService<ICharacterService>()));
        services.AddSingleton<ILevelingService, LevelingService>();
        services.AddSingleton<SkillCatalog>();
        services.AddSingleton<ISkillService, SkillService>();
        services.AddSingleton<IEquipmentService, EquipmentService>();
        services.AddSingleton<IItemResourceRepository, ItemResourceRepository>();
        services.AddSingleton<IMixResourceRepository, MixResourceRepository>();
        services.AddSingleton<IEnhanceResourceRepository, EnhanceResourceRepository>();
        services.AddSingleton<IMixResourceCatalog, MixResourceCatalog>();
        services.AddSingleton<IEnhanceResourceCatalog, EnhanceResourceCatalog>();
        services.AddSingleton<IItemMatchCatalog, ItemMatchCatalog>();
        services.AddSingleton<IItemGroupCatalog, ItemGroupCatalog>();
        services.AddSingleton<IItemSortCatalog, ItemSortCatalog>();
        services.AddSingleton<IInventoryService, InventoryService>();
        services.AddSingleton<IStorageService, StorageService>();
        services.AddSingleton<ICommercialStorageService, CommercialStorageService>();
        services.AddSingleton<IItemUseCatalog>(provider => new ItemUseCatalog(
            provider.GetRequiredService<IItemResourceRepository>(),
            Path.Combine(System.AppContext.BaseDirectory, "item-use.73.json")));
        services.AddSingleton<IItemWearCatalog, ItemWearCatalog>();
        services.AddSingleton<IEtherealSacrificeCatalog, EtherealSacrificeCatalog>();
        services.AddSingleton<IItemUseService, ItemUseService>();
        services.AddSingleton<ICardSocketCatalog, CardSocketCatalog>();
        services.AddSingleton<ICardSocketService, CardSocketService>();
        services.AddSingleton<ISkillCardService, SkillCardService>();
        services.AddSingleton<IItemDonateService, ItemDonateService>();
        services.AddSingleton<IItemSellCatalog, ItemSellCatalog>();
        services.AddSingleton<IMarketSellService, MarketSellService>();
        services.AddSingleton<IPetCatalog, PetCatalog>();
        services.AddSingleton<PetWorldService>();
        services.AddSingleton<IPetSummonService, PetSummonService>();
        services.AddSingleton<PetBehaviorService>();
        services.AddSingleton<IQuestService, QuestService>();
        services.AddSingleton<IGmCommandService, GmCommandService>();
        services.AddSingleton<IRateService, RateService>();
        services.AddSingleton<RateEventTicker>();
        services.AddSingleton<IMonsterDropCatalog, MonsterDropCatalog>();
        services.AddSingleton<IGroundItemService, GroundItemService>();
        services.AddSingleton<Navislamia.Game.Services.Creatures.ICreatureCatalog,
            Navislamia.Game.Services.Creatures.CreatureCatalog>();
        services.AddSingleton<Navislamia.Game.Services.Creatures.CreatureEvents>();
        services.AddSingleton<Navislamia.Game.Services.Creatures.ICreatureEvents>(provider =>
            provider.GetRequiredService<Navislamia.Game.Services.Creatures.CreatureEvents>());
        services.AddSingleton<SummonWorldService>();
        services.AddSingleton<Navislamia.Game.Services.Creatures.ICreatureService,
            Navislamia.Game.Services.Creatures.CreatureService>();
        services.AddSingleton<Navislamia.Game.Services.Compete.ICompeteService,
            Navislamia.Game.Services.Compete.CompeteService>();
        services.AddSingleton<Navislamia.Game.Services.Death.IDeathDropService,
            Navislamia.Game.Services.Death.DeathDropService>();
        services.AddSingleton<ICraftingSocleService, CraftingSocleService>();
        services.AddSingleton<IBoothWatchService, BoothWatchService>();
        services.AddSingleton<IPlayerVisibilityService, PlayerVisibilityService>();
        services.AddSingleton<PlayerRegenerationService>();
        services.AddSingleton<Navislamia.Game.Services.Weight.IInventoryChangeFeed, Navislamia.Game.Services.Weight.InventoryChangeFeed>();
        services.AddSingleton<Navislamia.Game.Services.Weight.IItemWeightCatalog, Navislamia.Game.Services.Weight.ItemWeightCatalog>();
        services.AddSingleton<Navislamia.Game.Services.Weight.ICarriedWeightService, Navislamia.Game.Services.Weight.CarriedWeightService>();
        services.AddSingleton<Navislamia.Game.Services.Party.IPartyService, Navislamia.Game.Services.Party.PartyService>();
        services.AddSingleton<Navislamia.Game.Services.Trade.IPlayerTradeService, Navislamia.Game.Services.Trade.PlayerTradeService>();
        services.AddSingleton<IBoothTradeService, BoothTradeService>();
        services.AddSingleton<ISoulstoneCraftCatalog, SoulstoneCraftCatalog>();
        services.AddSingleton<ISoulstoneCraftService, SoulstoneCraftService>();
        services.AddSingleton<Navislamia.Game.Maps.Collision.IWorldCollision, Navislamia.Game.Maps.Collision.WorldCollision>();
        services.AddSingleton<MonsterWorldState>();
        services.AddSingleton<IMonsterSpawnService, MonsterSpawnService>();
        services.AddSingleton<ICombatService, CombatService>();
        services.AddSingleton<IPkFieldService, PkFieldService>();
        services.AddSingleton<IFieldPropCatalog, FieldPropCatalog>();
        services.AddSingleton<IFieldPropService, FieldPropService>();
        services.AddSingleton<IWarpService, WarpService>();
        services.AddSingleton<Navislamia.Game.Services.Dungeons.DungeonCatalog>();
        services.AddSingleton<Navislamia.Game.Services.Guilds.GuildRuntime>();
        services.AddSingleton<Navislamia.Game.Services.Guilds.GuildCombatEvents>();
        services.AddSingleton<Navislamia.Game.Services.Guilds.IGuildService, Navislamia.Game.Services.Guilds.GuildService>();
        services.AddSingleton<Navislamia.Game.Services.Dungeons.DungeonRooms>();
        services.AddSingleton<Navislamia.Game.Services.Dungeons.IDungeonGuildRepository, Navislamia.Game.Services.Dungeons.DungeonGuildRepository>();
        services.AddSingleton<Navislamia.Game.Services.Dungeons.IDungeonService, Navislamia.Game.Services.Dungeons.DungeonService>();
        services.AddHostedService<DungeonMaintenanceService>();
        services.AddSingleton<IEventAreaService, EventAreaService>();
        services.AddSingleton<IResurrectionItemCatalog, ResurrectionItemCatalog>();
        services.AddSingleton<IResurrectionService, ResurrectionService>();
        services.AddSingleton<Navislamia.Game.Services.Huntaholic.IHuntaholicCatalog,
            Navislamia.Game.Services.Huntaholic.HuntaholicCatalog>();
        services.AddSingleton<Navislamia.Game.Services.Huntaholic.HuntaholicEvents>();
        services.AddSingleton<Navislamia.Game.Services.Huntaholic.IHuntaholicEvents>(provider =>
            provider.GetRequiredService<Navislamia.Game.Services.Huntaholic.HuntaholicEvents>());
        services.AddSingleton<Navislamia.Game.Services.Huntaholic.IHuntaholicService,
            Navislamia.Game.Services.Huntaholic.HuntaholicService>();
        services.AddSingleton<Navislamia.Game.Services.Auction.IAuctionCatalog, Navislamia.Game.Services.Auction.AuctionCatalog>();
        services.AddSingleton<Navislamia.Game.Services.Auction.IAuctionStore, Navislamia.Game.Services.Auction.AuctionStore>();
        services.AddSingleton<Navislamia.Game.Services.Auction.IAuctionService, Navislamia.Game.Services.Auction.AuctionService>();

        services.AddSingleton<IScriptService, ScriptService>();
        services.AddSingleton<SkillEffectScheduler>();
        services.AddSingleton<IMapService, MapService>();
        services.AddSingleton<NetworkService>();
        services.AddSingleton<INetworkService>(provider => provider.GetRequiredService<NetworkService>());
        services.AddSingleton<MonsterMovementService>();
        services.AddSingleton<IMonsterSkillCatalog, MonsterSkillCatalog>();
        services.AddSingleton<IMonsterSkillService, MonsterSkillService>();
        services.AddSingleton<MonsterAiService>();
        services.AddSingleton<ICharacterService, CharacterService>();
        services.AddSingleton<IBannedWordsRepository, BannedWordsRepository>();
        services.AddSingleton<IStatService, StatService>();
        services.AddSingleton<Navislamia.Game.Services.Progression.TitleCatalog>();
        services.AddSingleton<Navislamia.Game.Services.Progression.ITitleService, Navislamia.Game.Services.Progression.TitleService>();
    }

    private static void ConfigureDataAccess(IServiceCollection services)
    {
        services.AddDbContextPool<ArcadiaContext>((serviceProvider, builder) =>
        {
            var config = serviceProvider.GetService<IConfiguration>();
            var dbOptions = config.GetSection("Database").Get<DatabaseOptions>();
            dbOptions.InitialCatalog = dbOptions.ArcadiaCatalog;

            builder
                .UseNpgsql(dbOptions.ConnectionString(), options => options.EnableRetryOnFailure());
        });

        services.AddDbContextPool<TelecasterContext>((serviceProvider, builder) =>
        {
            var config = serviceProvider.GetService<IConfiguration>();
            var dbOptions = config.GetSection("Database").Get<DatabaseOptions>();
            dbOptions.InitialCatalog = dbOptions.TelecasterCatalog;

            builder
                .UseNpgsql(dbOptions.ConnectionString(), options => options.EnableRetryOnFailure());
        });
    }
}

