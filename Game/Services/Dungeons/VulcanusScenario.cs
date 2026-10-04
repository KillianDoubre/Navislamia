using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Navislamia.Configuration.Options;
using Navislamia.Game.Network.Clients;
using Navislamia.Game.Network.Packets;
using Navislamia.Game.Network.Packets.Game;
using Navislamia.Game.Services.Interfaces;
using Navislamia.Game.Services.Props;

namespace Navislamia.Game.Services.Dungeons;

/// <summary>
/// The Vulcanus instance scenario of the Epic 7 <c>ETC_dungeon_prop.lua</c> (<c>on_create_vulcanus_instance</c>,
/// <c>vulcanus_check_respawn_group_clear</c>, <c>vulcanus_clear_reward</c>, <c>enter_other_indun</c>,
/// <c>warp_indun</c>, <c>warp_floor</c>), as pure rules. docs/packet-specs/socle-donjons-instances-secrets.md §Vulcanus.
/// </summary>
public static class VulcanusRules
{
    public const int DungeonId = 20000;
    public const int BossGroup = 20013;
    public const string DifficultyFlag = "difficulty";

    /// <summary>The floor of a respawn group: 20001-20004, 20005-20008, 20009-20012, then the boss room 20013.</summary>
    public static int Floor(int group) => group switch
    {
        >= 20001 and <= 20004 => 1,
        >= 20005 and <= 20008 => 2,
        >= 20009 and <= 20012 => 3,
        BossGroup => 4,
        _ => 0
    };

    /// <summary>The room's bit in its floor's flag (<c>Vul1</c>..<c>Vul3</c>): 1, 2, 4, 8; the boss room has none.</summary>
    public static int RoomBit(int group) => Floor(group) is >= 1 and <= 3 ? 1 << ((group - 20001) % 4) : 0;

    public static string FloorFlag(int floor) => "Vul" + floor;

    /// <summary>A floor flag of 15 is a floor whose four rooms are cleared.</summary>
    public const int FloorCleared = 15;

    /// <summary>The gate the cleared room leaves: to the next room or floor, the exit after the boss.</summary>
    public static int Gate(int floor) => floor switch { 1 => 126024, 2 => 126025, 3 => 126026, 4 => 126027, _ => 0 };

    /// <summary>The boss room's exit gate stands higher (<c>prop_z_offset = 35</c>, 10 elsewhere).</summary>
    public static float GateZOffset(int floor) => floor == 4 ? 35 : 10;

    /// <summary><c>warp_floor</c>'s key: 20 of 1000401 on floor 1, 10 of 1000402, 5 of 1000403, one 1000404 for the boss.</summary>
    public static (int ItemId, int Count) Keys(int floor) => floor switch
    {
        1 => (1000401, 20),
        2 => (1000402, 10),
        3 => (1000403, 5),
        4 => (1000404, 1),
        _ => (0, 0)
    };

    private static readonly (int X, int Y)[][] Rooms =
    {
        new[] { (197393, 28580), (198814, 28457), (200543, 28458), (202065, 28458) },
        new[] { (197047, 25954), (198661, 25929), (200362, 25962), (202169, 26121) },
        new[] { (197303, 23750), (198586, 23765), (200390, 23677), (202114, 23701) },
        new[] { (206474, 27625) }
    };

    /// <summary><c>warp_floor(floor, gate_num)</c>'s destination.</summary>
    public static (int X, int Y) RoomPosition(int floor, int room) => Rooms[floor - 1][room - 1];

    /// <summary>
    /// <c>warp_indun</c>'s choice: the boss floor has one room; elsewhere a room drawn among those whose bit is clear in
    /// the floor flag (the Lua's lists of flag values are exactly "this bit is clear"). None when the floor is done.
    /// </summary>
    public static int ChooseRoom(int floor, int floorFlag, Random random)
    {
        if (floor == 4) return 1;
        if (floor is < 1 or > 3 || floorFlag == FloorCleared) return 0;
        var open = Enumerable.Range(1, 4).Where(room => (floorFlag & (1 << (room - 1))) == 0).ToArray();
        return open.Length == 0 ? 0 : open[random.Next(open.Length)];
    }

    /// <summary>
    /// <c>enter_other_indun</c> → <c>SCRIPT_EnterOtherInstanceDungeon</c>: two entries, labelled with the key counts, for
    /// another room of the current floor (-1 once the floor is cleared) and for the next floor.
    /// </summary>
    public static NpcDialogMenuEntry[] FloorWindow(int dungeonId, int currentFloor, int nextFloor, int currentFloorFlag,
        int currentCount, int nextCount)
    {
        var current = currentFloorFlag == FloorCleared ? -1 : currentFloor;
        return new[]
        {
            new NpcDialogMenuEntry { Label = currentCount.ToString(), Trigger = $"warp_indun({dungeonId},{current})" },
            new NpcDialogMenuEntry { Label = nextCount.ToString(), Trigger = $"warp_indun({dungeonId},{nextFloor})" }
        };
    }

