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
using Navislamia.Game.Maps;
using Navislamia.Game.Network;
using Navislamia.Game.Network.Interfaces;
using Navislamia.Game.Scripting;
using Navislamia.Game.Services;
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

    private static void ConfigureServices(IServiceCollection services)
    {
        services.AddSingleton<IGameModule, GameModule>();
        services.AddSingleton<IWorldRepository, WorldRepository>();
        services.AddSingleton<ICharacterRepository, CharacterRepository>();
        services.AddSingleton<IStarterItemsRepository, StarterItemsRepository>();
        services.AddSingleton<IStatResourceRepository, StatResourceRepository>();
        services.AddSingleton<INpcResourceRepository, NpcResourceRepository>();
        services.AddSingleton<INpcSpawnService, NpcSpawnService>();
        services.AddSingleton<INpcDialogService, NpcDialogService>();
        services.AddSingleton<IMonsterResourceRepository, MonsterResourceRepository>();
        services.AddSingleton<MonsterWorldState>();
        services.AddSingleton<IMonsterSpawnService, MonsterSpawnService>();
        services.AddSingleton<ICombatService, CombatService>();

        services.AddSingleton<IScriptService, ScriptService>();
        services.AddSingleton<IMapService, MapService>();
        services.AddSingleton<NetworkService>();
        services.AddSingleton<INetworkService>(provider => provider.GetRequiredService<NetworkService>());
        services.AddSingleton<MonsterMovementService>();
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

