using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using Navislamia.Game.DataAccess.Repositories.Interfaces;
using Navislamia.Game.Network.Clients;
using Navislamia.Game.Network.Packets.Enums;
using Navislamia.Game.Network.Packets.Game;
using Navislamia.Game.Services.Interfaces;
using Serilog;

namespace Navislamia.Game.Services.Party;

public sealed record DungeonParty(long Id, uint Leader, IReadOnlyList<uint> Members, IReadOnlyList<GameClient> Online,
    string Name = "", int Type = 0, long AttackGuild = 0);

/// <summary>
/// Parties, driven entirely by chat commands and <c>@PARTY</c> system lines — the Epic 7.3 client has no
/// party opcode: its party window sends <c>/pcreate</c>, <c>/pinvite</c>, <c>/pjoin %d %d</c>... and
/// parses the answers (<see cref="PartyMessages"/>). The rules are the official server's command handlers
/// (<c>onPartyCreate</c>, <c>onPartyInvite</c>, <c>onPartyJoin</c>, <c>onPartyLeave</c>,
/// <c>onPartyKick</c>, <c>onPartyPromote</c>, <c>onPartyDestroy</c>, <c>onPartyShareMode</c>).
/// </summary>
public interface IPartyService
{
    int CreateAttackParty(GameClient client, string name, long guild, int type) => 0;
    void DisbandAttackParty(long id) { }
    bool AttackPartyExists(long id) => false;
    DungeonParty DungeonParty(GameClient client) => null;
    /// <summary>Runs a party chat command; false when the line is not one, so it goes on to the GM commands.</summary>
    bool TryHandleCommand(GameClient client, string message);

    /// <summary>At the end of world entry: back into the party the character belongs to, if any.</summary>
    void OnWorldEntry(GameClient client);

    /// <summary>On the way to the lobby or out: the member stays in the party, offline.</summary>
    void OnWorldExit(GameClient client);

    /// <summary>HP or MP changed: the members see the new percentages when they moved.</summary>
    void OnVitalsChanged(GameClient client);

    /// <summary>A member changed job: its entry is broadcast again.</summary>
    void OnJobChanged(GameClient client) { }

    /// <summary>
    /// A system line to the member's party (<c>PrintfPartyChatMessage(CHAT_PARTY_SYSTEM, …)</c>, sender
    /// <c>@PARTY</c>); false when the client is in no party, so the caller sends it to the player alone.
    /// </summary>
    bool TrySendPartyLine(GameClient member, string text) => false;

    IReadOnlyList<GameClient> RewardMembers(GameClient killer, float x, float y, byte layer);
    int MemberCount(GameClient client) => 1;
    bool CanTakeDrop(GameClient owner, GameClient picker, long? dropPartyId);
    GameClient LootRecipient(GameClient picker, long? dropPartyId, float x, float y, byte layer);

    // The HuntaHolic room's party (PartyManager::TYPE_HUNTAHOLIC_PARTY), driven by HuntaholicService rather than
    // by chat commands, which ignore it (socle-huntaholic.md §4).
    int CreateHuntaholicParty(GameClient leader, string baseName) => 0;
    bool JoinHuntaholicParty(int partyId, GameClient member) => false;
    void LeaveHuntaholicParty(GameClient member) { }
    void DestroyHuntaholicParty(int partyId) { }
    IReadOnlyList<GameClient> OnlineMembers(int partyId) => System.Array.Empty<GameClient>();
    int PartyMemberCount(int partyId) => 0;
    bool IsPartyLeader(int partyId, GameClient client) => false;
    string PartyName(int partyId) => string.Empty;
    int PartyIdOf(GameClient client) => 0;

    /// <summary><c>onInstanceGameEnter</c>: the player leaves (or, alone, destroys) the party they are in.</summary>
    void LeaveForInstanceGame(GameClient client) { }
}

