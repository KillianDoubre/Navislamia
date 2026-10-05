using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Navislamia.Game.Network.Clients;
using Navislamia.Game.Network.Packets;
using Navislamia.Game.Services.Dungeons;
using Navislamia.Game.Services.Interfaces;
using Navislamia.Game.Services.Party;

namespace Navislamia.Game.Services;

public interface IRecallFeatherService
{
    ResultCode Check(GameClient caster, uint target, int count);
    void Offer(GameClient caster, uint target, int count);
}

/// <summary>ITEM_EFFECT_INSTANT::RECALL (112), StructCreature.cpp:4854-5021.</summary>
public sealed class RecallFeatherService : IRecallFeatherService
{
    private readonly IPartyService _parties;
    private readonly IPlayerVisibilityService _players;
    private readonly DungeonRooms _rooms;
    private readonly DungeonCatalog _catalog;
    private readonly Huntaholic.IHuntaholicCatalog _huntaholic;

    public RecallFeatherService(IPartyService parties, IPlayerVisibilityService players,
        DungeonRooms rooms = null, DungeonCatalog catalog = null, Huntaholic.IHuntaholicCatalog huntaholic = null)
    { _parties = parties; _players = players; _rooms = rooms; _catalog = catalog; _huntaholic = huntaholic; }

    private IReadOnlyList<GameClient> Targets(GameClient caster, uint target, int count)
    {
        if (count != 1) return _parties.DungeonParty(caster)?.Online.Where(c => c != caster).ToArray() ?? Array.Empty<GameClient>();
        return _players.Registry.TryResolve(target, out var player) ? new[] { player } : Array.Empty<GameClient>();
    }

    public ResultCode Check(GameClient caster, uint target, int count)
    {
        var info = caster.ConnectionInfo;
        if (_rooms?.RoomOf(caster) is { } room)
            return room.Key.Kind == DungeonRoomKind.Instance ? ResultCode.NotActableInInstanceDungeon : ResultCode.NotActableHere;
        var party = _parties.DungeonParty(caster);
        var targets = Targets(caster, target, count);
        if (party is null) return ResultCode.AccessDenied;
        if (targets.Count == 0) return count == 1 ? ResultCode.AccessDenied : ResultCode.NotExist;
        var dungeon = _catalog?.Dungeons.Values.FirstOrDefault(d => d.CellX == (int)(info.X / 16128)
            && d.CellY == (int)(info.Y / 16128));
        foreach (var player in targets)
        {
            var recipient = player.ConnectionInfo;
            if (player == caster || recipient.CharacterHandle == 0 || !party.Members.Contains(recipient.CharacterHandle)
                || recipient.StorageSecurityCheck || count == 1 && recipient.CharacterHp <= 0)
                return count == 1 ? ResultCode.AccessDenied : ResultCode.NotActable;
            if (count != 1 && _huntaholic?.GetHuntaholicId(recipient.X, recipient.Y) > 0) return ResultCode.TargetInHuntaholic;
            if (dungeon is not null && recipient.CharacterLevel < Math.Max(1, dungeon.Level - 40)) return ResultCode.AccessDenied;
        }
        return ResultCode.Success;
    }

    public void Offer(GameClient caster, uint target, int count)
    {
        // Freeze the position in the expression; moving the caster while the popup is open does not change it.
        var info = caster.ConnectionInfo;
        var trigger = string.Create(CultureInfo.InvariantCulture, $"recall_feather( {info.X:F6}, {info.Y:F6}, {info.Layer} )");
        foreach (var player in Targets(caster, target, count))
            if (player != caster && player.ConnectionInfo.CharacterHp > 0 && !player.ConnectionInfo.StorageSecurityCheck)
                ScriptWindows.Show(player, "recall_feather_confirm_window", info.CharacterName, trigger);
    }
}
