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
using Navislamia.Game.Services;
using Navislamia.Game.Services.Interfaces;
using Navislamia.Game.Services.Props;
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
        services.Configure<UploadOptions>(context.Configuration.GetSection("Network:Upload"));
        services.Configure<ScriptOptions>(context.Configuration.GetSection("Script"));
        services.Configure<MapOptions>(context.Configuration.GetSection("Map"));
        services.Configure<ServerOptions>(context.Configuration.GetSection("Server"));
        ConfigureMonsterSpawns(services, context);
        ConfigureNpcDialogs(services, context);
        ConfigureSkillCatalog(services, context);
        ConfigureMonsterDrops(services, context);
        ConfigureFieldProps(services, context);
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

    private static void ConfigureMonsterSpawns(IServiceCollection services, HostBuilderContext context)
    {
        var catalogPath = Path.Combine(context.HostingEnvironment.ContentRootPath, "monster-spawns.73.json");

        if (!File.Exists(catalogPath))
        {
            services.Configure<MonsterSpawnOptions>(context.Configuration.GetSection("MonsterSpawns"));
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
        services.AddSingleton<ICharacterRepository, CharacterRepository>();
        services.AddSingleton<IStarterItemsRepository, StarterItemsRepository>();
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
        services.AddSingleton<ISkillCastService, SkillCastService>();
        services.AddSingleton<INpcResourceRepository, NpcResourceRepository>();
        services.AddSingleton<INpcSpawnService, NpcSpawnService>();
        services.AddSingleton<INpcDialogService, NpcDialogService>();
        services.AddSingleton<IMonsterResourceRepository, MonsterResourceRepository>();
        services.AddSingleton<ILevelResourceRepository, LevelResourceRepository>();
        services.AddSingleton<ILevelingService, LevelingService>();
        services.AddSingleton<SkillCatalog>();
        services.AddSingleton<ISkillService, SkillService>();
        services.AddSingleton<IEquipmentService, EquipmentService>();
        services.AddSingleton<IItemResourceRepository, ItemResourceRepository>();
        services.AddSingleton<IItemSortCatalog, ItemSortCatalog>();
        services.AddSingleton<IInventoryService, InventoryService>();
        services.AddSingleton<IMonsterDropCatalog, MonsterDropCatalog>();
        services.AddSingleton<IGroundItemService, GroundItemService>();
        services.AddSingleton<MonsterWorldState>();
        services.AddSingleton<IMonsterSpawnService, MonsterSpawnService>();
        services.AddSingleton<ICombatService, CombatService>();
        services.AddSingleton<IFieldPropCatalog, FieldPropCatalog>();
        services.AddSingleton<IFieldPropService, FieldPropService>();
        services.AddSingleton<IWarpService, WarpService>();

        services.AddSingleton<IScriptService, ScriptService>();
        services.AddSingleton<IMapService, MapService>();
        services.AddSingleton<NetworkService>();
        services.AddSingleton<INetworkService>(provider => provider.GetRequiredService<NetworkService>());
        services.AddSingleton<MonsterMovementService>();
        services.AddSingleton<MonsterAiService>();
        services.AddSingleton<ICharacterService, CharacterService>();
        services.AddSingleton<IBannedWordsRepository, BannedWordsRepository>();
        services.AddSingleton<IStatService, StatService>();
    }

    private static void ConfigureDataAccess(IServiceCollection services)
    {
        services.AddDbContextPool<ArcadiaContext>((serviceProvider, builder) =>
        {
            var config = serviceProvider.GetService<IConfiguration>();
            var dbOptions = config.GetSection("Database").Get<DatabaseOptions>();
            dbOptions.InitialCatalog = "Arcadia";

            builder
                .UseNpgsql(dbOptions.ConnectionString(), options => options.EnableRetryOnFailure());
        });

        services.AddDbContextPool<TelecasterContext>((serviceProvider, builder) =>
        {
            var config = serviceProvider.GetService<IConfiguration>();
            var dbOptions = config.GetSection("Database").Get<DatabaseOptions>();
            dbOptions.InitialCatalog = "Telecaster";

            builder
                .UseNpgsql(dbOptions.ConnectionString(), options => options.EnableRetryOnFailure());
        });
    }
}

