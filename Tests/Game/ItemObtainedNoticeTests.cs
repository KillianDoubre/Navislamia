using FluentAssertions;
using Navislamia.Game.DataAccess.Entities.Enums;
using Navislamia.Game.Services;
using NUnit.Framework;

namespace Tests.Game;

/// <summary>The official "item obtained" line (MixManager, HuntaholicManager): @254 with the count when joinable.</summary>
[TestFixture]
public class ItemObtainedNoticeTests
{
    private const int JoinFlag = 1 << (int)ItemUseFlag.Join;

    [Test]
    public void A_joinable_item_names_its_count_and_another_its_name_alone()
    {
        ItemObtainedNotice.Message(1234, 1, joinable: true).Should().Be("@254\v#@item_name@#\v@1234\v#@item_num@#\v1");
        ItemObtainedNotice.Message(1234, 1, joinable: false).Should().Be("@253\v#@item_name@#\v@1234");
    }

    [Test]
    public void Joinable_is_the_join_flag_unless_a_creature_is_bound_to_the_card()
    {
        ItemObtainedNotice.IsJoinable(JoinFlag, ItemFlag.None).Should().BeTrue();
        ItemObtainedNotice.IsJoinable(JoinFlag, 0).Should().BeTrue();
        ItemObtainedNotice.IsJoinable(0, 0).Should().BeFalse();
        ItemObtainedNotice.IsJoinable(JoinFlag, unchecked((ItemFlag)(int)0x80000000)).Should().BeFalse("ITEM_FLAG_SUMMON");
    }
}
