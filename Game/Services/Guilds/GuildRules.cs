using System;
using System.Linq;
using Navislamia.Game.DataAccess.Entities.Enums;
using Navislamia.Game.DataAccess.Entities.Telecaster;
using Navislamia.Game.Network.Clients;

namespace Navislamia.Game.Services.Guilds;

public static class GuildRules
{
    public const int MaxMembers = 200;
    public const long CreationCost = 100000;
    public const long MaxGold = GoldRules.MaxCarried;
    public const GuildPermissions All = (GuildPermissions)32767;
    public static readonly TimeSpan RejoinDelay = TimeSpan.FromDays(7);
    public static readonly int[] Officers = { 1012, 2012, 4012, 6012, 7037 };
    public static string Normalize(string name) => name?.Trim().ToUpperInvariant();
    // These fields enter pipe-delimited client messages and Lua-looking dialog triggers. Accept ASCII
    // letters, digits and spaces only; no delimiters, quoting, controls or code can be smuggled through them.
    public static bool ValidName(string name) => name is { Length: >= 1 and <= 30 }
        && name == name.Trim() && name.All(c => char.IsAsciiLetterOrDigit(c) || c == ' ');
    public static bool ValidText(string text, int limit) => text is not null && text.Length <= limit
        && text.All(c => c >= ' ' && c <= '~' && c != '|' && c != '\\');
    public static bool Permitted(GuildEntity guild, CharacterEntity member, GuildPermissions permission) =>
        guild is not null && member.GuildId == guild.Id && (guild.LeaderId == member.Id
            || member.GuildPermission is >= 1 and <= 6 && guild.PermissionSets is { Length: 6 }
                && (guild.PermissionSets[member.GuildPermission - 1] & permission) != 0);
    public static long Tax(long amount, int rate) => (long)((decimal)Math.Max(0, amount) * Math.Clamp(rate, 0, 10) / 100m);
    public static bool SameAlliance(ConnectionInfo first, ConnectionInfo second) =>
        first.AllianceId is > 0 && first.AllianceId == second.AllianceId;
}
