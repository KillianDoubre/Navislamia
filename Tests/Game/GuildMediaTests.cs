using System.Buffers.Binary;
using System.Text;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Navislamia.Game.DataAccess.Entities.Enums;
using Navislamia.Game.Network.Packets.Enums;
using Navislamia.Game.Network.Packets.Game;
using Navislamia.Game.Network.Packets.Upload;

namespace Tests.Game;

[TestFixture]
public class GuildMediaTests
{
    [Test]
    public async Task Upload_is_authorized_once_persisted_and_visible_to_non_members()
    {
        var h = new GuildTests.Harness(); var leader = await h.Player(1); var member = await h.Player(2);
        var outsider = await h.Player(3);
        (await h.Create(leader, "Navis")).Should().BeTrue(); await h.Join(leader, member);
        var uploads = new List<byte[]>(); h.Uploads.IsReady = () => true; h.Uploads.Send = uploads.Add;
        (await h.Guilds.ExecuteCommandAsync(member, "/gupdateicon")).Should().BeFalse(); uploads.Should().BeEmpty();
        (await h.Guilds.ExecuteCommandAsync(leader, "/gupdateicon")).Should().BeTrue(); uploads.Should().ContainSingle();
        var request = uploads.Single(); request.Length.Should().Be(24);
        BinaryPrimitives.ReadUInt16LittleEndian(request.AsSpan(4)).Should().Be(50003);
        var window = h.Frames[1].Sent.Single(f => BinaryPrimitives.ReadUInt16LittleEndian(f.AsSpan(4)) == 652);
        window.Length.Should().Be(51);
        BinaryPrimitives.ReadInt32LittleEndian(window.AsSpan(15)).Should().Be(BinaryPrimitives.ReadInt32LittleEndian(request.AsSpan(19)));
        Encoding.ASCII.GetString(window, 19, 32).TrimEnd('\0').Should().Be("Navislamia");
        var guild = BinaryPrimitives.ReadInt32LittleEndian(request.AsSpan(15));
        var upload = new GuildUploadPackets.Upload(guild, 512, "game001_0000000001_mark.jpg", false);
        (await h.Guilds.CompleteUploadAsync(upload with { Filename = "../bad.jpg" })).Should().BeFalse();
        (await h.Guilds.CompleteUploadAsync(upload)).Should().BeTrue();
        (await h.Guilds.CompleteUploadAsync(upload)).Should().BeFalse();
        using (var db = h.Db()) { var saved = await db.Guilds.SingleAsync(); saved.Icon.Should().Be(upload.Filename); saved.IconSize.Should().Be(512); }
        (await h.Guilds.ExecuteCommandAsync(outsider, "/gicon " + guild)).Should().BeTrue();
        h.Frames[3].Sent.Select(f => Encoding.ASCII.GetString(f)).Should().Contain(s => s.Contains("https://guild.test/icons/" + upload.Filename));
        (await h.Guilds.ExecuteCommandAsync(leader, "/gwindow")).Should().BeTrue();
        h.Frames[1].Sent.Select(f => BitConverter.ToUInt16(f, 4)).Should().Contain((ushort)651);
        using (var db = h.Db()) { var saved = await db.Guilds.SingleAsync(); saved.DonationPoint = 321; await db.SaveChangesAsync(); }
        (await h.Guilds.ExecuteCommandAsync(outsider, "/granking")).Should().BeTrue();
        h.Frames[3].Sent.Select(f => Encoding.ASCII.GetString(f)).Should().Contain(s => s.Contains("1. Navis : 321"));
    }

    [Test]
    public async Task Expiry_permission_revocation_notice_and_public_advertisement()
    {
        var h = new GuildTests.Harness(); var leader = await h.Player(1); var member = await h.Player(2);
        var outsider = await h.Player(3);
        await h.Create(leader, "Navis"); await h.Join(leader, member);
        var uploads = new List<byte[]>(); h.Uploads.IsReady = () => true; h.Uploads.Send = uploads.Add;
        (await h.Guilds.ExecuteCommandAsync(leader, "/gnotice Bonjour")).Should().BeTrue();
        h.Frames[2].Sent.Select(f => Encoding.ASCII.GetString(f)).Should().Contain(s => s.Contains("NOTICE|Bonjour"));
        (await h.Guilds.ExecuteCommandAsync(member, "/gadvertise 2 60 Recrutement")).Should().BeFalse();
        (await h.Guilds.ExecuteCommandAsync(leader, "/gadvertise 2 60 Recrutement")).Should().BeTrue();
        (await h.Guilds.ExecuteCommandAsync(outsider, "/glist")).Should().BeTrue();
        h.Frames[3].Sent.Select(f => Encoding.ASCII.GetString(f)).Should().Contain(s => s.Contains("Navis : Recrutement"));
        (await h.Guilds.ExecuteCommandAsync(leader, "/gupdatebanner")).Should().BeTrue();
        h.Time.Now = h.Time.Now.AddMinutes(6);
        var guild = BinaryPrimitives.ReadInt32LittleEndian(uploads.Single().AsSpan(15));
        (await h.Guilds.CompleteUploadAsync(new(guild, 512, "banner.jpg", true))).Should().BeFalse();
        (await h.Guilds.ExecuteCommandAsync(leader, "/gupdatebanner")).Should().BeTrue();
        using (var db = h.Db()) { var g = await db.Guilds.SingleAsync(); g.LeaderId = 2; await db.SaveChangesAsync(); }
        (await h.Guilds.CompleteUploadAsync(new(guild, 512, "banner.jpg", true))).Should().BeFalse();
    }

    [TestCase(650)] [TestCase(651)] [TestCase(660)]
    public void Empty_windows_have_exact_header_and_checksum(int id)
    {
        var packet = GameGuildPackets.BuildWindow((GamePackets)id); packet.Length.Should().Be(7);
        BinaryPrimitives.ReadUInt16LittleEndian(packet.AsSpan(4)).Should().Be((ushort)id);
        packet[6].Should().Be(StorageTestHarness.Checksum(packet));
    }

    [Test]
    public void Upload_reader_checks_exact_size_filename_and_type()
    {
        var name = Encoding.ASCII.GetBytes("game_1.jpg");
        var packet = GameGuildPackets.Frame(50009, 17 + name.Length);
        BinaryPrimitives.WriteInt32LittleEndian(packet.AsSpan(7), 1);
        BinaryPrimitives.WriteInt32LittleEndian(packet.AsSpan(11), 1234);
        packet[15] = (byte)name.Length; name.CopyTo(packet.AsSpan(17));
        GuildUploadPackets.TryReadUpload(packet, out var upload).Should().BeTrue(); upload.Filename.Should().Be("game_1.jpg");
        packet[16] = 2; GuildUploadPackets.TryReadUpload(packet, out _).Should().BeFalse();
        packet[16] = 0; packet[15]++; GuildUploadPackets.TryReadUpload(packet, out _).Should().BeFalse();
    }
}
