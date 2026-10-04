using System;
using System.Collections.Frozen;
using System.Collections.Generic;
using System.Linq;
using Navislamia.Game.DataAccess.Entities.Enums;
using Navislamia.Game.DataAccess.Repositories.Interfaces;
using Serilog;

namespace Navislamia.Game.Services.Stats;

/// <summary>
/// Decodes <c>StateResource.value_0..value_17</c> into stat effects.
/// </summary>
/// <remarks>
/// The values are six <c>(mask, base, perLevel)</c> triplets and <c>amount = base + perLevel * level</c>,
/// which the reference emulator's <c>SEF_PARAMETER_INC</c> branch applies in exactly that order. Triplets
/// 0, 1, 4 and 5 address ParameterA (the decoded bitset); triplets 2 and 3 address ParameterB, which is
/// decoded for elemental resistances (bits 0..6), as it is for item effects.
/// <para>
/// <c>StateResource.effect_type</c> is a different value space from <c>SkillResource.effect_type</c>:
/// here 1 is a flat add and 2 a percentage.
/// </para>
/// </remarks>
public class StateCatalog : IStateCatalog
{
    public const int ParameterInc = 1;
    public const int ParameterAmp = 2;

    public static readonly int[] SupportedEffectTypes = { ParameterInc, ParameterAmp };

    private static readonly int[] ParameterTriplets = { 0, 1, 2, 3, 4, 5 };

    private readonly ILogger _logger = Log.ForContext<StateCatalog>();
    private readonly FrozenDictionary<int, Buffs.PeriodicStateRule> _periodic;
    public Buffs.PeriodicStateRule Periodic(int stateId) => _periodic.GetValueOrDefault(stateId);
    private readonly FrozenDictionary<int, StateEffectTemplate[]> _states;
    private readonly FrozenSet<int> _eraseOnRequest;
    private readonly FrozenDictionary<int, Casting.StateRule> _rules;
    private readonly FrozenSet<int> _stateIds;
    private readonly FrozenDictionary<int, ResurrectionStateValues> _resurrections;

    public StateCatalog(IStateResourceRepository repository)
    {
        _periodic = (repository.GetPeriodicStates() ?? Array.Empty<Buffs.PeriodicStateRule>()).Where(r => r.Supported).ToFrozenDictionary(r => r.StateId);
        var states = new Dictionary<int, StateEffectTemplate[]>();
        foreach (var state in repository.GetStatStates())
        {
            var templates = BuildTemplates(state);
            if (templates.Count > 0)
            {
                states[state.StateId] = templates.ToArray();
            }
        }

        _states = states.ToFrozenDictionary();
        _stateIds = (repository.GetStateIds() ?? Array.Empty<int>()).ToFrozenSet();
        _resurrections = (repository.GetStatesWithEffect((int)StateEffectType.Resurrection)
                          ?? Array.Empty<StateEffectFields>())
            .ToFrozenDictionary(state => state.StateId, state => ResurrectionStateValues.From(state.Values));
        _eraseOnRequest = (repository.GetEraseOnRequestStateIds() ?? Array.Empty<int>()).ToFrozenSet();
        _rules = (repository.GetStateRules() ?? Array.Empty<StateRuleFields>())
            .ToFrozenDictionary(row => row.StateId, ToRule);
        _logger.Debug("Loaded {count} stat states and {cancellable} cancellable states", _states.Count,
            _eraseOnRequest.Count);
    }

    public Casting.StateRule GetRule(int stateId) =>
        _rules.TryGetValue(stateId, out var rule) ? rule : Casting.StateRule.None with { StateId = stateId };

    /// <summary>
    /// <c>reiteration_count</c> is a text column: anything that is not a number reads as no reiteration.
    /// </summary>
    private static Casting.StateRule ToRule(StateRuleFields row) => new(row.StateId,
        row.DuplicateGroups ?? Array.Empty<int>(),
        int.TryParse(row.ReiterationCount, System.Globalization.NumberStyles.Integer,
            System.Globalization.CultureInfo.InvariantCulture, out var count) ? count : 0,
        row.StateTimeType, row.EffectType, row.Values ?? Array.Empty<decimal>());

    public bool IsEraseOnRequest(int stateId)
    {
        return _eraseOnRequest.Contains(stateId);
    }

    public bool Exists(int stateId) => _stateIds.Contains(stateId);

    public bool TryGetResurrection(int stateId, out ResurrectionStateValues values) =>
        _resurrections.TryGetValue(stateId, out values);

    public IReadOnlyList<StatEffect> Resolve(int stateId, int stateLevel)
    {
        if (stateLevel <= 0 || !_states.TryGetValue(stateId, out var templates))
        {
            return Array.Empty<StatEffect>();
        }

        var effects = new StatEffect[templates.Length];
        for (var i = 0; i < templates.Length; i++)
        {
            effects[i] = templates[i].Resolve(stateLevel);
        }

        return effects;
    }

    public static IReadOnlyList<StateEffectTemplate> BuildTemplates(StateEffectFields state)
    {
        if (state.Values is null || !SupportedEffectTypes.Contains(state.EffectType))
        {
            return Array.Empty<StateEffectTemplate>();
        }

        var isPercent = state.EffectType == ParameterAmp;
        List<StateEffectTemplate> templates = null;

        foreach (var triplet in ParameterTriplets)
        {
            var index = triplet * 3;
            if (index + 2 >= state.Values.Length)
            {
                break;
            }

            var mask = state.Values[index];
            var amountBase = (float)state.Values[index + 1];
            var perLevel = (float)state.Values[index + 2];
            if (mask <= 0 || mask > uint.MaxValue || (amountBase == 0f && perLevel == 0f))
            {
                continue;
            }

            foreach (var target in (triplet is 2 or 3 ? ParameterBitset.DecodeResistance((uint)mask) : ParameterBitset.Decode((uint)mask)))
            {
                templates ??= new List<StateEffectTemplate>();
                templates.Add(new StateEffectTemplate(target, amountBase, perLevel, isPercent));
            }
        }

        return (IReadOnlyList<StateEffectTemplate>)templates ?? Array.Empty<StateEffectTemplate>();
    }
}
