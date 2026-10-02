using System;
using System.Collections.Generic;
using System.Linq;
using Navislamia.Game.DataAccess.Repositories.Interfaces;

namespace Navislamia.Game.Services.Buffs;

/// <summary>StructSkill's region testers, distribution and effect parameters (enhancement zero).</summary>
public readonly record struct SkillArea(float Radius, bool TargetOrigin = true, int Shape = -1,
    float Property = 0, bool IncludeOrigin = true, int Distribution = 0, int TargetMax = 0);

public static class SkillAreaRules
{
    public const float UnitSize = 12f;
    public static bool IsSupportedDamage(int effect) => effect is
        101 or 111 or 112 or 113 or 201 or 211 or 212 or 213 or 231 or 232 or 241 or 261 or 262 or 263 or 271
        or 30001 or 30011 or 30012 or 30013 or 30016;
    public static bool IsMagical(int effect) => effect is >= 201 and <= 271;
    public static bool IsArea(int effect) => effect is
        111 or 112 or 113 or 211 or 212 or 213 or 261 or 262 or 263 or 271 or 30011 or 30012 or 30013;
    public static bool IsAtOnceMultiple(int effect) => effect is 112 or 212 or 241 or 30012 or 30016;
    public static bool IsSequential(int effect) => effect is 232 or 263;
    public static float Var(CastableBuffFields f, int i) => f.Vars is { } v && i < v.Length ? (float)v[i] : 0;

    public static SkillArea Area(CastableBuffFields f) => f.EffectType switch
    {
        111 => new(Var(f, 2) * UnitSize, Distribution: (int)Var(f, 3), TargetMax: (int)Var(f, 11)),
        112 or 212 => new(Var(f, 5) * UnitSize, Distribution: (int)Var(f, 6), TargetMax: (int)Var(f, 7)),
        113 or 213 => new(Var(f, 8) * UnitSize, Var(f, 6) != 0, (int)Var(f, 5), Var(f, 9),
            Var(f, 7) != 0, (int)Var(f, 2), (int)Var(f, 3)),
        211 => new(Var(f, 2) * UnitSize, Distribution: (int)Var(f, 3), TargetMax: (int)Var(f, 4)),
        261 or 263 => new(Var(f, 9) * UnitSize, Distribution: (int)Var(f, 10), TargetMax: (int)Var(f, 11)),
        262 => new(Var(f, 9) * UnitSize, Var(f, 6) != 0, (int)Var(f, 5), Var(f, 8),
            Var(f, 7) != 0, (int)Var(f, 10), (int)Var(f, 11)),
        271 => new(Var(f, 9) * UnitSize, Distribution: (int)Var(f, 16), TargetMax: (int)Var(f, 17)),
        30011 or 30012 => new(Var(f, 4) * UnitSize, Distribution: (int)Var(f, 5), TargetMax: (int)Var(f, 6)),
        30013 => new(Var(f, 4) * UnitSize, Var(f, 8) != 0, (int)Var(f, 7), Var(f, 10),
            Var(f, 9) != 0, (int)Var(f, 5), (int)Var(f, 6)),
        _ => default
    };

    public static float Damage(CastableBuffFields f, int level, float attack, float magic)
    {
        if (f.EffectType is 101 or 111 or 112 or 113 or 201 or 211 or 212 or 213)
            return Math.Max(0, (int)((IsMagical(f.EffectType) ? magic : attack) + Var(f, 0) + Var(f, 1) * level));
        var damage = SkillDamageCurve.BaseDamage(f.Kind, f.Vars, level, attack, magic);
        return f.EffectType == 271 ? Math.Max(0, (int)(damage * Var(f, 10))) : damage;
    }

    public static int FireCount(CastableBuffFields f, int level) => f.EffectType switch
    {
        232 or 241 or 263 => Math.Clamp((int)(Var(f, 6) + Var(f, 7) * level), 1, 255),
        112 or 212 => Math.Clamp((int)(Var(f, 2) + Var(f, 3) * level), 1, 255),
        30012 => Math.Clamp((int)(Var(f, 7) + Var(f, 8) * level), 1, 255),
        30016 => Math.Clamp((int)(Var(f, 4) + Var(f, 5) * level), 1, 255),
        // Ground damage fires immediately, then every interval until the duration (inclusive).
        271 => Math.Clamp((int)(Duration(f, level) / Interval(f)) + 1, 1, 65535),
        _ => 1
    };
    public static uint Interval(CastableBuffFields f) => Math.Max(1u, ServerClock.FromSeconds((decimal)Var(f,
        f.EffectType is 112 or 212 ? 4 : f.EffectType == 30012 ? 9 : f.EffectType == 30016 ? 6 : 8)));
    public static uint Duration(CastableBuffFields f, int level) =>
        ServerClock.FromSeconds((decimal)Math.Max(0, Var(f, 6) + Var(f, 7) * level));

    public static bool Contains(SkillArea area, float cx, float cy, float tx, float ty, float x, float y)
    {
        var ox = area.TargetOrigin ? tx : cx;
        var oy = area.TargetOrigin ? ty : cy;
        var dx = x - ox;
        var dy = y - oy;
        var distance = MathF.Sqrt(dx * dx + dy * dy);
        if (!float.IsFinite(distance) || distance > Math.Max(0, area.Radius)
            || (!area.IncludeOrigin && distance == 0)) return false;
        var vx = (area.TargetOrigin ? cx : tx) - ox;
        var vy = (area.TargetOrigin ? cy : ty) - oy;
        var length = MathF.Sqrt(vx * vx + vy * vy);
        if (length == 0) { vx = 1; vy = 0; } else { vx /= length; vy /= length; }
        var along = dx * vx + dy * vy;
        var across = MathF.Abs(dx * vy - dy * vx);
        var thickness = area.Property * UnitSize / 2;
        return area.Shape switch
        {
            0 => along >= 0 && across < thickness,
            1 => distance == 0 || along / distance >= MathF.Cos(area.Property),
            2 => across < thickness || MathF.Abs(along) < thickness,
            _ => true
        };
    }

    public static List<T> Select<T>(IEnumerable<T> source, SkillArea area, float cx, float cy, float tx, float ty,
        Func<T, (float X, float Y)> position, ref float damage, ICombatRandom random = null)
    {
        var targets = source.Where(t => { var p = position(t); return Contains(area, cx, cy, tx, ty, p.X, p.Y); }).ToList();
        if (area.Distribution is 3 or 4)
        {
            var ox = area.Distribution == 3 ? tx : cx;
            var oy = area.Distribution == 3 ? ty : cy;
            targets = targets.OrderBy(t => { var p = position(t); return (p.X-ox)*(p.X-ox)+(p.Y-oy)*(p.Y-oy); }).ToList();
        }
        if (area.Distribution is 2 or 3 or 4)
        {
            if (area.Distribution == 2)
            {
                random ??= CombatRandom.Shared;
                for (var i = targets.Count - 1; i > 0; i--)
                { var j = random.Next(i + 1); (targets[i], targets[j]) = (targets[j], targets[i]); }
            }
            targets = targets.Take(Math.Max(0, area.TargetMax)).ToList();
        }
        else if (area.Distribution == 1 && targets.Count > area.TargetMax)
            damage = Math.Max(0, (int)(damage * Math.Max(0, area.TargetMax) / targets.Count));
        return targets;
    }
}
