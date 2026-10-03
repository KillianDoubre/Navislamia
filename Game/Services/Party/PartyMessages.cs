using System.Collections.Generic;
using System.Text;

namespace Navislamia.Game.Services.Party;

/// <summary>
/// The <c>@PARTY</c> system lines the Epic 7.3 client parses (<c>TS_SC_CHAT</c> type 100, sender
/// <c>@PARTY</c>). Every format is the official server's own (<c>CaptainHerlockServer.exe</c> of the
/// Epic 7 Part 4 dump, 2012-11 build, read with its PDB): the function that emits it is named on each
/// builder. <c>|</c> separates fields and ends every one, which is why a party name may never hold one.
/// See <c>docs/packet-specs/socle-groupe.md</c>.
/// </summary>
public static class PartyMessages
{
    public const string Sender = "@PARTY";

    /// <summary><c>onPartyCreate</c>: <c>CREATE|%s|%s|%d|</c> — party, leader, then 0.</summary>
    public static string Create(string party, string leader, int type = 0) => $"CREATE|{party}|{leader}|{type}|";

    /// <summary><c>onPartyInvite</c>: <c>INVITE|%s|%s|%d|%d|</c> — inviter, party, id, password.</summary>
    public static string Invite(string inviter, string party, int partyId, int password) =>
        $"INVITE|{inviter}|{party}|{partyId}|{password}|";

    /// <summary><c>onPartyJoin</c>, to the members already there, before the join: <c>NEW|%s|</c>.</summary>
    public static string New(string member) => $"NEW|{member}|";

    /// <summary><c>onPartyJoin</c>, to the one who joined: <c>JOIN|%s|</c> — the party.</summary>
    public static string Join(string party) => $"JOIN|{party}|";

    /// <summary><c>BroadcastPartyLeave</c>: <c>LEAVE|%s|</c> — the member leaving.</summary>
    public static string Leave(string member) => $"LEAVE|{member}|";

    /// <summary><c>onPartyKick</c>, to every member the kicked one included: <c>KICK|%s|%s|</c>.</summary>
    public static string Kick(string party, string member) => $"KICK|{party}|{member}|";

    /// <summary><c>onPartyPromote</c>: <c>PROMOTE|%s|</c> — the new leader.</summary>
    public static string Promote(string leader) => $"PROMOTE|{leader}|";

    /// <summary><c>BroadcastPartyDestroy</c>: <c>DESTROY|%s|</c> — the party.</summary>
    public static string Destroy(string party) => $"DESTROY|{party}|";

    /// <summary><c>onPartyShareMode</c>: <c>MODE|%d|</c>.</summary>
    public static string Mode(PartyShareMode mode) => $"MODE|{(int)mode}|";

    /// <summary><c>SendLoginResult</c>: <c>LOGIN|%s|%s|</c> — party, member.</summary>
    public static string Login(string party, string member) => $"LOGIN|{party}|{member}|";

    /// <summary><c>StructPlayer::LogoutNow</c>: <c>LOGOUT|%s|</c>.</summary>
    public static string Logout(string member) => $"LOGOUT|{member}|";

    /// <summary>
    /// <c>BroadcastPartyMemberInfo</c>: <c>MINFO|</c> then one member entry.
    /// </summary>
    public static string MemberInfo(PartyMemberView member) => "MINFO|" + Entry(member);

    /// <summary>
    /// <c>SendPartyInfoByID</c>: <c>PINFO|%d|%s|%s|%d|%d|%d|%d|</c> — id, party, leader, share mode, max
    /// level, min level, party type — then one entry per member, in the party's order.
    /// </summary>
    public static string PartyInfo(int partyId, string party, string leader, PartyShareMode mode, int maxLevel,
        int minLevel, IEnumerable<PartyMemberView> members, int type = 0)
    {
        var text = new StringBuilder($"PINFO|{partyId}|{party}|{leader}|{(int)mode}|{maxLevel}|{minLevel}|{type}|");
        foreach (var member in members)
        {
            text.Append(Entry(member));
        }

        return text.ToString();
    }

    /// <summary>
    /// One member: <c>%d|%s|%d|%d|%d|%d|%d|%d|%d|</c> online — handle, name, race, job, HP %, MP %, x, y,
    /// then 2 — and <c>0|%s|%d|%d|0|0|0|0|0|</c> offline, from the member tag (the functor of
    /// <c>SendPartyInfoByID</c>). The two offline integers are the tag's fields at +0x30 and +0x34, read
    /// here as level and job in NGemity's order (<c>PartyMemberTag</c>): not established by the binary.
    /// </summary>
    public static string Entry(PartyMemberView member) => member.Online
        ? $"{member.Handle}|{member.Name}|{member.Race}|{member.Job}|{member.HpPercent}|{member.MpPercent}|{member.X}|{member.Y}|2|"
        : $"0|{member.Name}|{member.Level}|{member.Job}|0|0|0|0|0|";

    /// <summary>
    /// A vital as a percentage, the official way: truncated to a byte, and 1 rather than 0 while the value
    /// is not zero (<c>BroadcastPartyMemberInfo</c>).
    /// </summary>
    public static int Percent(int value, int maximum)
    {
        if (value <= 0 || maximum <= 0)
        {
            return 0;
        }

        var percent = (byte)(int)(value * 100f / maximum);
        return percent == 0 ? 1 : percent;
    }
}

/// <summary><c>/pshare monopoly|random|linear</c> (<c>onPartyShareMode</c>).</summary>
public enum PartyShareMode
{
    Monopoly = 0,
    Random = 1,
    Linear = 2,
}

public readonly record struct PartyMemberView(bool Online, uint Handle, string Name, int Race, int Job, int Level,
    int HpPercent, int MpPercent, int X, int Y);
