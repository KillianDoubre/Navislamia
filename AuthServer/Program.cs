using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Navislamia.AuthServer;
using Navislamia.AuthServer.Accounts;
using Navislamia.AuthServer.GameServers;
using Navislamia.AuthServer.Net;
using Navislamia.AuthServer.Sessions;
using Navislamia.Game.DataAccess.Contexts;
using Navislamia.Game.DataAccess.Repositories;
using Navislamia.Game.Network.Packets.Enums;
using Npgsql;
using Serilog;

Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Debug()
    .WriteTo.Console(outputTemplate:
        "[{Timestamp:HH:mm:ss} {Level:u3}] {Message:lj}{NewLine}{Exception}")
    .CreateLogger();

var config = new ConfigurationBuilder()
    .SetBasePath(AppContext.BaseDirectory)
    .AddJsonFile("appsettings.json", optional: false)
    .Build();

string Get(string key, string fallback) => config[key] ?? fallback;
int GetInt(string key, int fallback) => int.TryParse(config[key], out var v) ? v : fallback;

var cipherKey = Get("Network:CipherKey", "}h79q~B%al;k'y $E");

var registry = new GameServerRegistry();
var keyStore = new OneTimeKeyStore();

IAccountService accountService = new DenyAllAccountService();
try
{
    var connectionString = new NpgsqlConnectionStringBuilder
    {
        Host = Get("Database:Host", "localhost"),
        Port = GetInt("Database:Port", 5432),
        Username = Get("Database:User", "postgres"),
        Password = Get("Database:Password", ""),
        Database = Get("Database:Name", "auth")
    }.ConnectionString;

    var options = new DbContextOptionsBuilder<AuthContext>().UseNpgsql(connectionString).Options;
    var accountRepository = new AccountRepository(options);
    accountRepository.EnsureCreated();

    accountService = new AccountService(accountRepository);

    var seedUser = Get("Seed:Username", "test");
    var seedPass = Get("Seed:Password", "test");
    if (await accountRepository.GetByUsernameAsync(seedUser) is null)
    {
        await accountService.CreateAccountAsync(seedUser, seedPass);
        Log.Information("Seeded dev account '{user}'", seedUser);
    }

    Log.Information("Account database ready");
}
catch (Exception ex)
{
    Log.Error(ex, "Account database unavailable — client logins will be denied");
}

var gameHandler = new GameServerHandler(registry, keyStore, Log.ForContext<GameServerHandler>());
var gameServer = new PacketServer("Game", Get("Auth:Ip", "127.0.0.1"), GetInt("Auth:Port", 4502),
    useCipher: false, cipherKey, () => gameHandler.DispatchAsync, Log.ForContext<PacketServer>());

var clientLogger = Log.ForContext<AuthClientSession>();
var clientServer = new PacketServer("Client", Get("Client:Ip", "127.0.0.1"), GetInt("Client:Port", 4500),
    useCipher: true, cipherKey,
    () => new AuthClientSession(accountService, registry, keyStore, clientLogger).DispatchAsync,
    Log.ForContext<PacketServer>());

var upload = new StubListener("Upload", Get("Upload:Ip", "127.0.0.1"), GetInt("Upload:Port", 4616),
    new Dictionary<ushort, Func<byte[]>>
    {
        [(ushort)UploadPackets.TS_SU_LOGIN] = StubResponses.BuildUploadLoginResult
    },
    Log.ForContext<StubListener>());

try
{
    gameServer.Start();
    clientServer.Start();
    upload.Start();

    Log.Information("Auth server ready. Press Ctrl-C to stop.");
    await Task.Delay(Timeout.Infinite);
}
catch (Exception ex)
{
    Log.Fatal(ex, "Auth server failed to start");
    Environment.ExitCode = 1;
}
finally
{
    Log.CloseAndFlush();
}
