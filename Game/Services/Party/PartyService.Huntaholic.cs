using System;
using System.Collections.Generic;
using System.Linq;
using Navislamia.Game.Network.Clients;

namespace Navislamia.Game.Services.Party;

/// <summary>
/// The HuntaHolic room's party (<c>PartyManager::TYPE_HUNTAHOLIC_PARTY</c>, socle-huntaholic.md §4): made, joined,
/// left and destroyed by <c>HuntaholicManager</c>, never by a chat command — the official command handlers return
/// early on this party type, and refuse to create, join or invite inside the HuntaHolic area.
/// </summary>
public sealed partial class PartyService
{
    /// <summary><c>PartyManager::TYPE_HUNTAHOLIC_PARTY</c>, the type field of <c>PINFO</c>.</summary>
    public const int HuntaholicPartyType = 3;

    private readonly Huntaholic.IHuntaholicCatalog _huntaholics;

    /// <summary>
    /// <c>onPartyShareMode</c>/<c>Destroy</c>/<c>Invite</c>/<c>Kick</c>/<c>Leave</c>/<c>Promote</c> return in silence
    /// on a HuntaHolic party; <c>onPartyCreate</c>/<c>Join</c> answer <c>ERROR_IN_HUNTAHOLIC</c> inside the area and
    /// <c>onPartyInvite</c> <c>ERROR_TARGET_IN_HUNTAHOLIC</c> for a target there. Called under <c>_gate</c>.
    /// </summary>
    private bool IsRefusedByHuntaholic(GameClient client, string[] tokens)
    {
        var command = tokens[0].ToLowerInvariant();
        var info = client.ConnectionInfo;
        var inHuntaholicParty = TryGetParty(info.CharacterHandle, out var party) && party.Huntaholic;

        if (inHuntaholicParty && command is "/pshare" or "/pdestroy" or "/pinvite" or "/pkick" or "/pleave" or "/ppromote")
        {
            return true;
        }

        if (_huntaholics is null)
        {
            return false;
        }

        if (command is "/pcreate" or "/pjoin" && _huntaholics.GetHuntaholicId(info.X, info.Y) != 0)
        {
            Reply(client, "ERROR_IN_HUNTAHOLIC");
            return true;
        }

        if (command == "/pinvite" && tokens.Length >= 2 && FindOnline(tokens[1]) is { } target
            && _huntaholics.GetHuntaholicId(target.ConnectionInfo.X, target.ConnectionInfo.Y) != 0)
        {
            Reply(client, "ERROR_TARGET_IN_HUNTAHOLIC");
            return true;
        }

        return false;
    }

    /// <summary>
    /// <c>CreateInstanceDungeon</c>: a party named <c>name&lt;N_h&gt;</c> with the first free N, so it never collides
    /// with a player's party; the leader joins it and gets its <c>PINFO</c>. 0 when the player is in a party already.
    /// </summary>
    public int CreateHuntaholicParty(GameClient leader, string baseName)
    {
        var info = leader.ConnectionInfo;
        lock (_gate)
        {
            if (info.CharacterHandle == 0 || _partyOf.ContainsKey(info.CharacterHandle))
            {
                return 0;
            }

            string name = null;
            for (var suffix = 0; suffix < Huntaholic.HuntaholicRules.MaxInstanceCount; suffix++)
            {
                var candidate = $"{baseName}<{suffix}_h>";
                if (!_parties.Values.Any(p => string.Equals(p.Name, candidate, StringComparison.OrdinalIgnoreCase)))
                {
                    name = candidate;
                    break;
                }
            }

            if (name is null)
            {
                return 0;
            }

            var created = new PartyState(++_nextPartyId, name, NewPassword(), info.CharacterHandle) { Type = HuntaholicPartyType };
            var member = new PartyMember(info.CharacterHandle, info.CharacterName);
            Remember(member, info);
            created.Members.Add(member);
            _parties[created.Id] = created;
            _partyOf[info.CharacterHandle] = created.Id;
            info.PartyId = created.Id;
            SendPartyInfo(leader, created);
            return created.Id;
        }
    }

