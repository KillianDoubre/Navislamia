using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Navislamia.Game.Network.Clients;
using Navislamia.Game.Network.Packets.Game;
using Serilog;

namespace Navislamia.Game.Services.Creatures;

/// <summary>
/// The creature farm's read path: <c>TM_CS_REQUEST_FARM_INFO</c> (6000) answered with a
/// <c>TM_SC_FARM_INFO</c> (6001) filled from the farm's storage.
/// </summary>
public interface ICreatureFarmService
{
    /// <summary>
    /// Sends the farm of the calling character. Returns false — and sends nothing — when the session
    /// carries no character name, which is the state the client is in before the world entry completes.
    /// </summary>
    Task<bool> SendFarmInfoAsync(GameClient client);
}

/// <summary>
/// The farm window's content (docs/packet-specs/socle-ferme-creatures-officielle.md §5.6 point 3):
/// the farm table read on every <c>6000</c>, then one <c>SUMMON_INFO</c> per entry — never a projection
/// of what the farm could produce, the experience shown is the summon's own (§3.2).
/// <para>
/// The <c>6000</c> is sent on the window's opening and on each of its refreshes, so the read is
/// deliberately stateless: no memory of a farm, one query per request.
/// </para>
/// <para>
/// Open: the reference retrieves the entries whose ticket has expired <em>before</em> answering
/// (<c>onRequestFarmInfo</c> → <c>RegainSummon</c>, §5.1). That gesture grants farm experience, whose
/// curve's source of truth is not settled yet (A VERIFIER 6), so an expired entry is served as it is
/// stored rather than silently emptied of its experience. The deposit and retrieval lots own that.
/// </para>
/// </summary>
public sealed class CreatureFarmService : ICreatureFarmService
{
    private readonly ILogger _logger = Log.ForContext<CreatureFarmService>();
    private readonly ICreatureFarmStore _store;
    private readonly Func<DateTime> _localNow;

    /// <param name="localNow">
    /// The local server clock, as for the nursing reset at 06:00 (<c>StructPlayer.cpp:11439-11456</c>).
    /// </param>
    public CreatureFarmService(ICreatureFarmStore store, Func<DateTime> localNow = null)
    {
        _store = store;
        _localNow = localNow ?? (() => DateTime.Now);
    }

    public async Task<bool> SendFarmInfoAsync(GameClient client)
    {
        var characterName = client?.ConnectionInfo?.CharacterName;
        if (string.IsNullOrEmpty(characterName))
        {
            _logger.Warning("Refused a farm read for {clientTag}: the session carries no character",
                client?.ClientTag);
            return false;
        }

        IReadOnlyList<FarmedSummon> farm;
        try
        {
            farm = await _store.LoadAsync(characterName);
        }
        catch (Exception exception)
        {
            // The client keeps its window open and re-asks (6000): failing here must not tear the session
            // down, it leaves the farm as it was and logs.
            _logger.Error(exception, "Could not read the creature farm of {characterName}", characterName);
            return false;
        }

        var now = _localNow();
        var entries = farm
            .Select(f => CreatureFarmRules.SummonInfo(f.Slot, f.Experience, f.Name, f.Duration,
                f.RegistrationTime, f.NursingTime, f.IsCash, f.IsUsingCracker, f.CardInfo, now))
            .ToArray();

        client.Connection.Send(GameFarmPackets.BuildFarmInfo(entries));
        _logger.Debug("TM_SC_FARM_INFO ({id}) sent to {clientTag} for {characterName} with {count} entries",
            (ushort)Network.Packets.Enums.GamePackets.TM_SC_FARM_INFO, client.ClientTag, characterName,
            entries.Length);
        return true;
    }
}
