using System.Buffers.Binary;
using System.Text;
using FakeItEasy;
using FluentAssertions;
using Microsoft.Extensions.Options;
using Navislamia.Configuration.Options;
using Navislamia.Game.Network.Clients;
using Navislamia.Game.Network.Packets.Game;
using Navislamia.Game.Services;
using Navislamia.Game.Services.Interfaces;
using Navislamia.Game.Services.Props;

namespace Tests.Game;

[TestFixture]
public class ScriptWindowsTests
{
    internal static byte[] Reply(string text)
    {
        var bytes = Encoding.ASCII.GetBytes(text); var frame = new byte[9 + bytes.Length];
        BinaryPrimitives.WriteUInt32LittleEndian(frame, (uint)frame.Length);
        BinaryPrimitives.WriteUInt16LittleEndian(frame.AsSpan(4), 3001);
        BinaryPrimitives.WriteUInt16LittleEndian(frame.AsSpan(7), (ushort)bytes.Length);
        bytes.CopyTo(frame, 9); return frame;
    }

    [TestCase("secret_dungeon_confirm_window", "70101", "warp_to_secret_dungeon", "warp_to_secret_dungeon(70101)")]
    [TestCase("instance_dungeon_confirm_window", "40000", "warp_to_instance_dungeon", "warp_to_instance_dungeon(40000)")]
    [TestCase("instance_dungeon_confirm_window2", "40000", "exit_indun", "exit_indun(40000)")]
    [TestCase("dungeon_raid_confirm_window", "Ana", "begin_dungeon_raid", "begin_dungeon_raid()")]
    [TestCase("recall_feather_confirm_window", "Ana", "recall_feather( 12.250000, 20.000000, 0 )", "recall_feather( 12.250000, 20.000000, 0 )")]
    [TestCase("number_input_window", "Ana", "on_channel_set", "on_channel_set(001)")]
    public void Measured_windows_and_callbacks_preserve_lengths_offsets_and_single_use(string window, string argument, string trigger, string reply)
    {
        var connection = new StorageTestHarness.FrameConnection(Array.Empty<byte>());
        var client = StorageTestHarness.NewGameClient(connection); var info = StorageTestHarness.Session(client);
        info.CharacterHandle = 42;
        ScriptWindows.Show(client, window, argument, trigger).Should().BeTrue();
        var packet = connection.Sent.Single();
        packet.Length.Should().Be(13 + window.Length + argument.Length + trigger.Length);
        BinaryPrimitives.ReadUInt16LittleEndian(packet.AsSpan(4)).Should().Be(3003);
        BinaryPrimitives.ReadUInt16LittleEndian(packet.AsSpan(7)).Should().Be((ushort)window.Length);
        BinaryPrimitives.ReadUInt16LittleEndian(packet.AsSpan(9)).Should().Be((ushort)argument.Length);
        BinaryPrimitives.ReadUInt16LittleEndian(packet.AsSpan(11)).Should().Be((ushort)trigger.Length);
        Encoding.ASCII.GetString(packet, 13, packet.Length - 13).Should().Be(window + argument + trigger);
        packet[6].Should().Be(unchecked((byte)packet.Take(6).Sum(b => b)));
        info.ScriptWindow.Matches(reply).Should().BeTrue();
        info.ScriptWindow.Matches(reply + ";set_flag('extra',1)").Should().BeFalse();
        GameNpcDialogPackets.TryReadSelection(Reply(reply), 1024, out var read).Should().BeTrue(); read.Should().Be(reply);
        var scripts = A.Fake<INpcScriptService>();
        var dialogs = new NpcDialogService(Options.Create(new NpcDialogOptions()), A.Fake<IWarpService>(),
            A.Fake<IStorageService>(), A.Fake<IMarketService>(), npcScripts: scripts);
        dialogs.Select(client, Reply(reply)); dialogs.Select(client, Reply(reply));
        A.CallTo(() => scripts.RunWindowScriptAsync(client, reply)).MustHaveHappenedOnceExactly();
        info.ScriptWindow.Should().BeNull(); info.ScriptWindowTrigger.Should().BeEmpty();
    }

    [TestCase("on_channel_set(1);on_channel_set(2)")]
    [TestCase("prefix_on_channel_set(1)")]
    [TestCase("on_channel_set(1,2)")]
    [TestCase("on_channel_set(1+1)")]
    [TestCase("on_channel_set('1')")]
    [TestCase("on_channel_set(9223372036854775808)")]
    [TestCase("on_channel_set()")]
    [TestCase("on_channel_set(1)\0")]
    public void Numerical_callbacks_never_accept_lua_or_overflow(string reply)
        => new ScriptWindow("number_input_window", "Ana", "on_channel_set", 42, 0, 0).Matches(reply).Should().BeFalse();

    [Test]
    public void A_replaced_window_or_character_cannot_reuse_an_old_answer()
    {
        var client = StorageTestHarness.NewGameClient(new StorageTestHarness.FrameConnection(Array.Empty<byte>()));
        var info = StorageTestHarness.Session(client); info.CharacterHandle = 42;
        ScriptWindows.Show(client, "secret_dungeon_confirm_window", "70101", "warp_to_secret_dungeon");
        ScriptWindows.Show(client, "secret_dungeon_confirm_window", "80101", "warp_to_secret_dungeon");
        var scripts = A.Fake<INpcScriptService>();
        var dialogs = new NpcDialogService(Options.Create(new NpcDialogOptions()), A.Fake<IWarpService>(),
            A.Fake<IStorageService>(), A.Fake<IMarketService>(), npcScripts: scripts);
        dialogs.Select(client, Reply("warp_to_secret_dungeon(70101)"));
        info.ScriptWindow.Should().NotBeNull();
        info.CharacterHandle = 43;
        dialogs.Select(client, Reply("warp_to_secret_dungeon(80101)"));
        A.CallTo(() => scripts.RunWindowScriptAsync(A<GameClient>._, A<string>._)).MustNotHaveHappened();
        info.ClearCharacterSession(); info.ScriptWindow.Should().BeNull(); info.ScriptWindowTrigger.Should().BeEmpty();
    }
}
