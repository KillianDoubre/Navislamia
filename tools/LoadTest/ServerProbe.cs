using System.Diagnostics;
using System.Diagnostics.Tracing;
using System.Globalization;
using Microsoft.Diagnostics.NETCore.Client;
using Microsoft.Diagnostics.Tracing;
using Microsoft.Diagnostics.Tracing.Parsers;

namespace Navislamia.LoadTest;

/// <summary>
/// Watches the game server while the bots play, when it runs on this machine: the process's CPU and memory, the
/// runtime's counters (lock contention, thread pool, GC) and the server's own <c>Navislamia</c> meter
/// (<see cref="Navislamia.Game.Network.ServerMetrics"/>: tick of each loop, frame handling, visibility passes,
/// send delay), all read over EventPipe, so the server needs no setting. Lock waits come from the runtime's
/// contention events, which carry their duration.
/// </summary>
public sealed class ServerProbe : IDisposable
{
    private readonly Process _process;
    private readonly object _lock = new();
    private readonly EventPipeSession? _session;
    private readonly Task? _reader;
    private readonly CancellationTokenSource _stop = new();
    private readonly Task _sampler;

    private StageServerStats _current = new();

    private ServerProbe(Process process)
    {
        _process = process;
        try
        {
            var client = new DiagnosticsClient(process.Id);
            _session = client.StartEventPipeSession(new[]
            {
                new EventPipeProvider("System.Runtime", EventLevel.Informational, 0,
                    new Dictionary<string, string> { ["EventCounterIntervalSec"] = "1" }),
                new EventPipeProvider("System.Diagnostics.Metrics", EventLevel.Informational, 0x3,
                    new Dictionary<string, string>
                    {
                        ["SessionId"] = Guid.NewGuid().ToString(),
                        ["Metrics"] = Navislamia.Game.Network.ServerMetrics.MeterName,
                        ["RefreshInterval"] = "1",
                        ["MaxTimeSeries"] = "2000",
                        ["MaxHistograms"] = "500",
                    }),
                new EventPipeProvider("Microsoft-Windows-DotNETRuntime", EventLevel.Informational,
                    (long)ClrTraceEventParser.Keywords.Contention),
            }, requestRundown: false);
            _reader = Task.Run(Read);
            EventPipeAvailable = true;
        }
        catch (Exception exception)
        {
            Console.WriteLine($"Sonde : EventPipe indisponible ({exception.Message}) — CPU et mémoire seulement.");
        }

        _sampler = Task.Run(SampleAsync);
    }

    public bool EventPipeAvailable { get; }

    public string ProcessDescription => $"{_process.ProcessName} (pid {_process.Id})";

    public static ServerProbe? Attach(LoadOptions options)
    {
        Process? process = null;
        if (options.ServerPid is { } pid)
        {
            process = Process.GetProcessById(pid);
        }
        else
        {
            var candidates = Process.GetProcessesByName(options.ServerProcess);
            if (candidates.Length > 1)
            {
                Console.WriteLine($"Sonde : {candidates.Length} processus {options.ServerProcess}, préciser --server-pid.");
                return null;
            }

            process = candidates.FirstOrDefault();
        }

        if (process is null)
        {
            Console.WriteLine($"Sonde : aucun processus {options.ServerProcess} sur cette machine, mesures côté client seulement.");
            return null;
        }

        return new ServerProbe(process);
    }

    /// <summary>Everything measured since the previous call.</summary>
    public StageServerStats Take()
    {
        lock (_lock)
        {
            var taken = _current;
            _current = new StageServerStats();
            return taken;
        }
    }

    /// <summary>The live view for the console line, without resetting.</summary>
    public (double Cpu, double WorkingSetMb, double ContentionMsPerSecond) Live()
    {
        lock (_lock)
        {
            return (_current.LastCpu, _current.LastWorkingSetMb, _current.LastContentionMs);
        }
    }

