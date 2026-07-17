namespace Navislamia.Game.Services.Buffs;

public enum AuraAction
{
    /// <summary>Nothing was active in the group: switch this aura on.</summary>
    TurnOn,

    /// <summary>This exact aura was active: casting it again switches it off.</summary>
    TurnOff,

    /// <summary>Another aura of the same group was active: switch it off and this one on.</summary>
    Swap
}

/// <summary>
/// The aura toggle rule, ported from <c>Unit::ToggleAura</c>.
/// </summary>
/// <remarks>
/// <c>m_vAura</c> is keyed by <c>toggle_group</c>, not by skill, so **one aura per group is active at a
/// time**. Recasting the same aura turns it off; casting a different aura of the same group swaps it.
/// </remarks>
public static class AuraToggle
{
    /// <param name="activeSkillId">The aura currently active in the requested skill's toggle group, or 0.</param>
    public static AuraAction Resolve(int activeSkillId, int requestedSkillId)
    {
        if (activeSkillId == 0)
        {
            return AuraAction.TurnOn;
        }

        return activeSkillId == requestedSkillId ? AuraAction.TurnOff : AuraAction.Swap;
    }
}
