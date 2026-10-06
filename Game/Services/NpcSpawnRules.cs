using System;
using System.Collections.Frozen;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Navislamia.Game.DataAccess.Entities.Arcadia;

namespace Navislamia.Game.Services;

/// <summary>
/// Which NPCs a server shows, ported from the official <c>onNPCData</c> (2012-11 server, <c>0x1401c70a0</c>, the
/// 2015 source's <c>NPCLoader.cpp:143-180</c>) and <c>NPCRespawnObject</c>/<c>StructNPC</c> (<c>NPCRespawn.cpp:71-86</c>,
/// <c>NPCAI.cpp:163</c>). docs/packet-specs/socle-pnj-pays-periodes.md.
/// </summary>
public static class NpcSpawnRules
{
    /// <summary><c>GameContent::LOCAL_EXCLUDE_TEST_SERV</c>: not on a test server.</summary>
    public const int ExcludeTestServer = 1 << 29;

    /// <summary><c>GameContent::LOCAL_EXCLUDE_SERVICE_SERV</c>: not on a live (service) server.</summary>
    public const int ExcludeServiceServer = 1 << 30;

    /// <summary>
    /// Whether the NPC is loaded at all: its <c>local_flag</c> is an <b>exclusion</b> mask — a bit of the server's
    /// country set means "not in this country", 0 means everywhere —, bits 29/30 keep it off a test or a live server,
    /// and a periodic NPC whose period is over is not loaded.
    /// </summary>
    public static bool IsLoaded(NpcResourceEntity npc, int localFlag, bool serviceServer, DateTime nowUtc)
    {
        if ((npc.LocalFlag & ExcludeTestServer) != 0 && !serviceServer) return false;
        if ((npc.LocalFlag & ExcludeServiceServer) != 0 && serviceServer) return false;
        if ((npc.LocalFlag & localFlag) != 0) return false;
        return !npc.IsPeriodic || nowUtc < npc.EndOfPeriod.ToUniversalTime();
    }

    /// <summary>
    /// The 7.3 client's own <c>local_flag</c> per NPC (<c>db_npcresource.rdb</c>, tools/export_npc_client_flags.py): a
    /// later state than the Epic 7 server table, with the events that were over and every 9.4-only event row switched
    /// off. An NPC is excluded when either source excludes it, so the server never shows what the client has retired.
    /// </summary>
    public static IReadOnlyDictionary<long, int> ClientFlags => LazyClientFlags.Value;

    private static readonly Lazy<FrozenDictionary<long, int>> LazyClientFlags = new(() =>
    {
        using var stream = typeof(NpcSpawnRules).Assembly.GetManifestResourceStream("Navislamia.NpcClientFlags.json")
            ?? throw new InvalidOperationException("The embedded NPC client flags are missing");
        return JsonSerializer.Deserialize<Dictionary<string, int>>(stream)!
            .ToFrozenDictionary(entry => long.Parse(entry.Key), entry => entry.Value);
    });

    /// <summary>The server's flag combined with the client's: every exclusion either of them carries.</summary>
    public static int EffectiveFlag(NpcResourceEntity npc, IReadOnlyDictionary<long, int> clientFlags) =>
        npc.LocalFlag | (clientFlags is not null && clientFlags.TryGetValue(npc.Id, out var flag) ? flag : 0);

    /// <summary>A loaded NPC stands in the world from its period's beginning to its end; a regular one always.</summary>
    public static bool IsPresent(NpcResourceEntity npc, DateTime nowUtc) =>
        !npc.IsPeriodic || (npc.BeginOfPeriod.ToUniversalTime() <= nowUtc && nowUtc < npc.EndOfPeriod.ToUniversalTime());
}
