using System;
using System.Collections.Generic;
using Navislamia.Configuration.Options;
using Navislamia.Game.DataAccess.Entities.Enums;
using Navislamia.Game.Services.ReturnPoints;

namespace Navislamia.Game.Services.Props;

/// <summary>
/// The town teleporters of NPC_TeleportTown.lua (Epic 7 Part 4, branches/Live): the arrival spread of their three
/// teleports and the island teleporter's menu, which depends on the character. The other teleporters' pages are in
/// the dialog catalogue (tools/export_town_teleporters.py). See docs/packet-specs/socle-point-de-retour.md.
/// </summary>
public static class TownTeleportRules
{
    /// <summary>The island teleporter (NPC 3005), whose menu is built rather than read from the catalogue.</summary>
    public const string BeginnerContact = "NPC_TeleportField_Beginner_contact";

    /// <summary>The quest that sends the player to the senior instructor: once completed, the island teleporter offers the east coast.</summary>
    public const int EastCoastQuest = 1025;

    /// <summary><c>CHAT_NPC</c>, what the Lua's <c>message()</c> sends, from <c>@SCRIPT</c>.</summary>
    public const byte ChatNpc = 40;

    /// <summary>The line every teleport sends when the gold falls short.</summary>
    public const string NotEnoughGold = "@90010008";

    /// <summary>
    /// The scatter of the arrival, <c>warp(x + random(0, n), y + random(0, n))</c>: 10 for <c>RunTeleport</c>
    /// (whose comment says 100), 100 for the two others.
    /// </summary>
    public static int ArrivalSpread(PropActionKind kind) => kind == PropActionKind.RunTeleport ? 10 : 100;

    /// <summary><c>RunTeleport_City_To_Camp</c>: the return point becomes the camp, <c>x + random(0, 10)</c>.</summary>
    public static ReturnPoint CampReturnPoint(PropAction action, Random random) =>
        new(action.X + random.Next(0, 11), action.Y + random.Next(0, 11));

    /// <summary>
    /// <c>NPC_TeleportField_Beginner_contact</c>: the return point, the east coast once quest 1025 is completed
    /// (<c>get_quest_progress == 255</c>), the race's town after a job change (<c>job_depth &gt; 0</c>, 10 gold).
    /// "Another trainees' camp" (<c>Teleport_channel</c>) is left out: the island's channels are not modelled.
    /// </summary>
    public static NpcDialogDefinition BeginnerTeleporter(int race, int jobDepth, int eastCoastQuestProgress)
    {
        var menu = new List<NpcDialogMenuEntry>
        {
            new() { Label = "@90300507", Trigger = "Binding_Beginner_001()" }
        };

        if (eastCoastQuestProgress == 255)
            menu.Add(new NpcDialogMenuEntry { Label = "@90300503", Trigger = "RunTeleport( 0 , 175711 ,56887 )" });

        if (jobDepth > 0)
        {
            menu.Add((Race)race switch
            {
                Race.Deva => new NpcDialogMenuEntry { Label = "@90300505", Trigger = "RunTeleport_Begin_TO_City( 10 , 6625 , 6980 )" },
                Race.Asura => new NpcDialogMenuEntry { Label = "@90300506", Trigger = "RunTeleport_Begin_TO_City( 10 , 116799 , 58205 )" },
                _ => new NpcDialogMenuEntry { Label = "@90300512", Trigger = "RunTeleport_Begin_TO_City( 10 , 153506 , 77175 )" }
            });
        }

        menu.Add(new NpcDialogMenuEntry { Label = "@90010001", Trigger = string.Empty });
        return new NpcDialogDefinition { Title = "@90300501", Text = "@90300502", Menu = menu };
    }
}
