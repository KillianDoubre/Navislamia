using System;
using System.Collections.Frozen;
using System.Collections.Generic;
using System.Linq;
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
/// not decoded and is skipped, as it is for item effects.
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

    private static readonly int[] ParameterATriplets = { 0, 1, 4, 5 };

    private readonly ILogger _logger = Log.ForContext<StateCatalog>();
    private readonly FrozenDictionary<int, StateEffectTemplate[]> _states;

    public StateCatalog(IStateResourceRepository repository)
    {
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
        _logger.Debug("Loaded {count} stat states", _states.Count);
    }

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

        foreach (var triplet in ParameterATriplets)
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

            foreach (var target in ParameterBitset.Decode((uint)mask))
            {
                templates ??= new List<StateEffectTemplate>();
                templates.Add(new StateEffectTemplate(target, amountBase, perLevel, isPercent));
            }
        }

        return (IReadOnlyList<StateEffectTemplate>)templates ?? Array.Empty<StateEffectTemplate>();
    }
}
