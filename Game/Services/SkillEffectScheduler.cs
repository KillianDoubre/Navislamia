using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Serilog;

namespace Navislamia.Game.Services;

/// <summary>Timed skill fires in server ticks. Callbacks run outside the queue lock.</summary>
public sealed class SkillEffectScheduler : IDisposable
{
    private sealed record Effect(uint Due, uint Interval, int Remaining, Func<uint, bool> Fire, Action Complete);
    private readonly object _gate = new();
    private readonly List<Effect> _pending = new();
    private readonly List<GroundSkillProp> _props = new();
    private readonly CancellationTokenSource _stop = new();
    public SkillEffectScheduler(bool startTimer = true) { if (startTimer) _ = Run(); }
    public void Schedule(uint due, uint interval, int count, Func<uint, bool> fire, Action complete)
    {
        lock (_gate) _pending.Add(new Effect(due, Math.Max(1, interval), Math.Max(1, count), fire, complete));
    }
    public void Track(GroundSkillProp prop, uint now)
    { prop.Sync(now); lock (_gate) _props.Add(prop); }
    public void Tick(uint now)
    {
        GroundSkillProp[] props;
        lock (_gate) props = _props.ToArray();
        foreach (var prop in props)
        {
            try { if (!prop.Sync(now)) lock (_gate) _props.Remove(prop); }
            catch (Exception ex) { Log.Error(ex, "Ground skill actor sync failed"); }
        }
        List<Effect> ready = new();
        lock (_gate)
            for (var i = _pending.Count - 1; i >= 0; i--)
                if (unchecked((int)(now - _pending[i].Due)) >= 0)
                { ready.Add(_pending[i]); _pending.RemoveAt(i); }
        foreach (var effect in ready)
        {
            var keep = false;
            try { keep = effect.Fire(now); }
            catch (Exception ex) { Log.Error(ex, "Timed skill effect failed"); }
            if (keep && effect.Remaining > 1)
            {
                // Never burst all overdue shots at once after a stalled tick.
                lock (_gate) _pending.Add(effect with { Due = unchecked(now + effect.Interval), Remaining = effect.Remaining - 1 });
            }
            else
                try { effect.Complete?.Invoke(); }
                catch (Exception ex) { Log.Error(ex, "Timed skill completion failed"); }
        }
    }
    private async Task Run()
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(50));
        try
        {
            while (await timer.WaitForNextTickAsync(_stop.Token))
            {
                using var _ = Navislamia.Game.Network.ServerMetrics.Tick("skill-effects");
                Tick(ServerClock.Now);
            }
        }
        catch (OperationCanceledException) { }
    }
    public void Dispose() { _stop.Cancel(); lock (_gate) { _pending.Clear(); _props.Clear(); } _stop.Dispose(); }
}
