using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace Navislamia.Game.Network;

/// <summary>
/// The server's own measurements, published on the <c>Navislamia</c> meter for whoever listens: the load test
/// tool reads them over EventPipe (<c>tools/LoadTest</c>), <c>dotnet-counters monitor --counters Navislamia</c>
/// too. Without a listener every instrument is disabled and a record costs a field read, so the hot paths
/// check <see cref="Instrument.Enabled"/> before taking a timestamp.
/// </summary>
public static class ServerMetrics
{
    public const string MeterName = "Navislamia";

    private static readonly Meter Meter = new(MeterName);

    /// <summary>One tick of a periodic loop (combat, AI, movement…), tagged <c>loop</c>.</summary>
    public static readonly Histogram<double> TickDuration =
        Meter.CreateHistogram<double>("navislamia.tick.duration", "ms", "Duration of one tick of a periodic loop");

    /// <summary>The synchronous handling of one received game frame, tagged <c>packet</c> (its id).</summary>
    public static readonly Histogram<double> PacketHandling =
        Meter.CreateHistogram<double>("navislamia.packet.duration", "ms", "Synchronous handling of a received frame");

    /// <summary>A visibility pass, tagged <c>op</c>: <c>objects</c> (NPC, monsters, props, items) or <c>players</c>.</summary>
    public static readonly Histogram<double> VisibilityDuration =
        Meter.CreateHistogram<double>("navislamia.visibility.duration", "ms", "Duration of a visibility pass");

    /// <summary>From the oldest message of a batch being queued to the batch leaving on the socket.</summary>
    public static readonly Histogram<double> SendDelay =
        Meter.CreateHistogram<double>("navislamia.send.delay", "ms", "Queue-to-socket delay of an outgoing batch");

    public static readonly Counter<long> BytesSent =
        Meter.CreateCounter<long>("navislamia.send.bytes", "By", "Bytes written to client and auth sockets");

    public static readonly Counter<long> FramesReceived =
        Meter.CreateCounter<long>("navislamia.receive.frames", "{frame}", "Game frames received");

    public static long Timestamp() => Stopwatch.GetTimestamp();

    public static double ElapsedMs(long start) => Stopwatch.GetElapsedTime(start).TotalMilliseconds;

    /// <summary>Times a periodic tick: <c>using (ServerMetrics.Tick("combat")) Tick(now);</c></summary>
    public static Scope Tick(string loop) => new(TickDuration, "loop", loop);

    /// <summary>Times a visibility pass.</summary>
    public static Scope Visibility(string op) => new(VisibilityDuration, "op", op);

    public readonly struct Scope : System.IDisposable
    {
        private readonly Histogram<double> _histogram;
        private readonly string _tag;
        private readonly string _value;
        private readonly long _start;

        public Scope(Histogram<double> histogram, string tag, string value)
        {
            _histogram = histogram;
            _tag = tag;
            _value = value;
            _start = histogram.Enabled ? Stopwatch.GetTimestamp() : 0;
        }

        public void Dispose()
        {
            if (_start != 0)
            {
                _histogram.Record(ElapsedMs(_start), new KeyValuePair<string, object>(_tag, _value));
            }
        }
    }

    /// <summary>
    /// Times the frames of one receive call one after the other: <see cref="Begin"/> after a frame is read,
    /// <see cref="End"/> before the next one and when the call returns, whatever path it returns by.
    /// </summary>
    public struct FrameTimer
    {
        private long _start;
        private ushort _id;

        public void Begin(ushort id)
        {
            End();
            FramesReceived.Add(1);
            if (!PacketHandling.Enabled) return;
            _id = id;
            _start = Stopwatch.GetTimestamp();
        }

        public void End()
        {
            if (_start == 0) return;
            PacketHandling.Record(ElapsedMs(_start), new KeyValuePair<string, object>("packet", _id));
            _start = 0;
        }
    }
}
