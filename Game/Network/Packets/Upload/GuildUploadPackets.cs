using System;
using System.Buffers.Binary;
using System.Text;
using Navislamia.Game.Network.Packets.Enums;
using Navislamia.Game.Network.Packets.Game;

namespace Navislamia.Game.Network.Packets.Upload;

public static class GuildUploadPackets
{
    public sealed record Upload(int GuildId, int FileSize, string Filename, bool Banner);
    public static byte[] BuildRequest(int character, int account, int guild, int password, bool banner)
    {
        var frame = GameGuildPackets.Frame((ushort)UploadPackets.TM_SU_REQUEST_UPLOAD, 24);
        BinaryPrimitives.WriteInt32LittleEndian(frame.AsSpan(7), character);
        BinaryPrimitives.WriteInt32LittleEndian(frame.AsSpan(11), account);
        BinaryPrimitives.WriteInt32LittleEndian(frame.AsSpan(15), guild);
        BinaryPrimitives.WriteInt32LittleEndian(frame.AsSpan(19), password);
        frame[23] = banner ? (byte)1 : (byte)0;
        return frame;
    }
    public static bool TryReadUpload(ReadOnlySpan<byte> frame, out Upload upload)
    {
        upload = null;
        if (frame.Length < 18 || BinaryPrimitives.ReadInt32LittleEndian(frame) != frame.Length
            || BinaryPrimitives.ReadUInt16LittleEndian(frame[4..]) != (ushort)UploadPackets.TM_US_UPLOAD
            || frame.Length != 17 + frame[15] || frame[16] > 1) return false;
        var filename = Encoding.ASCII.GetString(frame[17..]);
        // Only a basename from the authenticated upload peer; never a path or a chat-token delimiter.
        if (filename.Length == 0 || filename.Contains("..") || Array.Exists(filename.ToCharArray(),
                c => !char.IsAsciiLetterOrDigit(c) && c is not ('_' or '-' or '.'))) return false;
        var size = BinaryPrimitives.ReadInt32LittleEndian(frame[11..]);
        var guild = BinaryPrimitives.ReadInt32LittleEndian(frame[7..]);
        if (size <= 0 || guild <= 0) return false;
        upload = new Upload(guild, size, filename, frame[16] == 1); return true;
    }
}
