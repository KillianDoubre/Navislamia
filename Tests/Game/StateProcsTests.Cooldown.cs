using System.Buffers.Binary;
using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using Navislamia.Game.Services;
using Navislamia.Game.Services.Combat;
using NUnit.Framework;

namespace Tests.Game;

/// <summary>EF_INC_SKILL_COOL_TIME_ON_* (10063-10070), socle-passifs-combat-recharge-bouclier.md §1.</summary>
public partial class StateProcsTests
{
    // Epic 7 SkillResource rows, var0..var19 as imported.
    private static readonly decimal[] AllSkillsOnMagicHit = // 31323, 10063: every skill -2.5 s × level, 4 % × level
        { 1, 0, -2.5m, 0, 0, 0, 0, 0, 0, 0, 4, 99, 0, 20, 0, 0, 0, 0, 99, 0 };
    private static readonly decimal[] OneSkillOnKill = // 21120, 10065: skill 2019 -8 s × level, 50 %, level gap 15
        { 0, 0, 0, 2019, 0, -8, 0, 0, 0, 0, 50, 99, 0, 0, 0, 0, 0, 0, 15, 0 };
    private static readonly decimal[] TwoSkillsWhenHit = // 21110, 10064: skills 3492 and 3815 -3 s, 10 %
        { 0, 0, 0, 3492, -3, 0, 3815, -3, 0, 0, 10, 99, 0, 23, 0, 0, 0, 0, 99, 0 };

    private const uint MagicHarm = EnergyProcs.MagicalSkill | EnergyProcs.Harmful;

    [Test]
    public void Cooldown_procs_read_their_own_var_layout()
    {
        var procs = new CooldownProcs(new[] { (31323, 10063, AllSkillsOnMagicHit), (21120, 10065, OneSkillOnKill) });
        var learned = new Dictionary<int, byte> { [31323] = 1, [21120] = 1 };

        procs.Resolve(learned, StateProcEvent.Attack, 0, MagicHarm, 0, 100, 100, 0, 100, Draw(3)).Should()
            .Equal(new[] { new CooldownProc(31323, true, -2, 0, 0, 0, 0) }, "-2.5 truncates to -2 like the C++ int");
        procs.Resolve(learned, StateProcEvent.Attack, 0, MagicHarm, 0, 100, 100, 0, 100, Draw(4)).Should()
            .BeEmpty("the chance is var9 + var10 × level = 4");
        procs.Resolve(learned, StateProcEvent.Attack, 0, EnergyProcs.NormalAttack, 0, 100, 100, 0, 100, Draw()).Should()
            .BeEmpty("var13 = 20 keeps harmful magic only");

        procs.Resolve(learned, StateProcEvent.Kill, 0, 0, 0, 100, 0, 10, 100, Draw()).Should()
            .Equal(new CooldownProc(21120, false, 0, 2019, -8, 0, 0));
        procs.Resolve(learned, StateProcEvent.Kill, 0, 0, 0, 100, 0, 16, 100, Draw()).Should()
            .BeEmpty("_KILL_TAG's level gap is var18 = 15");
    }

    [Test]
    public void A_shift_changes_only_a_cooling_skill_and_frees_it_below_zero()
    {
        CooldownProcs.Shift(1000, 1500, -2).Should().Be(1300u);
        CooldownProcs.Shift(1000, 1500, 3).Should().Be(1800u);
        CooldownProcs.Shift(1000, 1500, -10).Should().BeNull();
        CooldownProcs.Shift(1000, 900, -2).Should().Be(900u, "a ready skill is left alone");
    }

    [Test]
    public void A_magic_hit_shortens_every_cooling_skill_but_Grace_and_sends_their_time_left()
    {
        var h = new Harness((31323, 10063, AllSkillsOnMagicHit));
        var c = h.Client(1);
        var now = ServerClock.Now;
        Info(c).LearnedSkills[31323] = 1;
        Info(c).LearnedSkills[100] = 2;
        Info(c).LearnedSkills[CooldownProcs.Grace] = 1;
        Info(c).SkillCooldowns[100] = now + 1000;
        Info(c).SkillCooldowns[CooldownProcs.Grace] = now + 1000;
        h.Wires[c].Sent.Clear();

        h.Combat.NotifyHit(new CombatActor(c), new CombatActor(c, MonsterId: h.Monster), new HitResult(10, HitFlags.None),
            MagicHarm);

        Info(c).SkillCooldowns[100].Should().Be(now + 800);
        Info(c).SkillCooldowns[CooldownProcs.Grace].Should().Be(now + 1000, "SKILL_GRACE is never touched");
        var list = h.Wires[c].Sent.Single(p => BinaryPrimitives.ReadUInt16LittleEndian(p.AsSpan(4)) == 403);
        list.Should().HaveCount(14 + 14, "one TS_SKILL_INFO: only the skill that changed");
    }

    [Test]
    public void Being_hit_shortens_the_two_named_skills_of_the_target()
    {
        var h = new Harness((21110, 10064, TwoSkillsWhenHit));
        var c = h.Client(1);
        var now = ServerClock.Now;
        Info(c).LearnedSkills[21110] = 1;
        Info(c).LearnedSkills[3492] = 1;
        Info(c).LearnedSkills[3815] = 1;
        Info(c).SkillCooldowns[3492] = now + 1000;
        Info(c).SkillCooldowns[3815] = now + 200;

        h.Combat.NotifyHit(new CombatActor(c, MonsterId: h.Monster), new CombatActor(c), new HitResult(10, HitFlags.None));

        Info(c).SkillCooldowns[3492].Should().Be(now + 700);
        Info(c).SkillCooldowns.Should().NotContainKey(3815, "200 ticks minus 3 s leaves it ready");
    }

    [Test]
    public void A_summons_own_cooldown_passive_shifts_its_card_cooldowns()
    {
        var h = new Harness((31323, 10063, AllSkillsOnMagicHit));
        var c = h.Client(1);
        h.Summon(c, 10);
        var card = Info(c).CreatureCards[10];
        var now = ServerClock.Now;
        card.Skills[31323] = 1;
        card.Skills[300] = 1;
        card.SkillCooldowns[300] = now + 500;
        Info(c).LearnedSkills[100] = 1;
        Info(c).SkillCooldowns[100] = now + 500;
        h.Wires[c].Sent.Clear();

        h.Combat.NotifyHit(new CombatActor(c, 10), new CombatActor(c, MonsterId: h.Monster), new HitResult(10, HitFlags.None),
            MagicHarm);

        card.SkillCooldowns[300].Should().Be(now + 300);
        Info(c).SkillCooldowns[100].Should().Be(now + 500, "the master did not learn the passive");
        var list = h.Wires[c].Sent.Single(p => BinaryPrimitives.ReadUInt16LittleEndian(p.AsSpan(4)) == 403);
        BinaryPrimitives.ReadUInt32LittleEndian(list.AsSpan(7)).Should().Be(10u, "the 403 names the summon");
    }
}
