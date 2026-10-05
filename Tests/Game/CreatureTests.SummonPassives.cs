using System;
using System.Collections.Generic;
using System.Linq;
using FakeItEasy;
using FluentAssertions;
using Navislamia.Game.DataAccess.Entities.Enums;
using Navislamia.Game.DataAccess.Repositories.Interfaces;
using Navislamia.Game.Services;
using Navislamia.Game.Services.Creatures;
using Navislamia.Game.Services.Stats;
using NUnit.Framework;

namespace Tests.Game;

/// <summary>The master's summon passives (10031/10032) and the hunting expertise (10013), socle-passifs-combat-recharge-bouclier.md §3.</summary>
public partial class CreatureTests
{
    // Epic 7 SkillResource rows.
    private static readonly decimal[] HpExpansion = { 0, 0.06m, 0, 0, 0, 0, 0, 0, 0, 0 };          // 1831, 10032
    private static readonly decimal[] RegenExpansion = { 0, 0, 0, 0, 0, 0, 0, 0.1m, 0, 0.1m };    // 1851, 10032
    private static readonly decimal[] UndeadHunting =                                              // 41001, 10013
        { 8, 0.1m, 0.05m, 0, 0, 5, 0.1m, 0.05m, 0, 0, 6, 0.1m, 0.05m, 0, 0 };
    private static readonly decimal[] UndeadGuard =                                                // 41004, 10013
        { 8, 0, 0, 0.1m, 0.05m, 5, 0, 0, 0.1m, 0.05m, 6, 0, 0, 0.1m, 0.05m };

    private static SkillPassiveCatalog SummonPassives(params SkillPassiveFields[] extra)
    {
        var repository = A.Fake<ISkillResourceRepository>();
        A.CallTo(() => repository.GetStatPassives()).Returns(new[]
        {
            new SkillPassiveFields(1831, 10032, HpExpansion, SkillWeaponFlag.None, true),
            new SkillPassiveFields(1851, 10032, RegenExpansion, SkillWeaponFlag.None, true),
            new SkillPassiveFields(9031, 10031, new decimal[] { 100, 50, 0, 0, 0, 0 }, SkillWeaponFlag.None, true),
            new SkillPassiveFields(41001, 10013, UndeadHunting, SkillWeaponFlag.None, true),
            new SkillPassiveFields(41004, 10013, UndeadGuard, SkillWeaponFlag.None, true)
        }.Concat(extra).ToArray());
        return new SkillPassiveCatalog(repository);
    }

    [Test]
    public void The_masters_summon_passives_reach_max_hp_mp_sp_and_regeneration()
    {
        var catalog = SummonPassives();
        catalog.ResolveForSummon(1831, 3).Should().ContainSingle().Which.Should().Match<StatEffect>(e =>
            e.Target == StatTarget.MaxHp && e.IsPercent && Math.Abs(e.Value - 0.18f) < 1e-6f);
        catalog.ResolveForSummon(1851, 2).Select(e => (e.Target, e.IsPercent)).Should().Equal(
            (StatTarget.HpRegenPoint, true), (StatTarget.MpRegenPoint, true));
        catalog.ResolveForSummon(9031, 2).Should().Equal(new StatEffect(StatTarget.MaxHp, 200f, false));
        catalog.ResolveForSummon(1831, 0).Should().BeEmpty();
        catalog.Resolve(1831, 3, null).Should().BeEmpty("the master's own stats do not get it");
    }

    [Test]
    public void Hunting_training_grows_the_damage_against_its_types_and_avoids_what_they_deal()
    {
        var expertise = CreatureExpertise.From(new[] { (UndeadHunting, 2), (UndeadGuard, 2) });
        expertise.DamageAgainst(8).Should().BeApproximately(1.2f, 1e-6f, "0.1 + 0.05 × 2 against undead");
        expertise.DamageAgainst(1).Should().Be(1f);
        expertise.DamageTakenFrom(8).Should().BeApproximately(0.8f, 1e-6f);
        expertise.DamageTakenFrom(9).Should().Be(1f);
        expertise.DamageAgainst(99).Should().Be(1f, "no creature is of type 99");

        var all = CreatureExpertise.From(new[] { (new decimal[] { 99, 0.3m, 0, 0, 0 }, 1) });
        Enumerable.Range(0, CreatureExpertise.TypeCount).Select(all.DamageAgainst).Should().OnlyContain(v => Math.Abs(v - 1.3f) < 1e-6f);
    }

    [Test]
    public void A_summon_with_hunting_training_hits_harder_and_the_master_passive_refreshes_its_stats()
    {
        int Swing(bool trained)
        {
            var h = new Harness();
            var service = new CreatureService(Catalog(), h.Characters, h.World, h.Combat, new SummonWorldService(h.Players),
                h.Players, random: new SequenceRandom(0), runTicks: false, passives: SummonPassives(
                    new SkillPassiveFields(41010, 10013, new decimal[] { 99, 1m, 0, 0, 0 }, SkillWeaponFlag.None, true)));
            Bind(h);
            if (trained) h.Info.CreatureCards[60].Skills[41010] = 1;
            service.Summon(h.Client, 60);
            var damage = 0;
            A.CallTo(() => h.Combat.GetMonsterStats(0)).Returns(new StatBlock { Avoid = 0, Defence = 0 });
            A.CallTo(() => h.Combat.ApplyDamage(h.Client, 0, Harness.MonsterHandle, A<int>._, A<int>._))
                .Invokes((Navislamia.Game.Network.Clients.GameClient _, long _, uint _, int d, int _) => damage = d)
                .Returns(40);
            service.SummonAttack(h.Client, h.Info.Summons[0].Handle, Harness.MonsterHandle);
            service.ProcessSwings(DateTime.UtcNow.AddSeconds(1));

            if (trained)
            {
                // The master learns Creature HP Expansion: the summon's max HP follows (setSummonUpdate).
                var before = h.Info.Summons[0].Stats.MaxHp;
                h.Info.LearnedSkills[1831] = 5;
                service.RefreshSummonStats(h.Client);
                h.Info.Summons[0].Stats.MaxHp.Should().BeApproximately(before * 1.3f, 1f);
            }

            return damage;
        }

        var plain = Swing(false);
        var trained = Swing(true);
        plain.Should().BeGreaterThan(0);
        trained.Should().BeGreaterThan(plain, "1 + 1.0 against every type doubles the attack before the defence");
    }
}