    private async Task SampleAsync()
    {
        var cores = Environment.ProcessorCount;
        _process.Refresh();
        var lastCpu = _process.TotalProcessorTime;
        var lastWall = Stopwatch.GetTimestamp();
        double contentionAtLastSample = 0;
        try
        {
            while (!_stop.IsCancellationRequested)
            {
                await Task.Delay(1000, _stop.Token);
                _process.Refresh();
                if (_process.HasExited) return;
                var cpu = _process.TotalProcessorTime;
                var wall = Stopwatch.GetElapsedTime(lastWall).TotalMilliseconds;
                lastWall = Stopwatch.GetTimestamp();
                var percentOfMachine = (cpu - lastCpu).TotalMilliseconds / wall / cores * 100;
                lastCpu = cpu;
                lock (_lock)
                {
                    _current.CpuSamples.Add(percentOfMachine);
                    _current.WorkingSetMb.Add(_process.WorkingSet64 / 1048576.0);
                    _current.Threads.Add(_process.Threads.Count);
                    _current.LastCpu = percentOfMachine;
                    _current.LastWorkingSetMb = _process.WorkingSet64 / 1048576.0;
                    _current.LastContentionMs = _current.ContentionMs - contentionAtLastSample;
                    contentionAtLastSample = _current.ContentionMs;
                }
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (InvalidOperationException)
        {
            // The server exited.
        }
    }

    private void Read()
    {
        try
        {
            using var source = new EventPipeEventSource(_session!.EventStream);
            source.Clr.ContentionStop += data =>
            {
                var duration = data.PayloadByName("DurationNs") is double ns ? ns : 0;
                lock (_lock)
                {
                    _current.ContentionCount++;
                    _current.ContentionMs += duration / 1_000_000.0;
                }
            };
            source.Dynamic.All += OnEvent;
            source.Process();
        }
        catch (Exception exception) when (_stop.IsCancellationRequested || exception is EndOfStreamException)
        {
        }
    }

    private void OnEvent(TraceEvent e)
    {
        if (e.ProviderName == "System.Runtime" && e.EventName == "EventCounters")
        {
            // The event's one field is a dictionary whose "Payload" entry holds the counter (as dotnet-counters reads it).
            if (e.PayloadValue(0) is not IDictionary<string, object> outer) return;
            var payload = outer.TryGetValue("Payload", out var inner) && inner is IDictionary<string, object> fields
                ? fields
                : outer;
            var name = payload.TryGetValue("Name", out var n) ? n as string : null;
            if (name is null) return;
            var value = payload.TryGetValue("Mean", out var mean) ? Convert.ToDouble(mean, CultureInfo.InvariantCulture)
                : payload.TryGetValue("Increment", out var increment) ? Convert.ToDouble(increment, CultureInfo.InvariantCulture)
                : double.NaN;
            if (double.IsNaN(value)) return;
            lock (_lock)
            {
                if (!_current.Runtime.TryGetValue(name, out var list)) _current.Runtime[name] = list = new List<double>();
                list.Add(value);
            }

            return;
        }

        if (e.ProviderName != "System.Diagnostics.Metrics") return;
        if (e.PayloadByName("meterName") as string != Navislamia.Game.Network.ServerMetrics.MeterName) return;
        var instrument = e.PayloadByName("instrumentName") as string ?? "?";
        var tags = e.PayloadByName("tags") as string ?? "";
        var key = tags.Length == 0 ? instrument : $"{instrument} [{tags}]";

        if (e.EventName == "HistogramValuePublished")
        {
            var quantiles = ParseQuantiles(e.PayloadByName("quantiles") as string);
            var count = e.PayloadByName("count") is { } c ? Convert.ToInt64(c, CultureInfo.InvariantCulture) : 0;
            if (quantiles is null || count == 0 && quantiles.Value.P99 == 0) return;
            lock (_lock)
            {
                if (!_current.Histograms.TryGetValue(key, out var stats)) _current.Histograms[key] = stats = new HistogramStats();
                stats.Add(count, quantiles.Value);
            }
        }
        else if (e.EventName is "CounterRateValuePublished" or "UpDownCounterRateValuePublished")
        {
            if (!double.TryParse(e.PayloadByName("rate") as string, NumberStyles.Float, CultureInfo.InvariantCulture,
                    out var rate)) return;
            lock (_lock)
            {
                _current.Counters[key] = _current.Counters.GetValueOrDefault(key) + rate;
            }
        }
    }

    private static (double P50, double P95, double P99)? ParseQuantiles(string? text)
    {
        if (string.IsNullOrEmpty(text)) return null;
        double p50 = 0, p95 = 0, p99 = 0;
        foreach (var part in text.Split(';', StringSplitOptions.RemoveEmptyEntries))
        {
            var pair = part.Split('=');
            if (pair.Length != 2 || !double.TryParse(pair[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var v)) continue;
            switch (pair[0])
            {
                case "0.5": p50 = v; break;
                case "0.95": p95 = v; break;
                case "0.99": p99 = v; break;
            }
        }

        return (p50, p95, p99);
    }

    public void Dispose()
    {
        _stop.Cancel();
        try
        {
            _session?.Stop();
        }
        catch (Exception)
        {
            // The server may already be gone.
        }

        try
        {
            _reader?.Wait(TimeSpan.FromSeconds(5));
            _sampler.Wait(TimeSpan.FromSeconds(2));
        }
        catch (AggregateException)
        {
        }

        _session?.Dispose();
    }
}

/// <summary>A server histogram over a stage: one entry per published second (the meter resets each interval).</summary>
public sealed class HistogramStats
{
    public long Count { get; private set; }
    private readonly List<double> _p50 = new();
    public double WorstP95 { get; private set; }
    public double WorstP99 { get; private set; }

    public void Add(long count, (double P50, double P95, double P99) q)
    {
        Count += count;
        _p50.Add(q.P50);
        WorstP95 = Math.Max(WorstP95, q.P95);
        WorstP99 = Math.Max(WorstP99, q.P99);
    }

    /// <summary>The median of the per-second medians: the typical value.</summary>
    public double TypicalP50
    {
        get
        {
            if (_p50.Count == 0) return 0;
            var sorted = _p50.OrderBy(v => v).ToList();
            return sorted[sorted.Count / 2];
        }
    }
}

public sealed class StageServerStats
{
    public readonly List<double> CpuSamples = new();
    public readonly List<double> WorkingSetMb = new();
    public readonly List<double> Threads = new();
    public readonly Dictionary<string, List<double>> Runtime = new();
    public readonly Dictionary<string, HistogramStats> Histograms = new();
    public readonly Dictionary<string, double> Counters = new();
    public long ContentionCount;
    public double ContentionMs;

    internal double LastCpu;
    internal double LastWorkingSetMb;
    internal double LastContentionMs;
}
