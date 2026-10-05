using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Text;
using Navislamia.Game.Network.Packets.Enums;

namespace Navislamia.Game.Network.Packets.Game;

/// <summary>Epic 7.3 layouts measured in SFrame; see the 322, 451, 512, 514 and 3003/3004 sheets.</summary>
public static class GameSmallPackets
{
    public const int ScriptFrameLimit = 1024;
    public const int SkillLevelLimit = (4096 - 9) / 5;

    public static byte[] ShowSummonNameChange(uint handle) => HandleFrame(GamePackets.TM_SC_SHOW_SUMMON_NAME_CHANGE, handle);
    public static byte[] Target(uint handle) => HandleFrame(GamePackets.TM_SC_TARGET, handle);

    public static byte[] Sp(uint handle, int sp, int maxSp)
    {
        var frame = Frame(GamePackets.TM_SC_SP, 15);
        BinaryPrimitives.WriteUInt32LittleEndian(frame.AsSpan(7), handle);
        BinaryPrimitives.WriteInt16LittleEndian(frame.AsSpan(11), checked((short)sp));
        BinaryPrimitives.WriteInt16LittleEndian(frame.AsSpan(13), checked((short)maxSp));
        return frame;
    }

    // SendSkillLevelMessage sends the BASE level and no actor handle, cooldown or modification type.
    public static byte[] SkillLevels(IReadOnlyList<KeyValuePair<int, byte>> skills)
    {
        var count = Math.Min(skills.Count, SkillLevelLimit);
        var frame = Frame(GamePackets.TM_SC_SKILL_LEVEL_LIST, 9 + 5 * count);
        BinaryPrimitives.WriteUInt16LittleEndian(frame.AsSpan(7), (ushort)count);
        for (var i = 0; i < count; i++)
        {
            BinaryPrimitives.WriteInt32LittleEndian(frame.AsSpan(9 + 5 * i), skills[i].Key);
            frame[13 + 5 * i] = skills[i].Value;
        }
        return frame;
    }

    // Official SendWindowMessage / SendGeneralMessageBox silently ignore a frame above 1024 bytes.
    // Strings are UTF-8, counted in BYTES, concatenated without terminators.
    public static byte[] ShowWindow(string window, string argument, string trigger)
    {
        var w = CString(window); var a = CString(argument); var t = CString(trigger);
        if (13L + w.Length + a.Length + t.Length > ScriptFrameLimit) return null;
        var frame = Frame(GamePackets.TM_SC_SHOW_WINDOW, 13 + w.Length + a.Length + t.Length);
        BinaryPrimitives.WriteUInt16LittleEndian(frame.AsSpan(7), (ushort)w.Length);
        BinaryPrimitives.WriteUInt16LittleEndian(frame.AsSpan(9), (ushort)a.Length);
        BinaryPrimitives.WriteUInt16LittleEndian(frame.AsSpan(11), (ushort)t.Length);
        w.CopyTo(frame, 13); a.CopyTo(frame, 13 + w.Length); t.CopyTo(frame, 13 + w.Length + a.Length);
        return frame;
    }

    public static byte[] GeneralMessageBox(string text)
    {
        var bytes = CString(text);
        if (9L + bytes.Length > ScriptFrameLimit) return null;
        var frame = Frame(GamePackets.TM_SC_GENERAL_MESSAGE_BOX, 9 + bytes.Length);
        BinaryPrimitives.WriteUInt16LittleEndian(frame.AsSpan(7), (ushort)bytes.Length);
        bytes.CopyTo(frame, 9);
        return frame;
    }

    private static byte[] CString(string text)
    {
        text ??= string.Empty;
        var nul = text.IndexOf('\0');
        return Encoding.UTF8.GetBytes(nul < 0 ? text : text[..nul]);
    }

    private static byte[] HandleFrame(GamePackets id, uint handle)
    {
        var frame = Frame(id, 11);
        BinaryPrimitives.WriteUInt32LittleEndian(frame.AsSpan(7), handle);
        return frame;
    }

    private static byte[] Frame(GamePackets id, int length)
    {
        var frame = new byte[length];
        BinaryPrimitives.WriteUInt32LittleEndian(frame, (uint)length);
        BinaryPrimitives.WriteUInt16LittleEndian(frame.AsSpan(4), (ushort)id);
        for (var i = 0; i < 6; i++) frame[6] = unchecked((byte)(frame[6] + frame[i]));
        return frame;
    }
}
