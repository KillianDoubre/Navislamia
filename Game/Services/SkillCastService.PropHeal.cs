using System;
using System.Collections.Generic;
using System.Linq;
using Navislamia.Game.DataAccess.Repositories.Interfaces;
using Navislamia.Game.Network.Clients;
using Navislamia.Game.Network.Packets.Game;
using Navislamia.Game.Services.Buffs;
using Navislamia.Game.Services.Props;

namespace Navislamia.Game.Services;

/// <summary>
/// The HuntaHolic healing props (docs/packet-specs/socle-huntaholic.md §8): their skill is cast at the prop, the prop is
/// used up (<c>UseProp</c>), then <c>REGION_HEAL_BY_FIELD_PROP</c> (9502) heals around it at once, or
/// <c>MAKE_AREA_EFFECT_PROP_BY_FIELD_PROP</c> (9503) leaves a healing area on its spot.
/// </summary>
public partial class SkillCastService
{
    private bool FirePropHeal(GameClient client, GameActionPackets.SkillRequest request, CastableBuffFields fields,
        int skillLevel, long instanceId, uint now)
    {
        // UseProp may delete the prop, so its position is kept first, as the reference does. A prop someone else
        // used first is gone: the cast fails.
        if (_dynamicProps is null || !_dynamicProps.TryUse(instanceId, out var prop))
        {
            return false;
        }

        var info = client.ConnectionInfo;
        var (x, y, layer) = (prop.Instance.X, prop.Instance.Y, prop.Layer);

        if (fields.EffectType == BuffCatalog.RegionHealByFieldProp)
        {
            var heal = PropHealRules.Region(fields, skillLevel);
            var hits = HealAroundProp(client, x, y, layer, heal.Range, heal.Targets, heal.FlatHp, heal.HpRatio, 0f, now,
                SkillHitType.AddHp);
            BroadcastPropHeal(client, request, x, y, layer, SkillPacketType.Fire, hits, heal.Range);
            return true;
        }

        var area = PropHealRules.Area(fields, skillLevel);
        BroadcastPropHeal(client, request, x, y, layer, SkillPacketType.Fire, Array.Empty<SkillHit>(), area.Range);

        // StructSkillProp: the area shows as a skill prop for its duration and fires at once, then every interval. It
        // ends early when its caster is gone (onProcess finds no caster).
        var caster = info.CharacterHandle;
        bool CasterHere() => info.CharacterHandle == caster;
        _effects.Track(new GroundSkillProp(x, y, 0, layer, now, area.DurationTicks, fields.SkillId,
            () => _players?.Registry.Clients ?? new[] { client },
            recipient =>
            {
                if (ReferenceEquals(recipient, client)) return caster;
                lock (recipient.ConnectionInfo.PlayerVisibilityLock)
                    return recipient.ConnectionInfo.SpawnedPlayers.ContainsKey(caster) ? caster : 0;
            },
            CasterHere), now);

        _effects.Schedule(now, area.IntervalTicks, PropHealRules.AreaFireCount(area), tick =>
        {
            if (!CasterHere()) return false;
            var hits = HealAroundProp(client, x, y, layer, area.Range, area.Targets, 0, area.HpRatio, area.MpRatio, tick,
                SkillHitType.AddHpMpSp);

            // broadcastSkillMessage(REGION_FIRE) only when something was healed (m_nTargetCount).
            if (hits.Count > 0)
            {
                BroadcastPropHeal(client, request, x, y, layer, SkillPacketType.RegionFire, hits, area.Range);
            }

            return true;
        }, null);
        _effects.Tick(now);
        return true;
    }

    /// <summary>
    /// The players of <paramref name="layer"/> alive within <paramref name="range"/> of the prop that the target limit
    /// reaches: each is healed (<c>Heal</c>, <c>MPHeal</c>, bounded by the maxima) and gets its <c>hp</c>/<c>mp</c>.
    /// Summons are not healed here.
    /// </summary>
    private List<SkillHit> HealAroundProp(GameClient caster, float x, float y, byte layer, float range,
        PropHealTargets targets, int flatHp, float hpRatio, float mpRatio, uint now, SkillHitType hitType)
    {
        var hits = new List<SkillHit>();
        var casterParty = caster.ConnectionInfo.PartyId ?? 0;
        foreach (var target in _players?.Registry.Clients ?? new[] { caster })
        {
            var info = target.ConnectionInfo;
            if (info.CharacterHandle == 0 || info.Layer != layer || info.CharacterHp <= 0) continue;
            var (tx, ty) = info.PositionAt(now);
            if (CombatRange.Distance(x, y, tx, ty) > range) continue;
            if (!PropHealRules.Reaches(targets, ReferenceEquals(target, caster), casterParty, info.PartyId ?? 0)) continue;

            var healedHp = PropHealRules.Healed(flatHp, hpRatio, info.CharacterHp, info.CharacterMaxHp);
            info.CharacterHp += healedHp;
            target.SendVitalProperty(GameStatPackets.BuildProperty(info.CharacterHandle, "hp", info.CharacterHp));

            var healedMp = 0;
            if (mpRatio > 0)
            {
                var maxMp = (int)_statService.Compute(info).Total.MaxMp;
                healedMp = PropHealRules.Healed(0, mpRatio, info.CharacterMp, maxMp);
                info.CharacterMp += healedMp;
                target.SendVitalProperty(GameStatPackets.BuildProperty(info.CharacterHandle, "mp", info.CharacterMp));
            }

            hits.Add(hitType == SkillHitType.AddHpMpSp
                ? new SkillHit(SkillHitType.AddHpMpSp, info.CharacterHandle, info.CharacterHp, healedHp, IncMp: healedMp,
                    TargetMp: info.CharacterMp)
                : new SkillHit(SkillHitType.AddHp, info.CharacterHandle, info.CharacterHp, healedHp));
        }

        return hits;
    }

    /// <summary>
    /// The heal's <c>TS_SC_SKILL</c> to the caster and to the players of the layer who see the prop's spot. The prop's
    /// handle differs per client and the prop is gone, so the others read no target.
    /// </summary>
    private void BroadcastPropHeal(GameClient caster, GameActionPackets.SkillRequest request, float x, float y,
        byte layer, SkillPacketType type, IReadOnlyList<SkillHit> hits, float range)
    {
        var info = caster.ConnectionInfo;
        byte[] Frame(uint target) => GameSkillPackets.BuildSkill((ushort)request.SkillId, request.SkillLevel,
            info.CharacterHandle, target, x, y, request.Z, layer, type, 0, 0, info.CharacterHp, info.CharacterMp,
            hits: hits, multiple: hits.Count > 1, range: range);

        caster.Connection.Send(Frame(type == SkillPacketType.Fire ? request.Target : 0));
        foreach (var watcher in (_players?.Registry.Clients ?? Array.Empty<GameClient>())
                     .Where(c => !ReferenceEquals(c, caster) && c.ConnectionInfo.CharacterHandle != 0
                                 && c.ConnectionInfo.Layer == layer
                                 && CombatRange.Distance(x, y, c.ConnectionInfo.X, c.ConnectionInfo.Y) <= WorldVisibility.ViewRange))
        {
            watcher.Connection.Send(Frame(0));
        }
    }
}
