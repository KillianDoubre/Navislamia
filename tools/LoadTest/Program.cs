using System.Diagnostics;
using Navislamia.LoadTest;

LoadOptions options;
try
{
    options = LoadOptions.Parse(args);
}
catch (Exception exception) when (exception is ArgumentException or FormatException)
{
    Console.WriteLine(exception.Message);
    return 2;
}

switch (options.Command)
{
    case "seed":
        return await Seeder.RunAsync(options);
    case "run":
        return await RunAsync(options);
    default:
        Console.WriteLine("""
            Test de charge Navislamia : des clients sans affichage qui se connectent, marchent, parlent et combattent.

              dotnet run --project tools/LoadTest -c Release -- seed [--count 100] [--prefix load] [--password load]
                  crée les comptes load001…load100 dans la base auth (une fois).

              dotnet run --project tools/LoadTest -c Release -- run [--stages 50,100] [--hold 120] [--ramp 5]
                  monte à 50 clients, tient 120 s, monte à 100, tient 120 s, puis écrit le rapport.

            Options de run : --auth ip:port, --game ip:port, --auth-key, --game-key, --server-index n,
              --prefix, --password, --no-combat, --move-interval s, --chat-interval s, --walk-radius u,
              --hunt-range u, --attack-seconds s, --server-process DevConsole | --server-pid n, --report fichier.md
            AuthServer et DevConsole doivent tourner ; les adresses et les clés XRC4 sont lues dans leurs appsettings.
            """);
        return options.Command == "help" ? 0 : 2;
}

static async Task<int> RunAsync(LoadOptions options)
{
    var templates = await CharacterTemplates.LoadAsync(options.TelecasterDatabase, options.Prefix);
    Console.WriteLine(templates.FromDatabase
        ? $"Apparence copiée de {templates.Count} personnage(s) de Telecaster."
        : "Apparence par défaut (aucun personnage dans Telecaster).");

    using var probe = ServerProbe.Attach(options);
    if (probe is not null) Console.WriteLine($"Sonde attachée à {probe.ProcessDescription}.");

    var metrics = new RunMetrics();
    var stop = new CancellationTokenSource();
    Console.CancelKeyPress += (_, e) =>
    {
        e.Cancel = true;
        stop.Cancel();
    };

    var bots = new List<Task>();
    var results = new List<StageResult>();
    var started = DateTime.Now;
    var totals = new CounterSnapshot(metrics);

    foreach (var target in options.Stages)
    {
        if (stop.IsCancellationRequested) break;
        var stageStart = Stopwatch.GetTimestamp();
        Console.WriteLine($"— Étape : {target} clients (montée à {options.RampPerSecond} client/s, puis {options.HoldSeconds} s)");

        while (bots.Count < target && !stop.IsCancellationRequested)
        {
            var bot = new Bot(bots.Count + 1, options, templates, metrics);
            bots.Add(Task.Run(() => bot.RunAsync(stop.Token)));
            await Task.Delay(TimeSpan.FromSeconds(1 / options.RampPerSecond));
        }

        var holdEnd = Stopwatch.GetTimestamp() + (long)(options.HoldSeconds * Stopwatch.Frequency);
        var lastLine = 0L;
        while (Stopwatch.GetTimestamp() < holdEnd && !stop.IsCancellationRequested)
        {
            await Task.Delay(500);
            if (Stopwatch.GetElapsedTime(lastLine).TotalSeconds < 5) continue;
            lastLine = Stopwatch.GetTimestamp();
            PrintLive(target, metrics, probe, Stopwatch.GetElapsedTime(stageStart).TotalSeconds);
        }

        var seconds = Stopwatch.GetElapsedTime(stageStart).TotalSeconds;
        results.Add(new StageResult(target, seconds, metrics.InWorld, metrics.Connecting,
            metrics.AuthLogin.Drain(), metrics.WorldEntry.Drain(), metrics.MoveEcho.Drain(), metrics.ChatEcho.Drain(),
            metrics.AttackFirstSwing.Drain(), totals.Delta(), TakeProblems(metrics), probe?.Take()));
        Console.WriteLine($"  fin d'étape : {metrics.InWorld}/{target} en jeu.");
    }

    stop.Cancel();
    await Task.WhenAny(Task.WhenAll(bots), Task.Delay(TimeSpan.FromSeconds(15)));

    var path = options.ReportPath ?? $"loadtest-{started:yyyyMMdd-HHmm}.md";
    await File.WriteAllTextAsync(path, Report.Write(options, started, probe, results));
    Console.WriteLine($"Rapport : {Path.GetFullPath(path)}");
    return 0;
}

static Dictionary<string, long> TakeProblems(RunMetrics metrics)
{
    var taken = new Dictionary<string, long>();
    foreach (var key in metrics.Problems.Keys.ToList())
    {
        if (metrics.Problems.TryRemove(key, out var count)) taken[key] = count;
    }

    return taken;
}

static void PrintLive(int target, RunMetrics metrics, ServerProbe? probe, double elapsed)
{
    var move = metrics.MoveEcho.Peek();
    var chat = metrics.ChatEcho.Peek();
    var server = probe?.Live();
    var line = $"  [{elapsed,4:0} s] en jeu {metrics.InWorld}/{target} (+{metrics.Connecting} en connexion)" +
               $" | marche p95 {(move.Count > 0 ? $"{move.P95:0} ms" : "—")} | chat p95 {(chat.Count > 0 ? $"{chat.P95:0} ms" : "—")}" +
               $" | problèmes {metrics.Problems.Values.Sum()}";
    if (server is { } s)
    {
        line += $" | serveur CPU {s.Cpu:0} % · {s.WorkingSetMb:0} Mo · attente verrous {s.ContentionMsPerSecond:0} ms/s";
    }

    Console.WriteLine(line);
}

public sealed record StageResult(int Target, double Seconds, int InWorld, int Connecting,
    Summary AuthLogin, Summary WorldEntry, Summary MoveEcho, Summary ChatEcho, Summary AttackFirstSwing,
    CounterDelta Counters, Dictionary<string, long> Problems, StageServerStats? Server);

public sealed record CounterDelta(long FramesSent, long FramesReceived, long BytesReceived, long MovesSent,
    long MovesLost, long ChatsSent, long AttacksSent, long SwingsLanded, long Kills, long Deaths);

/// <summary>Turns the run's running counters into per-stage amounts.</summary>
public sealed class CounterSnapshot
{
    private readonly RunMetrics _metrics;
    private CounterDelta _last = new(0, 0, 0, 0, 0, 0, 0, 0, 0, 0);

    public CounterSnapshot(RunMetrics metrics) => _metrics = metrics;

    public CounterDelta Delta()
    {
        var now = new CounterDelta(_metrics.FramesSent.Value, _metrics.FramesReceived.Value, _metrics.BytesReceived.Value,
            _metrics.MovesSent.Value, _metrics.MovesLost.Value, _metrics.ChatsSent.Value, _metrics.AttacksSent.Value,
            _metrics.SwingsLanded.Value, _metrics.Kills.Value, _metrics.Deaths.Value);
        var delta = new CounterDelta(now.FramesSent - _last.FramesSent, now.FramesReceived - _last.FramesReceived,
            now.BytesReceived - _last.BytesReceived, now.MovesSent - _last.MovesSent, now.MovesLost - _last.MovesLost,
            now.ChatsSent - _last.ChatsSent, now.AttacksSent - _last.AttacksSent, now.SwingsLanded - _last.SwingsLanded,
            now.Kills - _last.Kills,
            now.Deaths - _last.Deaths);
        _last = now;
        return delta;
    }
}