public sealed partial class PartyService : IPartyService
{
    private readonly Guilds.GuildRuntime _guilds;
    public bool AttackPartyExists(long id)
    {
        lock (_gate) return _parties.TryGetValue((int)id, out var party) && party.AttackGuild != 0;
    }
    public int CreateAttackParty(GameClient client, string name, long guild, int type)
    {
        lock (_gate)
        {
            if (type is not (1 or 2) || _partyOf.ContainsKey(client.ConnectionInfo.CharacterHandle)) return 0;
            Create(client, new[] { "/pcreate", name }, guild, type);
            return _partyOf.TryGetValue(client.ConnectionInfo.CharacterHandle, out var id) ? id : 0;
        }
    }
    public void DisbandAttackParty(long id)
    {
        lock (_gate)
        {
            if (!_parties.TryGetValue((int)id, out var party) || party.AttackGuild == 0) return;
            SendToParty(party, PartyMessages.Destroy(party.Name));
            foreach (var member in party.Members)
            {
                _partyOf.Remove(member.CharacterId);
                var online = Online(member.CharacterId);
                if (online is not null) online.ConnectionInfo.PartyId = null;
            }
            _parties.Remove(party.Id);
        }
    }
    public DungeonParty DungeonParty(GameClient client)
    {
        lock (_gate)
        {
            if (!TryGetParty(client.ConnectionInfo.CharacterHandle, out var party)) return null;
            return new DungeonParty(party.Id, (uint)party.LeaderId,
                party.Members.Select(m => (uint)m.CharacterId).ToArray(),
                party.Members.Select(m => Online(m.CharacterId)).Where(c => c is not null).ToArray(), party.Name, party.Type, party.AttackGuild);
        }
    }
    public int MemberCount(GameClient client)
    {
        lock (_gate) return TryGetParty(client.ConnectionInfo.CharacterHandle, out var party) ? party.Members.Count : 1;
    }
    /// <summary><c>onPartyInvite</c> and <c>onPartyJoin</c> refuse at <c>GetMemberCount &gt;= 8</c>.</summary>
    public const int MaxMembers = 8;

    /// <summary><c>onPartyCreate</c> ignores a longer name and checks <c>IsValidName(name, …, 1, 30)</c>.</summary>
    public const int MaxNameLength = 30;

    private readonly ILogger _logger = Log.ForContext<PartyService>();
    private readonly IPlayerVisibilityService _players;
    private readonly IStatService _stats;
    private readonly IBannedWordsRepository _bannedWords;
    private readonly object _gate = new();
    private readonly Dictionary<int, PartyState> _parties = new();
    private readonly Dictionary<long, int> _partyOf = new();
    private int _nextPartyId;

    public PartyService(IPlayerVisibilityService players, IStatService stats, IBannedWordsRepository bannedWords,
        Guilds.GuildRuntime guilds = null, Huntaholic.IHuntaholicCatalog huntaholics = null)
    {
        _guilds = guilds;
        _huntaholics = huntaholics;
        _players = players;
        _stats = stats;
        _bannedWords = bannedWords;
    }

    public bool TryHandleCommand(GameClient client, string message)
    {
        var tokens = message.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (tokens.Length == 0)
        {
            return false;
        }

        Action<GameClient, string[]> handler = tokens[0].ToLowerInvariant() switch
        {
            "/pcreate" => Create,
            "/pinvite" => Invite,
            "/pjoin" => Join,
            "/pleave" => (c, _) => Leave(c),
            "/pkick" => Kick,
            "/ppromote" => Promote,
            "/pdestroy" => (c, _) => Destroy(c),
            "/pshare" => Share,
            "/plist" or "/pinfo" => (c, _) => SendPartyInfo(c),
            _ => null
        };

        if (handler is null)
        {
            return false;
        }

        if (client.ConnectionInfo.CharacterHandle != 0)
        {
            lock (_gate)
            {
                if (!IsRefusedByHuntaholic(client, tokens))
                {
                    handler(client, tokens);
                }
            }
        }

        return true;
    }

    public void OnWorldEntry(GameClient client)
    {
        var info = client.ConnectionInfo;
        lock (_gate)
        {
            if (!TryGetParty(info.CharacterHandle, out var party))
            {
                info.PartyId = null;
                return;
            }

            // SendLoginResult: LOGIN to the party (the member is online already, so they get it too), the
            // member's MINFO, then the whole PINFO to them.
            var member = party.Find(info.CharacterHandle);
            Remember(member, info);
            info.PartyId = party.Id;
            SendToParty(party, PartyMessages.Login(party.Name, member.Name));
            BroadcastMemberInfo(party, client);
            SendPartyInfo(client, party);
        }
    }

