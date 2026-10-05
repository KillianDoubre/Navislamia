using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using FakeItEasy;
using FluentAssertions;
using Navislamia.Game.Network.Clients;
using Navislamia.Game.Network.Packets;
using Navislamia.Game.Network.Packets.Enums;
using Navislamia.Game.Network.Packets.Game;
using Navislamia.Game.Services.Creatures;
using Navislamia.Game.Services;
using NUnit.Framework;

namespace Tests.Game;

public partial class CreatureTests
{
    private static CreatureCard RenameCard(Harness h)
    {
        var card = h.Info.CreatureCards[50];
        card.SummonId = 100; card.SummonCode = SummonId; card.SummonHandle = 0x50000001;
        card.SummonName = "Original"; card.Level = 5;
        h.Info.SummonSlots = new long[6]; h.Info.SummonSlots[0] = 50; h.Info.CharacterGold = 25000;
        A.CallTo(() => h.Characters.RenameSummonAsync("Ana", 100, "Renamed", 20000)).Returns(ResultCode.Success);
        return card;
    }

    [Test]
    public async Task Name_change_requires_an_owned_window_and_consumes_it_once()
    {
        var h = new Harness(); var card = RenameCard(h);
        await h.Service.ChangeNameAsync(h.Client, "Renamed"); h.Sent.Should().BeEmpty();
        h.Service.ShowNameChange(h.Client, 0x50000002).Should().BeFalse();
        h.Service.ShowNameChange(h.Client, 0).Should().BeTrue();
        BinaryPrimitives.ReadUInt32LittleEndian(h.Sent.Single().AsSpan(7)).Should().Be(card.SummonHandle);
        h.Ids.Should().Equal(322);
        await h.Service.ChangeNameAsync(h.Client, "Renamed");
        card.SummonName.Should().Be("Renamed"); h.Info.CharacterGold.Should().Be(20000);
        h.Info.SummonNameChangeTarget.Should().Be(0);
        h.Ids.Should().Contain((ushort)30).And.Contain((ushort)507);
        var count = h.Sent.Count;
        await h.Service.ChangeNameAsync(h.Client, "Again"); h.Sent.Should().HaveCount(count);
        A.CallTo(() => h.Characters.RenameSummonAsync("Ana", 100, "Renamed", 20000)).MustHaveHappenedOnceExactly();
    }

    [TestCase("abc", "@117")] [TestCase("Original", "@118")] [TestCase("original", "@17")]
    [TestCase("Bad_Name", "@17")] [TestCase("Name1234567890123456", "@17")]
    public async Task Refused_names_consume_the_window_without_charging(string name, string line)
    {
        var h = new Harness(); var card = RenameCard(h);
        h.Service.ShowNameChange(h.Client, card.SummonHandle);
        await h.Service.ChangeNameAsync(h.Client, name);
        card.SummonName.Should().Be("Original"); h.Info.CharacterGold.Should().Be(25000);
        h.Info.SummonNameChangeTarget.Should().Be(0);
        h.Sent.Last().Should().Equal(GameChatPackets.BuildChat("@SYSTEM", 34, line));
    }

    [Test]
    public async Task Persistence_failure_refunds_the_gold_and_emits_no_name_change()
    {
        var h = new Harness(); var card = RenameCard(h);
        A.CallTo(() => h.Characters.RenameSummonAsync("Ana", 100, "Renamed", 20000)).Returns(ResultCode.DBError);
        h.Service.ShowNameChange(h.Client, card.SummonHandle);
        await h.Service.ChangeNameAsync(h.Client, "Renamed");
        h.Info.CharacterGold.Should().Be(25000); card.SummonName.Should().Be("Original");
        h.Ids.Should().NotContain((ushort)30); h.Info.SummonNameChangeTarget.Should().Be(0);
    }

    [Test]
    public async Task Insufficient_gold_cannot_rename_after_the_window_was_shown()
    {
        var h = new Harness(); var card = RenameCard(h);
        h.Service.ShowNameChange(h.Client, card.SummonHandle); h.Info.CharacterGold = 4999;
        await h.Service.ChangeNameAsync(h.Client, "Renamed");
        h.Info.CharacterGold.Should().Be(4999); card.SummonName.Should().Be("Original");
        A.CallTo(() => h.Characters.RenameSummonAsync(A<string>._, A<long>._, A<string>._, A<long>._)).MustNotHaveHappened();
    }

