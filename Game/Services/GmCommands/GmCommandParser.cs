using System;
using Navislamia.Game.Network.Packets.Enums;

namespace Navislamia.Game.Services.GmCommands;

/// <summary>
/// One chat line read as a command: <see cref="Name"/> is the word after the slash, lower-cased;
/// <see cref="Args"/> the whitespace-separated words that follow; <see cref="Rest"/> everything after the
/// name, trimmed, for the commands that take free text (<c>/notice</c>).
/// </summary>
public readonly record struct GmCommandLine(string Name, string[] Args, string Rest);

/// <summary>
/// Recognises a GM command in a <c>TM_CS_CHAT_REQUEST</c>. The rule is NGemity's
/// (<c>WorldSession::onChatRequest</c>): any line whose first character is <c>/</c> and whose chat type is
/// not a whisper is a command and is never echoed as chat. The 7.3 client forwards such a line to the
/// server — typing <c>/position</c> came back as an echo before this module existed. See
/// docs/gm-commands.md.
/// </summary>
public static class GmCommandParser
{
    public const char Prefix = '/';

    private static readonly char[] Separators = { ' ', '\t' };

    /// <summary>Whether a chat line is a command rather than a message to relay.</summary>
    public static bool IsCommand(byte chatType, string message)
    {
        if (chatType == (byte)ChatType.Whisper || string.IsNullOrEmpty(message))
        {
            return false;
        }

        return Clean(message).StartsWith(Prefix);
    }

    /// <summary>
    /// Splits a command line. Case-insensitive on the name, unlike NGemity's exact comparison: nothing in
    /// the client distinguishes <c>/Position</c> from <c>/position</c>, and refusing one would only look
    /// like a broken command.
    /// </summary>
    public static bool TryParse(string message, out GmCommandLine line)
    {
        line = default;
        var text = Clean(message ?? string.Empty);
        if (text.Length < 2 || text[0] != Prefix)
        {
            return false;
        }

        var body = text.Substring(1);
        var space = body.IndexOfAny(Separators);
        var name = (space < 0 ? body : body.Substring(0, space)).ToLowerInvariant();
        if (name.Length == 0)
        {
            return false;
        }

        var rest = space < 0 ? string.Empty : body.Substring(space + 1).Trim();
        var args = rest.Length == 0
            ? Array.Empty<string>()
            : rest.Split(Separators, StringSplitOptions.RemoveEmptyEntries);

        line = new GmCommandLine(name, args, rest);
        return true;
    }

    /// <summary>The chat buffer carries the NUL terminator in its count; it is not part of the text.</summary>
    private static string Clean(string message) => message.TrimEnd('\0').Trim();
}
