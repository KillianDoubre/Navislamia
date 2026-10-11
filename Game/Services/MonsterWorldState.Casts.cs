using System.Collections.Generic;

namespace Navislamia.Game.Services;

/// <summary>
/// A monster's spell between its <c>ST_Casting</c> and its fire (<c>StructSkill::m_Status == STATUS_CAST</c>), kept here
/// because the player's cast service, which breaks it (<c>EF_CASTING_CANCEL_WITH_ADD_STATE</c>), cannot reach the
/// monster skill service without a dependency cycle.
/// </summary>
public partial class MonsterWorldState
{
    private readonly Dictionary<long, (int Life, bool Magical, bool Broken)> _casts = new();

    /// <summary>The spell family a cast breaker names: 1 physical, 2 magical, 99 any (<c>SkillBase::SC_*</c>).</summary>
    public const int PhysicalCast = 1;

    /// <inheritdoc cref="PhysicalCast"/>
    public const int MagicalCast = 2;

    /// <inheritdoc cref="PhysicalCast"/>
    public const int AnyCast = 99;

    public void BeginCast(long instanceId, bool magical)
    {
        lock (_stateLock) _casts[instanceId] = (_lifeVersions.GetValueOrDefault(instanceId), magical, false);
    }

    /// <summary>The cast fired: nothing can break it any more. True when it had been broken first.</summary>
    public bool EndCast(long instanceId)
    {
        lock (_stateLock)
        {
            return _casts.Remove(instanceId, out var cast) && cast.Broken;
        }
    }

    /// <summary><c>CASTING_CANCEL_WITH_ADD_STATE</c>: the cast of this family, if one is under way, is broken.</summary>
    public bool TryInterruptCast(long instanceId, int family)
    {
        lock (_stateLock)
        {
            if (!_casts.TryGetValue(instanceId, out var cast) || cast.Broken
                || cast.Life != _lifeVersions.GetValueOrDefault(instanceId))
            {
                return false;
            }

            var matches = family == AnyCast || (family == MagicalCast) == cast.Magical && family is PhysicalCast or MagicalCast;
            if (!matches) return false;
            _casts[instanceId] = cast with { Broken = true };
            return true;
        }
    }

    public bool IsCastBroken(long instanceId)
    {
        lock (_stateLock) return _casts.TryGetValue(instanceId, out var cast) && cast.Broken;
    }
}
