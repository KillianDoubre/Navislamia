using System.Collections.Concurrent;

namespace Navislamia.LoadTest;

public sealed class Counter
{
    private long _value;
    public long Value => Interlocked.Read(ref _value);
    public void Increment() => Interlocked.Increment(ref _value);
    public void Add(long amount) => Interlocked.Add(ref _value, amount);
}

/// <summary>Every sample of one latency, kept for exact percentiles (a run holds a few hundred thousand at most).</summary>
public sealed class Recorder
{
    private readonly object _lock = new();
    private List<double> _samples = new();

    public void Record(double ms)
    {
        lock (_lock) _samples.Add(ms);
    }

    /// <summary>Takes the samples recorded since the last call.</summary>
    public Summary Drain()
    {
        List<double> taken;
        lock (_lock)
        {
            taken = _samples;
            _samples = new List<double>();
        }

        return Summary.Of(taken);
    }

    /// <summary>The samples since the last <see cref="Drain"/>, without taking them.</summary>
    public Summary Peek()
    {
        lock (_lock) return Summary.Of(new List<double>(_samples));
    }
}

public readonly record struct Summary(int Count, double P50, double P95, double P99, double Max)
{
    public static Summary Of(List<double> samples)
    {
        if (samples.Count == 0) return default;
        samples.Sort();
        double At(double q) => samples[Math.Min(samples.Count - 1, (int)Math.Ceiling(q * samples.Count) - 1)];
        return new Summary(samples.Count, At(0.50), At(0.95), At(0.99), samples[^1]);
    }

    public string Format() => Count == 0 ? "—" : $"{P50:0.0} / {P95:0.0} / {P99:0.0} / {Max:0.0}";
}

/// <summary>What the bots measure: the server's answer times as a client sees them, traffic and failures.</summary>
public sealed class RunMetrics
{
    public readonly Recorder AuthLogin = new();
    public readonly Recorder WorldEntry = new();
    public readonly Recorder MoveEcho = new();
    public readonly Recorder ChatEcho = new();
    public readonly Recorder AttackFirstSwing = new();

    public readonly Counter FramesSent = new();
    public readonly Counter FramesReceived = new();
    public readonly Counter BytesReceived = new();
    public readonly Counter MovesSent = new();
    public readonly Counter MovesLost = new();
    public readonly Counter ChatsSent = new();
    public readonly Counter AttacksSent = new();
    public readonly Counter Kills = new();
    public readonly Counter SwingsLanded = new();
    public readonly Counter Deaths = new();

    private int _connecting;
    private int _inWorld;

    public int Connecting => Volatile.Read(ref _connecting);
    public int InWorld => Volatile.Read(ref _inWorld);

    public void BeginConnecting() => Interlocked.Increment(ref _connecting);
    public void EndConnecting() => Interlocked.Decrement(ref _connecting);
    public void EnterWorld() => Interlocked.Increment(ref _inWorld);
    public void LeaveWorld() => Interlocked.Decrement(ref _inWorld);

    /// <summary>Failures and refusals by kind ("login: timeout", "result 5 → 6"…).</summary>
    public readonly ConcurrentDictionary<string, long> Problems = new();

    public void Problem(string kind) => Problems.AddOrUpdate(kind, 1, (_, n) => n + 1);
}