    public void OnWorldExit(GameClient client)
    {
        var info = client.ConnectionInfo;
        lock (_gate)
        {
            if (!TryGetParty(info.CharacterHandle, out var party))
            {
                return;
            }

            Remember(party.Find(info.CharacterHandle), info);
            SendToParty(party, PartyMessages.Logout(info.CharacterName), except: info.CharacterHandle);
        }
    }

    public void OnVitalsChanged(GameClient client)
    {
        var info = client.ConnectionInfo;
        lock (_gate)
        {
            if (!TryGetParty(info.CharacterHandle, out var party))
            {
                return;
            }

            var member = party.Find(info.CharacterHandle);
            var (hp, mp) = Percentages(info);
            if (hp == member.LastHpPercent && mp == member.LastMpPercent)
            {
                return;
            }

            BroadcastMemberInfo(party, client);
        }
    }

    /// <summary>
    /// A member changed job: <c>PartyManager::OnChangeCharacterJob</c> refreshes the member's entry, whose job
    /// field the party window shows.
    /// </summary>
    public bool TrySendPartyLine(GameClient member, string text)
    {
        lock (_gate)
        {
            if (!TryGetParty(member.ConnectionInfo.CharacterHandle, out var party))
            {
                return false;
            }

            SendToParty(party, text);
            return true;
        }
    }

    public void OnJobChanged(GameClient client)
    {
        lock (_gate)
        {
            if (TryGetParty(client.ConnectionInfo.CharacterHandle, out var party))
            {
                BroadcastMemberInfo(party, client);
            }
        }
    }

    /// <summary>Only online members close enough to the kill share its experience, JP and gold.</summary>
    public IReadOnlyList<GameClient> RewardMembers(GameClient killer, float x, float y, byte layer)
    {
        lock (_gate)
        {
            if (!TryGetParty(killer.ConnectionInfo.CharacterHandle, out var party))
                return new[] { killer };
            return NearbyMembers(party, x, y, layer).ToArray();
        }
    }

    public bool CanTakeDrop(GameClient owner, GameClient picker, long? dropPartyId)
    {
        if (ReferenceEquals(owner, picker)) return true;
        lock (_gate)
        {
            return dropPartyId is not null && TryGetParty(owner.ConnectionInfo.CharacterHandle, out var party)
                && party.Id == dropPartyId && _partyOf.TryGetValue(picker.ConnectionInfo.CharacterHandle, out var id)
                && id == party.Id && ReferenceEquals(Online(picker.ConnectionInfo.CharacterHandle), picker);
        }
    }

    /// <summary>Monopoly gives the item to its picker; random and linear assign it among nearby members.</summary>
    public GameClient LootRecipient(GameClient picker, long? dropPartyId, float x, float y, byte layer)
    {
        lock (_gate)
        {
            if (dropPartyId is null || !TryGetParty(picker.ConnectionInfo.CharacterHandle, out var party)
                || party.Id != dropPartyId || party.ShareMode == PartyShareMode.Monopoly)
                return picker;

            var members = NearbyMembers(party, x, y, layer).ToArray();
            if (members.Length == 0) return picker;
            var index = party.ShareMode == PartyShareMode.Random
                ? RandomNumberGenerator.GetInt32(members.Length)
                : party.NextLootIndex++ % members.Length;
            return members[index];
        }
    }

    private IEnumerable<GameClient> NearbyMembers(PartyState party, float x, float y, byte layer)
    {
        foreach (var member in party.Members)
        {
            var client = Online(member.CharacterId);
            if (client is null) continue;
            var info = client.ConnectionInfo;
            var dx = info.X - x;
            var dy = info.Y - y;
            if (info.Layer == layer && dx * dx + dy * dy <= WorldVisibility.ViewRange * WorldVisibility.ViewRange)
                yield return client;
        }
    }