    /// <summary><c>TS_SC_DIALOG::TYPE_OTHER_INSTANCE_DUNGEON_CONFIRM_WINDOW</c>.</summary>
    public const int FloorWindowType = 10;
}

/// <summary>Carries out <see cref="VulcanusRules"/>: room clears, rewards, gates and the trips between rooms.</summary>
public sealed class VulcanusScenario
{
    /// <summary><c>CHAT_NPC</c>, the channel the ported <c>message()</c> lines use; the Lua's <c>cprint</c> lines go there too.</summary>
    private const byte ChatNpc = 40;

    private readonly DungeonCatalog _catalog;
    private readonly DungeonRooms _rooms;
    private readonly MonsterWorldState _monsters;
    private readonly IDynamicFieldProps _props;
    private readonly IFieldPropService _fieldProps;
    private readonly ILevelingService _leveling;
    private readonly Random _random;

    public VulcanusScenario(DungeonCatalog catalog, DungeonRooms rooms, MonsterWorldState monsters,
        IDynamicFieldProps props, IFieldPropService fieldProps, ILevelingService leveling, Random random = null)
    {
        _catalog = catalog;
        _rooms = rooms;
        _monsters = monsters;
        _props = props;
        _fieldProps = fieldProps;
        _leveling = leveling;
        _random = random ?? Random.Shared;
    }

    /// <summary>
    /// A new instance room: its props are posed on its layer, and Vulcanus keeps its floor flags and the difficulty
    /// it was entered at (<c>on_create_vulcanus_instance</c>).
    /// </summary>
    public void OnCreate(DungeonRoom room)
    {
        foreach (var prop in _catalog.InstanceProps.Where(p => p.DungeonId == room.Key.DungeonId && p.Type == room.Type))
        {
            Pose(room, prop.PropId, prop.X, prop.Y, prop.ZOffset, prop.RotateX, prop.RotateY, prop.RotateZ,
                prop.ScaleX, prop.ScaleY, prop.ScaleZ);
        }

        if (room.Key.DungeonId != VulcanusRules.DungeonId) return;
        lock (room.Flags)
        {
            for (var floor = 1; floor <= 3; floor++) room.Flags[VulcanusRules.FloorFlag(floor)] = 0;
            room.Flags[VulcanusRules.DifficultyFlag] = room.Type;
        }
    }

    /// <summary>
    /// <c>vulcanus_check_respawn_group_clear</c>: when the last monster of a room dies, the room is cleared — its bit in
    /// the floor flag, the floor's reward to every player of the instance, and the gate at the monster's spot.
    /// </summary>
    public void OnMonsterKilled(long instanceId)
    {
        if (_monsters is null) return;
        var room = _rooms.FindByMonster(instanceId, out var group);
        if (room is null || room.Key.DungeonId != VulcanusRules.DungeonId) return;
        var floor = VulcanusRules.Floor(group);
        if (floor == 0) return;
        if (room.GroupOf.Any(entry => entry.Value == group && entry.Key != instanceId && _monsters.IsAlive(entry.Key))) return;

        int difficulty;
        lock (room.Flags)
        {
            // A room clears once, whatever kills arrive after the last one.
            var cleared = "cleared" + group;
            if (room.Flags.ContainsKey(cleared)) return;
            room.Flags[cleared] = 1;
            if (floor <= 3)
            {
                var flag = VulcanusRules.FloorFlag(floor);
                room.Flags[flag] = room.Flags.GetValueOrDefault(flag) | VulcanusRules.RoomBit(group);
            }

            difficulty = room.Flags.GetValueOrDefault(VulcanusRules.DifficultyFlag);
        }

        var reward = _catalog.VulcanusRewards.FirstOrDefault(r => r.Difficulty == difficulty && r.Floor == floor);
        var members = _rooms.MembersOf(room);
        foreach (var member in members)
        {
            if (group != VulcanusRules.BossGroup)
            {
                Say(member, "@9813");
                Say(member, "@9250");
            }

            if (reward is not null) Credit(member, reward);
        }

        var (x, y) = _monsters.GetPosition(instanceId);
        Pose(room, VulcanusRules.Gate(floor), x, y, VulcanusRules.GateZOffset(floor));
        foreach (var member in members) _fieldProps?.Sync(member);
    }

