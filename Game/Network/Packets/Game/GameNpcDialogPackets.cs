using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Text;
using Navislamia.Configuration.Options;
using Navislamia.Game.Network.Packets.Enums;

namespace Navislamia.Game.Network.Packets.Game;

public static class GameNpcDialogPackets
{
    private const int HeaderSize = 7;
    private const int SelectionHeaderSize = 9;
    private const int DialogHeaderSize = 21;
    private const int DialogHandleOffset = 11;

    public static bool TryReadContact(ReadOnlySpan<byte> packet, out uint handle)
    {
        handle = 0;
        if (packet.Length != 11)
        {
            return false;
        }

        handle = BinaryPrimitives.ReadUInt32LittleEndian(packet.Slice(HeaderSize, 4));
        return handle != 0;
    }

    public static bool TryReadSelection(ReadOnlySpan<byte> packet, int maxTriggerLength, out string trigger)
    {
        trigger = string.Empty;
        if (packet.Length < SelectionHeaderSize || maxTriggerLength < 0)
        {
            return false;
        }

        var length = BinaryPrimitives.ReadUInt16LittleEndian(packet.Slice(HeaderSize, 2));
        if (length > maxTriggerLength || packet.Length != SelectionHeaderSize + length)
        {
            return false;
        }

        trigger = Encoding.ASCII.GetString(packet.Slice(SelectionHeaderSize, length));
        return true;
    }

    public static byte[] BuildDialog(uint npcHandle, string title, string text,
        IReadOnlyCollection<NpcDialogMenuEntry> menu, int type = 0)
    {
        var titleBytes = Encoding.ASCII.GetBytes(title ?? string.Empty);
        var textBytes = Encoding.ASCII.GetBytes(text ?? string.Empty);
        var menuBytes = Encoding.ASCII.GetBytes(BuildMenu(menu));

        if (titleBytes.Length > ushort.MaxValue || textBytes.Length > ushort.MaxValue ||
            menuBytes.Length > ushort.MaxValue)
        {
            throw new ArgumentException("NPC dialog fields must fit in a UInt16 length");
        }

        var packet = new byte[DialogHeaderSize + titleBytes.Length + textBytes.Length + menuBytes.Length];
        var span = packet.AsSpan();
        BinaryPrimitives.WriteUInt32LittleEndian(span.Slice(0, 4), (uint)packet.Length);
        BinaryPrimitives.WriteUInt16LittleEndian(span.Slice(4, 2), (ushort)GamePackets.TM_SC_DIALOG);
        BinaryPrimitives.WriteInt32LittleEndian(span.Slice(7, 4), type);
        BinaryPrimitives.WriteUInt32LittleEndian(span.Slice(DialogHandleOffset, 4), npcHandle);
        BinaryPrimitives.WriteUInt16LittleEndian(span.Slice(15, 2), (ushort)titleBytes.Length);
        BinaryPrimitives.WriteUInt16LittleEndian(span.Slice(17, 2), (ushort)textBytes.Length);
        BinaryPrimitives.WriteUInt16LittleEndian(span.Slice(19, 2), (ushort)menuBytes.Length);

        var offset = DialogHeaderSize;
        titleBytes.CopyTo(span.Slice(offset));
        offset += titleBytes.Length;
        textBytes.CopyTo(span.Slice(offset));
        offset += textBytes.Length;
        menuBytes.CopyTo(span.Slice(offset));
        WriteChecksum(packet);
        return packet;
    }

    public static byte[] CopyWithNpcHandle(byte[] dialogTemplate, uint npcHandle)
    {
        if (dialogTemplate == null || dialogTemplate.Length < DialogHeaderSize)
        {
            throw new ArgumentException("A complete NPC dialog packet is required", nameof(dialogTemplate));
        }

        var packet = (byte[])dialogTemplate.Clone();
        BinaryPrimitives.WriteUInt32LittleEndian(packet.AsSpan(DialogHandleOffset, 4), npcHandle);
        return packet;
    }

    private static string BuildMenu(IReadOnlyCollection<NpcDialogMenuEntry> entries)
    {
        if (entries.Count == 0)
        {
            return string.Empty;
        }

        var builder = new StringBuilder();
        foreach (var entry in entries)
        {
            if (entry.Label.Contains('\t') || entry.Trigger.Contains('\t'))
            {
                continue;
            }

            builder.Append('\t').Append(entry.Label).Append('\t').Append(entry.Trigger).Append('\t');
        }

        return builder.ToString();
    }

    private static void WriteChecksum(byte[] packet)
    {
        byte checksum = 0;
        for (var i = 0; i < 6; i++)
        {
            checksum += packet[i];
        }

        packet[6] = checksum;
    }
}
