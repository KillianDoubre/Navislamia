using System.Globalization;
using Microsoft.Extensions.Configuration;
using Npgsql;

namespace Navislamia.LoadTest;

/// <summary>
/// The run's settings. Addresses and XRC4 keys default to what the repository's own servers read
/// (<c>AuthServer/appsettings.json</c>, <c>DevConsole/appsettings.json</c>), found by walking up to
/// <c>Navislamia.sln</c>; every one can be given on the command line instead.
/// </summary>
public sealed class LoadOptions
{
    public string Command { get; private set; } = "help";

    public string AuthIp { get; private set; } = "127.0.0.1";
    public int AuthPort { get; private set; } = 4601;
    public string AuthKey { get; private set; } = "t@`o{i`n`TQ;<pPsPD";
    public string GameKey { get; private set; } = "t@`o{i`n`TQ;<pPsPD";
    public string? GameIp { get; private set; }
    public int? GamePort { get; private set; }
    public ushort? ServerIndex { get; private set; }

    public string Prefix { get; private set; } = "load";
    public string Password { get; private set; } = "load";
    public int Count { get; private set; } = 100;

    public int[] Stages { get; private set; } = { 50, 100 };
    public int HoldSeconds { get; private set; } = 120;
    public double RampPerSecond { get; private set; } = 5;
    public int Seed { get; private set; } = 7300;

    public bool Combat { get; private set; } = true;
    public double MoveInterval { get; private set; } = 3;
    public double ChatInterval { get; private set; } = 20;
    public float WalkRadius { get; private set; } = 120;
    public float HuntRange { get; private set; } = 250;
    public double AttackSeconds { get; private set; } = 15;

    public string ServerProcess { get; private set; } = "DevConsole";
    public int? ServerPid { get; private set; }
    public string? ReportPath { get; private set; }

    public string? AuthDatabase { get; private set; }
    public string? TelecasterDatabase { get; private set; }

    public static string AccountName(string prefix, int index) => $"{prefix}{index:000}";

    /// <summary>4-18 letters or digits, capitalised like the server's <c>FormatName</c>.</summary>
    public static string CharacterName(string prefix, int index) =>
        char.ToUpperInvariant(prefix[0]) + prefix[1..] + $"{index:000}";

    public static LoadOptions Parse(string[] args)
    {
        var options = new LoadOptions();
        options.ReadRepositoryDefaults();
        if (args.Length == 0) return options;

        options.Command = args[0].ToLowerInvariant();
        for (var i = 1; i < args.Length; i++)
        {
            var name = args[i];
            string Next() => i + 1 < args.Length ? args[++i] : throw new ArgumentException($"{name} attend une valeur");
            double Number() => double.Parse(Next(), CultureInfo.InvariantCulture);

            switch (name)
            {
                case "--auth": (options.AuthIp, options.AuthPort) = Endpoint(Next()); break;
                case "--game": { var (ip, port) = Endpoint(Next()); options.GameIp = ip; options.GamePort = port; break; }
                case "--auth-key": options.AuthKey = Next(); break;
                case "--game-key": options.GameKey = Next(); break;
                case "--server-index": options.ServerIndex = ushort.Parse(Next()); break;
                case "--prefix": options.Prefix = Next(); break;
                case "--password": options.Password = Next(); break;
                case "--count": options.Count = int.Parse(Next()); break;
                case "--clients":
                case "--stages":
                    options.Stages = Next().Split(',').Select(int.Parse).ToArray();
                    break;
                case "--hold": options.HoldSeconds = int.Parse(Next()); break;
                case "--ramp": options.RampPerSecond = Number(); break;
                case "--seed": options.Seed = int.Parse(Next()); break;
                case "--no-combat": options.Combat = false; break;
                case "--move-interval": options.MoveInterval = Number(); break;
                case "--chat-interval": options.ChatInterval = Number(); break;
                case "--walk-radius": options.WalkRadius = (float)Number(); break;
                case "--hunt-range": options.HuntRange = (float)Number(); break;
                case "--attack-seconds": options.AttackSeconds = Number(); break;
                case "--server-process": options.ServerProcess = Next(); break;
                case "--server-pid": options.ServerPid = int.Parse(Next()); break;
                case "--report": options.ReportPath = Next(); break;
                default: throw new ArgumentException($"option inconnue : {name}");
            }
        }

        if (options.Prefix.Length == 0 || !options.Prefix.All(char.IsAsciiLetter) || options.Prefix.Length > 15)
            throw new ArgumentException("--prefix : 1 à 15 lettres ASCII (le nom de personnage est préfixe + 3 chiffres)");
        if (options.Stages.Length == 0 || options.Stages.Any(n => n <= 0))
            throw new ArgumentException("--stages : une liste de nombres de clients positifs, par exemple 50,100");
        return options;
    }

    private static (string, int) Endpoint(string value)
    {
        var parts = value.Split(':');
        return (parts[0], int.Parse(parts[1]));
    }

    private void ReadRepositoryDefaults()
    {
        var root = FindRepository();
        if (root is null) return;

        var auth = Load(Path.Combine(root, "AuthServer", "appsettings.json"));
        if (auth is not null)
        {
            AuthIp = auth["Client:Ip"] ?? AuthIp;
            AuthPort = int.TryParse(auth["Client:Port"], out var port) ? port : AuthPort;
            AuthKey = auth["Network:CipherKey"] ?? AuthKey;
            AuthDatabase = new NpgsqlConnectionStringBuilder
            {
                Host = auth["Database:Host"] ?? "localhost",
                Port = int.TryParse(auth["Database:Port"], out var dbPort) ? dbPort : 5432,
                Username = auth["Database:User"] ?? "postgres",
                Password = auth["Database:Password"] ?? "",
                Database = auth["Database:Name"] ?? "auth"
            }.ConnectionString;
        }

        var game = Load(Path.Combine(root, "DevConsole", "appsettings.json"));
        if (game is not null)
        {
            GameKey = game["Network:CipherKey"] ?? GameKey;
            TelecasterDatabase = new NpgsqlConnectionStringBuilder
            {
                Host = game["Database:DataSource"] ?? "localhost",
                Port = int.TryParse(game["Database:Port"], out var dbPort) ? dbPort : 5432,
                Username = game["Database:User"] ?? "postgres",
                Password = game["Database:Password"] ?? "",
                Database = game["Database:TelecasterCatalog"] ?? "Telecaster"
            }.ConnectionString;
        }
    }

    private static IConfiguration? Load(string path) =>
        File.Exists(path) ? new ConfigurationBuilder().AddJsonFile(path, optional: true).Build() : null;

    private static string? FindRepository()
    {
        foreach (var start in new[] { Directory.GetCurrentDirectory(), AppContext.BaseDirectory })
        {
            for (var dir = new DirectoryInfo(start); dir is not null; dir = dir.Parent)
            {
                if (File.Exists(Path.Combine(dir.FullName, "Navislamia.sln"))) return dir.FullName;
            }
        }

        return null;
    }
}
