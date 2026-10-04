using System;
using System.Collections.Generic;
using Navislamia.Game.Network.Clients;

namespace Navislamia.Game.Services.Props;

/// <summary>
/// The life of the world's props (docs/packet-specs/socle-props.md), as the official <c>StructFieldProp</c> and
/// <c>FieldPropManager</c> run it. A prop with no use count, regen time or life time is always there and tracked by
/// nobody; the others come and go.
/// </summary>
public interface IFieldPropStates
{
    /// <summary>Whether the prop is in the world now (a used-up or expired prop is not, until it returns).</summary>
    bool IsPresent(long instanceId);

    /// <summary>
    /// <c>StructFieldProp::IsCastable</c> and the remain count: there, uses left, and nobody else casting at it. The
    /// cast holds the prop until <paramref name="until"/> (the cast's end, a guard against a cast that never ends).
    /// </summary>
    bool TryBeginCast(long instanceId, GameClient caster, uint until);

    /// <summary><c>CancelCast</c>: the prop is free again.</summary>
    void EndCast(long instanceId, GameClient caster);

    /// <summary>
    /// <c>UseProp</c>: one use taken. False when the prop is gone or used up meanwhile; <paramref name="removed"/> when
    /// that was its last use, so it leaves the world until its regen time.
    /// </summary>
    bool TryUse(long instanceId, out bool removed);

    /// <summary>The props that came back or expired by <paramref name="now"/>: their spot must be streamed again.</summary>
    IReadOnlyList<FieldPropInstance> Tick(uint now);
}

public sealed class FieldPropStates : IFieldPropStates
{
    private sealed class State
    {
        public FieldPropInstance Instance;
        public PropRules Rules;
        public bool Present;
        public int Uses;
        public uint DueAt;
        public uint ExpiresAt;
        public GameClient Caster;
        public uint CastUntil;
    }

    private readonly object _lock = new();
    private readonly Dictionary<long, State> _states = new();
    private readonly Func<uint> _clock;

    public FieldPropStates(IFieldPropCatalog catalog, Func<uint> clock = null)
    {
        _clock = clock ?? (() => ServerClock.Now);
        var now = _clock();
        foreach (var instance in catalog.Instances)
        {
            if (!catalog.TryGetTemplate(instance.PropId, out var template) || !template.RulesOrNone.IsTracked)
            {
                continue;
            }

            // RegisterFieldProp: every prop is first pended for its regen time from the start.
            var rules = template.RulesOrNone;
            _states[instance.InstanceId] = new State
            {
                Instance = instance, Rules = rules, Present = false, DueAt = unchecked(now + rules.RegenTicks)
            };
        }

        Tick(now);
    }

    public bool IsPresent(long instanceId)
    {
        lock (_lock) return !_states.TryGetValue(instanceId, out var state) || state.Present;
    }

    public bool TryBeginCast(long instanceId, GameClient caster, uint until)
    {
        lock (_lock)
        {
            if (!_states.TryGetValue(instanceId, out var state)) return true;
            if (!state.Present || state.Rules.UseCount > 0 && state.Uses < 1) return false;
            var now = _clock();
            if (state.Caster is not null && !ReferenceEquals(state.Caster, caster) && unchecked((int)(state.CastUntil - now)) > 0)
            {
                return false;
            }

            state.Caster = caster;
            state.CastUntil = until;
            return true;
        }
    }

    public void EndCast(long instanceId, GameClient caster)
    {
        lock (_lock)
        {
            if (_states.TryGetValue(instanceId, out var state) && ReferenceEquals(state.Caster, caster)) state.Caster = null;
        }
    }

    public bool TryUse(long instanceId, out bool removed)
    {
        removed = false;
        lock (_lock)
        {
            if (!_states.TryGetValue(instanceId, out var state)) return true;
            state.Caster = null;
            if (!state.Present) return false;
            if (state.Rules.UseCount > 0)
            {
                if (state.Uses < 1) return false;
                state.Uses--;
            }

            // A prop without a use count keeps its one use forever (m_nUseCount = 1 never decremented).
            if (state.Rules.UseCount > 0 && state.Uses == 0)
            {
                Remove(state, _clock());
                removed = true;
            }

            return true;
        }
    }

    public IReadOnlyList<FieldPropInstance> Tick(uint now)
    {
        List<FieldPropInstance> changed = null;
        lock (_lock)
        {
            foreach (var state in _states.Values)
            {
                if (!state.Present && unchecked((int)(now - state.DueAt)) >= 0)
                {
                    state.Present = true;
                    state.Uses = state.Rules.UseCount;
                    state.ExpiresAt = state.Rules.LifeTicks > 0 ? unchecked(now + state.Rules.LifeTicks) : 0;
                    (changed ??= new List<FieldPropInstance>()).Add(state.Instance);
                }
                else if (state.Present && state.ExpiresAt != 0 && unchecked((int)(now - state.ExpiresAt)) > 0)
                {
                    // The life time is over: out of the world, back after the regen time.
                    Remove(state, now);
                    (changed ??= new List<FieldPropInstance>()).Add(state.Instance);
                }
            }
        }

        return (IReadOnlyList<FieldPropInstance>)changed ?? Array.Empty<FieldPropInstance>();
    }

    private static void Remove(State state, uint now)
    {
        state.Present = false;
        state.Caster = null;
        state.ExpiresAt = 0;
        state.DueAt = unchecked(now + state.Rules.RegenTicks);
    }
}