    /// <summary><c>enter_other_indun</c>: the warning line, then the floor window on the gate's handle.</summary>
    public void ShowFloorWindow(GameClient client, uint gateHandle, PropAction action)
    {
        var room = _rooms.RoomOf(client);
        if (room is null || room.Key.DungeonId != action.DungeonId || action.DungeonId != VulcanusRules.DungeonId) return;
        int flag;
        lock (room.Flags) flag = room.Flags.GetValueOrDefault(VulcanusRules.FloorFlag(action.X));
        var menu = VulcanusRules.FloorWindow(action.DungeonId, action.X, action.Y, flag, action.Type, (int)action.Cost);

        Say(client, "@9812");
        var info = client.ConnectionInfo;
        lock (info.NpcVisibilityLock)
        {
            info.ClearNpcDialog();
            info.NpcDialogHandle = gateHandle;
            info.NpcDialogRevision++;
            foreach (var entry in menu) info.NpcDialogTriggers.Add(entry.Trigger);
            client.Connection.Send(GameNpcDialogPackets.BuildDialog(gateHandle, "InDun", "Warp", menu,
                VulcanusRules.FloorWindowType));
        }
    }

    /// <summary>
    /// <c>warp_indun</c> → <c>warp_floor</c>: a room of the floor, the keys taken (<c>@9810</c> without them), the trip on
    /// the instance's layer. <paramref name="consumeKeys"/> takes the keys and reports whether it could.
    /// </summary>
    public async Task<ResultCode> WarpFloorAsync(GameClient client, int floor, Func<int, int, Task<bool>> consumeKeys,
        IWarpService warp)
    {
        var room = _rooms.RoomOf(client);
        if (room is null || room.Key.DungeonId != VulcanusRules.DungeonId || floor is < 1 or > 4) return ResultCode.NotActable;
        int flag;
        lock (room.Flags) flag = room.Flags.GetValueOrDefault(VulcanusRules.FloorFlag(floor));
        var target = VulcanusRules.ChooseRoom(floor, flag, _random);
        if (target == 0) return ResultCode.NotActable;

        var (itemId, count) = VulcanusRules.Keys(floor);
        if (!await consumeKeys(itemId, count))
        {
            Say(client, "@9810");
            return ResultCode.AccessDenied;
        }

        var (x, y) = VulcanusRules.RoomPosition(floor, target);
        warp.Warp(client, x, y, room.Layer);
        if (floor == 4)
        {
            Say(client, "@90604914");
            Say(client, "@90604915");
        }

        return ResultCode.Success;
    }

    private void Pose(DungeonRoom room, int propId, float x, float y, float zOffset, float rotateX = 0, float rotateY = 0,
        float rotateZ = 0, float scaleX = 1, float scaleY = 1, float scaleZ = 1)
    {
        if (_props is null || !_catalog.PropTemplates.TryGetValue(propId, out var definition)) return;
        var template = new FieldPropTemplate(definition.Id, definition.ActivateSkillId, 0, definition.MinLevel,
            definition.MaxLevel, 0, 0, PropScript.Parse(definition.Script), Array.Empty<PropActivation>());
        var prop = _props.Add(propId, x, y, room.Layer, template, null, zOffset, rotateX, rotateY, rotateZ, scaleX, scaleY,
            scaleZ);
        _rooms.TrackProp(room, prop.InstanceId);
    }

    /// <summary><c>add_exp_jp(exp, jp)</c> and <c>insert_gold(gold)</c>.</summary>
    private void Credit(GameClient client, VulcanusReward reward)
    {
        var info = client.ConnectionInfo;
        lock (info.ProgressLock)
        {
            info.CharacterExp += reward.Exp;
            info.CharacterJp += reward.Jp;
        }

        info.AddGold(reward.Gold);
        _leveling?.ApplyExperience(client);
        client.Connection.Send(GameCharacterPackets.BuildExpUpdate(info.CharacterHandle, info.CharacterExp, info.CharacterJp));
        client.Connection.Send(GameCharacterPackets.BuildGoldUpdate(info.CharacterGold, info.CharacterChaos));
    }

    private static void Say(GameClient client, string text) =>
        client.Connection.Send(GameChatPackets.BuildChat("@SCRIPT", ChatNpc, text));
}

/// <summary>
/// What combat tells the dungeons without depending on them (the <c>HuntaholicEvents</c> pattern): the dungeon service
/// reaches the warp, which reaches combat.
/// </summary>
public sealed class DungeonEvents
{
    private volatile Action<long> _monsterKilled;

    public void Attach(Action<long> monsterKilled) => _monsterKilled = monsterKilled;

    public void MonsterKilled(long instanceId) => _monsterKilled?.Invoke(instanceId);
}
