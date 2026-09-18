using System.Collections.Generic;
using FluentAssertions;
using Navislamia.Game.Network.Packets;
using Navislamia.Game.Services.Buffs;

namespace Tests.Game;

[TestFixture]
public class StateRemovalTests
{
    private const uint Handle = 0x40000123u;
    private const int QuickPace = 2622;
    private const int Shield = 1101;

    private static ActiveBuff Buff(int stateId, int skillId = 0, ushort stateHandle = 1)
    {
        return new ActiveBuff(stateHandle, stateId, skillId, 1, 100, 500);
    }

    private static (bool Ok, StateRemovalPlan Plan, ResultCode Error) Resolve(int stateCode,
        IReadOnlyList<ActiveBuff> buffs, IReadOnlyDictionary<int, int> auras = null,
        bool eraseOnRequest = true, uint target = Handle)
    {
        var ok = StateRemoval.TryResolve(target, Handle, stateCode, buffs,
            auras ?? new Dictionary<int, int>(), eraseOnRequest, out var plan, out var error);

        return (ok, plan, error);
    }

    [Test]
    public void TryResolve_AcceptsThePlayersOwnCancellableState()
    {
        var buffs = new List<ActiveBuff> { Buff(Shield, 700), Buff(QuickPace, 1201, 9) };

        var result = Resolve(QuickPace, buffs);

        result.Ok.Should().BeTrue();
        result.Error.Should().Be(ResultCode.Success);
        result.Plan.Index.Should().Be(1);
        result.Plan.Buff.StateId.Should().Be(QuickPace);
        result.Plan.Buff.StateHandle.Should().Be(9);
        result.Plan.ToggleGroup.Should().Be(0, "a plain buff is not a toggled aura");
    }

    [Test]
    public void TryResolve_RefusesAHandleThatIsNotThePlayersOwn()
    {
        var buffs = new List<ActiveBuff> { Buff(QuickPace) };

        var zero = Resolve(QuickPace, buffs, target: 0);
        zero.Ok.Should().BeFalse();
        zero.Error.Should().Be(ResultCode.NotExist, "the client never builds the message for a null handle");

        var other = Resolve(QuickPace, buffs, target: 0x40000999u);
        other.Ok.Should().BeFalse();
        other.Error.Should().Be(ResultCode.NotExist, "summons and other players are not modelled");
    }

    [Test]
    public void TryResolve_RefusesAStateThePlayerDoesNotHave()
    {
        var result = Resolve(QuickPace, new List<ActiveBuff> { Buff(Shield) });

        result.Ok.Should().BeFalse();
        result.Error.Should().Be(ResultCode.NotExist);
    }

    [Test]
    public void TryResolve_NeverReadsZeroAsRemoveEverything()
    {
        var result = Resolve(0, new List<ActiveBuff> { Buff(Shield), Buff(QuickPace) });

        result.Ok.Should().BeFalse();
        result.Error.Should().Be(ResultCode.NotExist, "no sentinel means 'all states'");
    }

    [Test]
    public void TryResolve_RefusesAStateWithoutEraseOnRequest()
    {
        var result = Resolve(QuickPace, new List<ActiveBuff> { Buff(QuickPace) }, eraseOnRequest: false);

        result.Ok.Should().BeFalse();
        result.Error.Should().Be(ResultCode.NotActable,
            "only the states whose state_time_type carries AF_ERASE_ON_REQUEST are cancellable");
        result.Plan.Should().Be(default(StateRemovalPlan), "a refused request plans nothing");
    }

    [Test]
    public void TryResolve_TakesTheFirstEntryMatchingTheCode()
    {
        var buffs = new List<ActiveBuff> { Buff(QuickPace, 1201, 3), Buff(QuickPace, 1202, 4) };

        var result = Resolve(QuickPace, buffs);

        result.Ok.Should().BeTrue();
        result.Plan.Index.Should().Be(0, "the 408 carries no state level, so insertion order decides");
        result.Plan.Buff.StateHandle.Should().Be(3);
    }

    [Test]
    public void TryResolve_ReportsTheAuraGroupOfAToggledAura()
    {
        var buffs = new List<ActiveBuff> { Buff(QuickPace, 1201, 5) };
        var auras = new Dictionary<int, int> { [2] = 1201 };

        var result = Resolve(QuickPace, buffs, auras);

        result.Ok.Should().BeTrue();
        result.Plan.ToggleGroup.Should().Be(2, "an aura cancelled here must also be switched off");
    }

    [Test]
    public void TryResolve_ReportsTheSmallestGroupWhenTwoGroupsPointAtTheSameSkill()
    {
        var buffs = new List<ActiveBuff> { Buff(QuickPace, 1201) };
        var auras = new Dictionary<int, int> { [7] = 1201, [3] = 1201 };

        var result = Resolve(QuickPace, buffs, auras);

        result.Ok.Should().BeTrue();
        result.Plan.ToggleGroup.Should().Be(3, "the answer must not depend on dictionary order");
    }

    [Test]
    public void TryResolve_LeavesAnAuraOfAnotherSkillAlone()
    {
        var buffs = new List<ActiveBuff> { Buff(QuickPace, 1201) };
        var auras = new Dictionary<int, int> { [2] = 1301 };

        var result = Resolve(QuickPace, buffs, auras);

        result.Ok.Should().BeTrue();
        result.Plan.ToggleGroup.Should().Be(0, "another group's active aura is not this state");
    }
}