    private void Create(GameClient client, string[] tokens) => Create(client, tokens, 0, 0);
    private void Create(GameClient client, string[] tokens, long guild, int type)
    {
        var info = client.ConnectionInfo;
        if (tokens.Length < 2)
        {
            return;
        }

        var name = tokens[1];
        if (name.Length > MaxNameLength)
        {
            return;
        }

        if (_partyOf.ContainsKey(info.CharacterHandle))
        {
            Reply(client, "ERROR_ALREADY_PARTY_MEMBER");
            return;
        }

        if (!PartyRules.IsValidName(name) || _bannedWords.IsBannedWord(name))
        {
            Reply(client, "INVALID_PARTY_NAME");
            return;
        }

        if (_parties.Values.Any(party => string.Equals(party.Name, name, StringComparison.OrdinalIgnoreCase)))
        {
            Reply(client, "ALREADY_EXIST");
            return;
        }

        var created = new PartyState(++_nextPartyId, name, NewPassword(), info.CharacterHandle);
        created.AttackGuild = guild; created.Type = type;
        var member = new PartyMember(info.CharacterHandle, info.CharacterName);
        Remember(member, info);
        created.Members.Add(member);
        _parties[created.Id] = created;
        _partyOf[info.CharacterHandle] = created.Id;
        info.PartyId = created.Id;

        Reply(client, PartyMessages.Create(name, info.CharacterName, type));
        SendPartyInfo(client, created);
        _logger.Debug("{name} created party {party} ({id})", info.CharacterName, name, created.Id);
    }

    private void Invite(GameClient client, string[] tokens)
    {
        var info = client.ConnectionInfo;
        if (tokens.Length < 2 || !TryGetParty(info.CharacterHandle, out var party)
            || party.LeaderId != info.CharacterHandle)
        {
            return;
        }

        var target = FindOnline(tokens[1]);
        if (target is null)
        {
            return;
        }
        if (party.AttackGuild != 0 && _guilds?.Effective(target.ConnectionInfo.GuildId) != party.AttackGuild) return;

        if (party.Members.Count >= MaxMembers)
        {
            Reply(client, "ERROR_MAX");
            return;
        }

        if (_partyOf.ContainsKey(target.ConnectionInfo.CharacterHandle))
        {
            Reply(client, "ERROR_TARGET_ANOTHER");
            return;
        }

        Reply(target, PartyMessages.Invite(info.CharacterName, party.Name, party.Id, party.Password));
    }

    private void Join(GameClient client, string[] tokens)
    {
        var info = client.ConnectionInfo;
        if (tokens.Length < 2)
        {
            return;
        }

        if (_partyOf.ContainsKey(info.CharacterHandle))
        {
            Reply(client, "ERROR_YOU_CAN_JOIN_ONLY_ONE_PARTY");
            return;
        }

        if (!int.TryParse(tokens[1], out var partyId) || !_parties.TryGetValue(partyId, out var party))
        {
            Reply(client, "ERROR_INVALID_PARTY");
            return;
        }

        if (tokens.Length < 3 || !int.TryParse(tokens[2], out var password) || password != party.Password)
        {
            Reply(client, "HAS_NO_AUTHORITY");
            return;
        }
        if (party.AttackGuild != 0 && _guilds?.Effective(info.GuildId) != party.AttackGuild) return;

        // The official handler announces NEW before it checks the size and can then refuse: the size is
        // checked first here, so the members are never told about a join that does not happen.
        if (party.Members.Count >= MaxMembers)
        {
            Reply(client, "ERROR_MAX");
            return;
        }

        SendToParty(party, PartyMessages.New(info.CharacterName));

        var member = new PartyMember(info.CharacterHandle, info.CharacterName);
        Remember(member, info);
        party.Members.Add(member);
        _partyOf[info.CharacterHandle] = party.Id;
        info.PartyId = party.Id;

        Reply(client, PartyMessages.Join(party.Name));
        SendPartyInfo(client, party);
        BroadcastMemberInfo(party, client);
    }

    private void Leave(GameClient client)
    {
        var info = client.ConnectionInfo;

        // onPartyLeave: the leader cannot leave, they destroy the party or promote someone first.
        if (!TryGetParty(info.CharacterHandle, out var party) || party.LeaderId == info.CharacterHandle)
        {
            return;
        }

        SendToParty(party, PartyMessages.Leave(info.CharacterName));
        RemoveMember(party, info.CharacterHandle);
    }