    /// <summary><c>joinInstanceDungeon</c>: <c>JoinParty</c>, then <c>SendPartyInfo</c> and <c>BroadcastPartyMemberInfo</c>.</summary>
    public bool JoinHuntaholicParty(int partyId, GameClient member)
    {
        var info = member.ConnectionInfo;
        lock (_gate)
        {
            if (info.CharacterHandle == 0 || _partyOf.ContainsKey(info.CharacterHandle)
                || !_parties.TryGetValue(partyId, out var party) || !party.Huntaholic || party.Members.Count >= MaxMembers)
            {
                return false;
            }

            var entry = new PartyMember(info.CharacterHandle, info.CharacterName);
            Remember(entry, info);
            party.Members.Add(entry);
            _partyOf[info.CharacterHandle] = party.Id;
            info.PartyId = party.Id;
            SendPartyInfo(member, party);
            BroadcastMemberInfo(party, member);
            return true;
        }
    }

    /// <summary>
    /// <c>leaveInstanceDungeon</c>'s party half: a leader alone (or with nobody online to promote) destroys the party,
    /// otherwise the first other online member is promoted (<c>AutoPromote</c>, <c>PROMOTE</c>) and the leaver leaves
    /// (<c>BroadcastPartyLeave</c>). Nothing happens outside a HuntaHolic party.
    /// </summary>
    public void LeaveHuntaholicParty(GameClient member)
    {
        var info = member.ConnectionInfo;
        lock (_gate)
        {
            if (!TryGetParty(info.CharacterHandle, out var party) || !party.Huntaholic)
            {
                return;
            }

            if (party.LeaderId == info.CharacterHandle)
            {
                var heir = party.Members.FirstOrDefault(m => m.CharacterId != info.CharacterHandle && Online(m.CharacterId) is not null);
                if (heir is null)
                {
                    DestroyLocked(party);
                    return;
                }

                party.LeaderId = heir.CharacterId;
                SendToParty(party, PartyMessages.Promote(heir.Name));
            }

            SendToParty(party, PartyMessages.Leave(info.CharacterName));
            RemoveMember(party, info.CharacterHandle);
        }
    }

    public void DestroyHuntaholicParty(int partyId)
    {
        lock (_gate)
        {
            if (_parties.TryGetValue(partyId, out var party) && party.Huntaholic)
            {
                DestroyLocked(party);
            }
        }
    }

    public IReadOnlyList<GameClient> OnlineMembers(int partyId)
    {
        lock (_gate)
        {
            return _parties.TryGetValue(partyId, out var party)
                ? party.Members.Select(m => Online(m.CharacterId)).Where(c => c is not null).ToArray()
                : Array.Empty<GameClient>();
        }
    }

    public int PartyMemberCount(int partyId)
    {
        lock (_gate) return _parties.TryGetValue(partyId, out var party) ? party.Members.Count : 0;
    }

    public bool IsPartyLeader(int partyId, GameClient client)
    {
        lock (_gate)
            return _parties.TryGetValue(partyId, out var party) && party.LeaderId == client.ConnectionInfo.CharacterHandle;
    }

    public string PartyName(int partyId)
    {
        lock (_gate) return _parties.TryGetValue(partyId, out var party) ? party.Name : string.Empty;
    }

    public int PartyIdOf(GameClient client)
    {
        lock (_gate) return TryGetParty(client.ConnectionInfo.CharacterHandle, out var party) ? party.Id : 0;
    }

    /// <summary>
    /// <c>onInstanceGameEnter</c>: alone, the party is destroyed; a leader promotes the next member first; then the
    /// player leaves (<c>BroadcastPartyLeave</c>).
    /// </summary>
    public void LeaveForInstanceGame(GameClient client)
    {
        var info = client.ConnectionInfo;
        lock (_gate)
        {
            if (!TryGetParty(info.CharacterHandle, out var party))
            {
                return;
            }

            if (party.Members.Count == 1)
            {
                DestroyLocked(party);
                return;
            }

            if (party.LeaderId == info.CharacterHandle)
            {
                var heir = party.Members.FirstOrDefault(m => m.CharacterId != info.CharacterHandle && Online(m.CharacterId) is not null)
                           ?? party.Members.First(m => m.CharacterId != info.CharacterHandle);
                party.LeaderId = heir.CharacterId;
                SendToParty(party, PartyMessages.Promote(heir.Name));
            }

            SendToParty(party, PartyMessages.Leave(info.CharacterName));
            RemoveMember(party, info.CharacterHandle);
        }
    }

    private void DestroyLocked(PartyState party)
    {
        SendToParty(party, PartyMessages.Destroy(party.Name));
        foreach (var member in party.Members.ToArray())
        {
            RemoveMember(party, member.CharacterId);
        }
    }
}