    [Test]
    public async Task A_summoned_creatures_new_name_reaches_its_master_and_observers()
    {
        var h = new Harness(); var card = RenameCard(h);
        var presence = new SummonPresence(card.SummonHandle, new SummonWorldEntry { Name = "Original" }, 100, 100, 0);
        h.Info.Summons = new[] { presence };
        var wire = new StorageTestHarness.FrameConnection(Array.Empty<byte>());
        var observer = StorageTestHarness.NewGameClient(wire);
        A.CallTo(() => h.Players.Observers(h.Client)).Returns(new[] { observer });
        h.Service.ShowNameChange(h.Client, card.SummonHandle);
        await h.Service.ChangeNameAsync(h.Client, "Renamed");
        presence.Entry.Name.Should().Be("Renamed");
        wire.Sent.Should().ContainSingle().Which.Should().Equal(GamePetPackets.BuildChangeName(card.SummonHandle, "Renamed"));
        h.Ids.Should().Contain((ushort)30);
    }

    [Test]
    public void SP_changes_are_clamped_and_sent_to_the_owner_only()
    {
        var h = new Harness(); var card = RenameCard(h);
        var observer = StorageTestHarness.NewGameClient(new StorageTestHarness.FrameConnection(Array.Empty<byte>()));
        A.CallTo(() => h.Players.Observers(h.Client)).Returns(new[] { observer });
        h.Service.SetSp(h.Client, card, 123);
        h.Service.SetSp(h.Client, card, 123); h.Sent.Should().ContainSingle();
        h.Service.SetSp(h.Client, card, 5000); card.Sp.Should().Be(1000);
        h.Service.SyncSp(h.Client, card.SummonHandle, 80); card.Sp.Should().Be(80); card.MaxSp.Should().Be(80);
        h.Service.SetSp(h.Client, card, -10); card.Sp.Should().Be(0);
        h.Service.SetSp(h.Client, new CreatureCard { ItemId = 51, SummonHandle = 123 }, 10);
        h.Ids.Should().Equal(514, 514, 514, 514);
        ((StorageTestHarness.FrameConnection)observer.Connection).Sent.Should().BeEmpty();
        card.Progress().Sp.Should().Be(0);
    }

    [Test]
    public void Card_flip_returns_base_levels_empty_for_an_unbound_card_and_not_exist_for_unknown_handles()
    {
        var h = new Harness(); var card = RenameCard(h); card.Skills[40011] = 2; card.Skills[40012] = 3;
        h.Service.SendCardSkillList(h.Client, card.Handle);
        h.Sent.Single().Should().Equal(GameSmallPackets.SkillLevels(card.Skills.ToArray()));
        card.SummonId = 0; h.Service.SendCardSkillList(h.Client, card.Handle);
        h.Sent.Last().Should().Equal(GameSmallPackets.SkillLevels(Array.Empty<KeyValuePair<int, byte>>()));
        h.Service.SendCardSkillList(h.Client, 999);
        var refusal = h.Sent.Last();
        BinaryPrimitives.ReadUInt16LittleEndian(refusal.AsSpan(7)).Should().Be(452);
        BinaryPrimitives.ReadUInt16LittleEndian(refusal.AsSpan(9)).Should().Be((ushort)ResultCode.NotExist);
        h.Ids.Should().NotContain((ushort)403).And.NotContain((ushort)301);
    }

    [Test]
    public void Card_flip_finds_the_card_of_another_player_online_like_the_global_FindItem()
    {
        var h = new Harness();
        var other = StorageTestHarness.NewGameClient(new StorageTestHarness.FrameConnection(Array.Empty<byte>()));
        var otherInfo = StorageTestHarness.Session(other);
        otherInfo.CharacterHandle = 8;
        var card = new CreatureCard { ItemId = 77, Amount = 1, SummonId = 300, SummonCode = SummonId };
        card.Skills[40011] = 4;
        otherInfo.CreatureCards[77] = card;
        h.Registry.Register(8, other);

        h.Service.SendCardSkillList(h.Client, 77);

        h.Sent.Single().Should().Equal(GameSmallPackets.SkillLevels(card.Skills.ToArray()),
            "a card shown in a trade window or a booth is flipped like an owned one");
    }
}