    private void Kick(GameClient client, string[] tokens)
    {
        var info = client.ConnectionInfo;
        if (tokens.Length < 2 || !TryGetParty(info.CharacterHandle, out var party)
            || party.LeaderId != info.CharacterHandle)
        {
            return;
        }

        // By name, online or not (IsMember), never the leader.
        var member = party.Members.FirstOrDefault(m =>
            string.Equals(m.Name, tokens[1], StringComparison.OrdinalIgnoreCase));
        if (member is null || member.CharacterId == party.LeaderId)
        {
            return;
        }

        SendToParty(party, PartyMessages.Kick(party.Name, member.Name));
        RemoveMember(party, member.CharacterId);
    }

    private void Promote(GameClient client, string[] tokens)
    {
        var info = client.ConnectionInfo;
        if (tokens.Length < 2 || !TryGetParty(info.CharacterHandle, out var party)
            || party.LeaderId != info.CharacterHandle)
        {
            return;
        }

        // The new leader must be online and in this party (FindPlayer, then their party id).
        var target = FindOnline(tokens[1]);
        if (target is null || !_partyOf.TryGetValue(target.ConnectionInfo.CharacterHandle, out var targetParty)
            || targetParty != party.Id)
        {
            return;
        }

        SendToParty(party, PartyMessages.Promote(target.ConnectionInfo.CharacterName));
        party.LeaderId = target.ConnectionInfo.CharacterHandle;
    }

    private void Destroy(GameClient client)
    {
        var info = client.ConnectionInfo;
        if (!TryGetParty(info.CharacterHandle, out var party) || party.LeaderId != info.CharacterHandle)
        {
            return;
        }

        SendToParty(party, PartyMessages.Destroy(party.Name));
        foreach (var member in party.Members.ToArray())
        {
            RemoveMember(party, member.CharacterId);
        }
    }

    private void Share(GameClient client, string[] tokens)
    {
        var info = client.ConnectionInfo;
        if (tokens.Length < 2 || !TryGetParty(info.CharacterHandle, out var party)
            || party.LeaderId != info.CharacterHandle)
        {
            return;
        }

        PartyShareMode? mode = tokens[1].ToLowerInvariant() switch
        {
            "monopoly" => PartyShareMode.Monopoly,
            "random" => PartyShareMode.Random,
            "linear" => PartyShareMode.Linear,
            _ => null
        };
        if (mode is null)
        {
            return;
        }

        party.ShareMode = mode.Value;
        SendToParty(party, PartyMessages.Mode(mode.Value));
    }

    private void SendPartyInfo(GameClient client)
    {
        if (TryGetParty(client.ConnectionInfo.CharacterHandle, out var party))
        {
            SendPartyInfo(client, party);
        }
    }

    private void SendPartyInfo(GameClient client, PartyState party)
    {
        var views = party.Members.Select(View).ToList();
        var online = views.Where(view => view.Online).Select(view => view.Level).DefaultIfEmpty(0).ToList();
        var leader = party.Find(party.LeaderId)?.Name ?? string.Empty;
        Reply(client, PartyMessages.PartyInfo(party.Id, party.Name, leader, party.ShareMode, online.Max(),
            online.Min(), views, party.Type));
    }

    private void BroadcastMemberInfo(PartyState party, GameClient subject)
    {
        var member = party.Find(subject.ConnectionInfo.CharacterHandle);
        var view = View(member);
        member.LastHpPercent = view.HpPercent;
        member.LastMpPercent = view.MpPercent;
        SendToParty(party, PartyMessages.MemberInfo(view));
    }

    private PartyMemberView View(PartyMember member)
    {
        var client = Online(member.CharacterId);
        if (client is null)
        {
            return new PartyMemberView(false, 0, member.Name, member.Race, member.Job, member.Level, 0, 0, 0, 0);
        }

        var info = client.ConnectionInfo;
        Remember(member, info);
        var (hp, mp) = Percentages(info);
        return new PartyMemberView(true, info.CharacterHandle, member.Name, info.CharacterRace, info.CharacterJob,
            info.CharacterLevel, hp, mp, (int)info.X, (int)info.Y);
    }

