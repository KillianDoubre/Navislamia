using System.Buffers.Binary;
using System.Text;
using System.Text.Json;
using FakeItEasy;
using FluentAssertions;
using Microsoft.Extensions.Options;
using Navislamia.Configuration.Options;
using Navislamia.Game.Network.Packets.Game;
using Navislamia.Game.Services;
using Navislamia.Game.Services.Interfaces;

namespace Tests.Game;

[TestFixture]
public class GameNpcDialogPacketsTests
{
    [Test]
    public void Contact_ReadsTheEpic73NpcHandle()
    {
        var packet = new byte[11];
        BinaryPrimitives.WriteUInt32LittleEndian(packet.AsSpan(7), 0x11223344);

        GameNpcDialogPackets.TryReadContact(packet, out var handle).Should().BeTrue();
        handle.Should().Be(0x11223344);
        GameNpcDialogPackets.TryReadContact(packet.AsSpan(0, 10), out _).Should().BeFalse();
    }

    [Test]
    public void Dialog_UsesTheEpic73DynamicStringAndTabMenuLayout()
    {
        var menu = new[]
        {
            new NpcDialogMenuEntry { Label = "@90010001", Trigger = "NextDialog()" },
            new NpcDialogMenuEntry { Label = "@90010002", Trigger = "" }
        };

        var packet = GameNpcDialogPackets.BuildDialog(0x11223344, "@91000437", "@91000438", menu);

        BinaryPrimitives.ReadUInt32LittleEndian(packet).Should().Be((uint)packet.Length);
        BinaryPrimitives.ReadUInt16LittleEndian(packet.AsSpan(4)).Should().Be(3000);
        BinaryPrimitives.ReadInt32LittleEndian(packet.AsSpan(7)).Should().Be(0);
        BinaryPrimitives.ReadUInt32LittleEndian(packet.AsSpan(11)).Should().Be(0x11223344);
        BinaryPrimitives.ReadUInt16LittleEndian(packet.AsSpan(15)).Should().Be(9);
        BinaryPrimitives.ReadUInt16LittleEndian(packet.AsSpan(17)).Should().Be(9);

        var menuLength = BinaryPrimitives.ReadUInt16LittleEndian(packet.AsSpan(19));
        Encoding.ASCII.GetString(packet, 21, 9).Should().Be("@91000437");
        Encoding.ASCII.GetString(packet, 30, 9).Should().Be("@91000438");
        Encoding.ASCII.GetString(packet, 39, menuLength).Should()
            .Be("\t@90010001\tNextDialog()\t\t@90010002\t\t");
        packet[6].Should().Be(Checksum(packet));
    }

    [Test]
    public void Selection_ReadsTheLengthPrefixedTrigger()
    {
        const string trigger = "NextDialog()";
        var packet = new byte[9 + trigger.Length];
        BinaryPrimitives.WriteUInt16LittleEndian(packet.AsSpan(7), (ushort)trigger.Length);
        Encoding.ASCII.GetBytes(trigger).CopyTo(packet, 9);

        GameNpcDialogPackets.TryReadSelection(packet, 1024, out var parsed).Should().BeTrue();
        parsed.Should().Be(trigger);
        GameNpcDialogPackets.TryReadSelection(packet.AsSpan(0, packet.Length - 1), 1024, out _)
            .Should().BeFalse();
        GameNpcDialogPackets.TryReadSelection(packet, trigger.Length - 1, out _).Should().BeFalse();
    }

    [Test]
    public void DialogTemplate_BindsAHandleWithoutMutatingSharedData()
    {
        var template = GameNpcDialogPackets.BuildDialog(0, "@1", "@2", Array.Empty<NpcDialogMenuEntry>());

        var packet = GameNpcDialogPackets.CopyWithNpcHandle(template, 0x11223344);

        BinaryPrimitives.ReadUInt32LittleEndian(packet.AsSpan(11)).Should().Be(0x11223344);
        BinaryPrimitives.ReadUInt32LittleEndian(template.AsSpan(11)).Should().Be(0);
        packet[6].Should().Be(Checksum(packet));
    }

    [Test]
    public void Catalog_ContainsTheEpic7DialogsForTheCurrentLostIslandArea()
    {
        using var document = JsonDocument.Parse(
            File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "npc-dialogs.73.json")));
        var catalog = document.RootElement.GetProperty("NpcDialogCatalog")
            .Deserialize<NpcDialogOptions>();

        catalog.Should().NotBeNull();
        catalog!.Npcs[11421].Should().Be("NPC_lost_island_colbai_contact()");
        catalog.Dialogs["NPC_lost_island_colbai_contact"].Title.Should().Be("@91000437");
        catalog.Dialogs["NPC_lost_island_colbai_contact"].Text.Should().Be("@91000438");
        catalog.Dialogs["NPC_lost_island_colbai_contact"].Menu.Should().ContainSingle()
            .Which.Label.Should().Be("@90010002");
        var compileCatalog = () => new NpcDialogService(Options.Create(catalog), A.Fake<IWarpService>());
        compileCatalog.Should().NotThrow();
    }

    private static byte Checksum(byte[] packet)
    {
        byte checksum = 0;
        for (var index = 0; index < 6; index++)
        {
            checksum += packet[index];
        }

        return checksum;
    }
}