    private (int Hp, int Mp) Percentages(ConnectionInfo info)
    {
        var maxHp = info.CharacterMaxHp;
        var maxMp = info.CharacterMp;
        if (_stats is not null && _stats.Compute(info).Total is { } total)
        {
            maxHp = (int)total.MaxHp;
            maxMp = (int)total.MaxMp;
        }

        return (PartyMessages.Percent(info.CharacterHp, Math.Max(maxHp, info.CharacterHp)),
            PartyMessages.Percent(info.CharacterMp, Math.Max(maxMp, info.CharacterMp)));
    }

    private void RemoveMember(PartyState party, long characterId)
    {
        party.Members.RemoveAll(member => member.CharacterId == characterId);
        _partyOf.Remove(characterId);
        if (Online(characterId) is { } client)
        {
            client.ConnectionInfo.PartyId = null;
        }

        if (party.Members.Count == 0)
        {
            _parties.Remove(party.Id);
        }
    }

    private void SendToParty(PartyState party, string text, long except = 0)
    {
        foreach (var member in party.Members)
        {
            if (member.CharacterId != except && Online(member.CharacterId) is { } client)
            {
                Reply(client, text);
            }
        }
    }

    private GameClient Online(long characterId) =>
        _players.Registry.TryResolve((uint)characterId, out var client)
        && client.ConnectionInfo.CharacterHandle == (uint)characterId
            ? client
            : null;

    private GameClient FindOnline(string name) => _players.Registry.Clients.FirstOrDefault(client =>
        client.ConnectionInfo.CharacterHandle != 0
        && string.Equals(client.ConnectionInfo.CharacterName, name, StringComparison.OrdinalIgnoreCase));

    private bool TryGetParty(long characterId, out PartyState party)
    {
        party = null;
        return characterId != 0 && _partyOf.TryGetValue(characterId, out var id) && _parties.TryGetValue(id, out party);
    }

    private static void Remember(PartyMember member, ConnectionInfo info)
    {
        member.Level = info.CharacterLevel;
        member.Job = info.CharacterJob;
        member.Race = info.CharacterRace;
    }

    private static void Reply(GameClient client, string text) =>
        client.Connection.Send(GameChatPackets.BuildChat(PartyMessages.Sender, (byte)ChatType.PartySystem, text));

    /// <summary>The number <c>/pjoin</c> must repeat: what makes an invitation the only way in.</summary>
    private static int NewPassword() => RandomNumberGenerator.GetInt32(1, int.MaxValue);

    private sealed class PartyState
    {
        public long AttackGuild { get; set; }
        public int Type { get; set; }
        public PartyState(int id, string name, int password, long leaderId)
        {
            Id = id;
            Name = name;
            Password = password;
            LeaderId = leaderId;
        }

        public int Id { get; }
        public string Name { get; }
        public int Password { get; }
        public long LeaderId { get; set; }
        public PartyShareMode ShareMode { get; set; }
        public int NextLootIndex { get; set; }
        public bool Huntaholic => Type == HuntaholicPartyType;
        public List<PartyMember> Members { get; } = new();

        public PartyMember Find(long characterId) => Members.FirstOrDefault(m => m.CharacterId == characterId);
    }

    private sealed class PartyMember
    {
        public PartyMember(long characterId, string name)
        {
            CharacterId = characterId;
            Name = name;
        }

        public long CharacterId { get; }
        public string Name { get; }
        public int Level { get; set; }
        public int Job { get; set; }
        public int Race { get; set; }
        public int LastHpPercent { get; set; } = -1;
        public int LastMpPercent { get; set; } = -1;
    }
}

public static class PartyRules
{
    /// <summary>
    /// 1 to 30 letters or digits. <c>GameRule::IsValidName</c>'s character set is not read; this is the
    /// safe subset, and it can never hold <c>|</c>, the field separator every <c>@PARTY</c> line relies on.
    /// </summary>
    public static bool IsValidName(string name) =>
        !string.IsNullOrEmpty(name) && name.Length <= PartyService.MaxNameLength && name.All(char.IsAsciiLetterOrDigit);
}
