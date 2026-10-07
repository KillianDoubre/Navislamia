using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Navislamia.Game.Network.Clients;
using Navislamia.Game.Network.Packets.Game;
using Serilog;

namespace Navislamia.Game.Services.Creatures;

/// <summary>
/// The creature farm's server side: <c>TM_CS_REQUEST_FARM_INFO</c> (6000) answered with a
/// <c>TM_SC_FARM_INFO</c> (6001) filled from the farm's storage, and <c>TM_CS_NURSE_CREATURE</c> (6006)
/// answered with a <c>TM_SC_RESULT_NURSE</c> (6007).
/// </summary>
public interface ICreatureFarmService
{
    /// <summary>
    /// Sends the farm of the calling character. Returns false — and sends nothing — when the session
    /// carries no character name, which is the state the client is in before the world entry completes.
    /// </summary>
    Task<bool> SendFarmInfoAsync(GameClient client);

    /// <summary>
    /// The nursing gesture (<c>NurseSummon</c>, <c>StructPlayer.cpp:11423-11467</c>, 7.3
    /// <c>0x1400d63a0</c>): the handle must resolve one of the character's own cards, that card must carry
    /// <c>ITEM_FLAG_FARMED_SUMMON</c> (bit 27) and be named by one of its farm rows, and that entry must not
    /// have been nursed since the last 06:00. The nursing time is then written and the farm's script decides
    /// between <c>REWARDED</c> and <c>NO_REWARD</c>; a refused nursing is <c>FAILED</c>.
    /// <para>
    /// Sends the <c>6007</c> carrying the verdict and returns it — the client expects that answer for every
    /// 6006 it built itself. Sends nothing and returns <c>Failed</c> when the session carries no character.
    /// </para>
    /// </summary>
    Task<NurseResult> NurseAsync(GameClient client, uint creatureCardHandle);
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
    private readonly Navislamia.Game.Scripting.IScriptService _scripts;

    /// <param name="localNow">
    /// The local server clock, as for the nursing reset at 06:00 (<c>StructPlayer.cpp:11439-11456</c>).
    /// </param>
    /// <param name="scripts">
    /// The interpreter the nursing gesture asks for its verdict, as the official server does
    /// (<c>return NPC_Creature_Farm_nurse_handler()</c>). Null — or a function that is not loaded — reads as
    /// "no script", which the verdict maps to <c>NO_REWARD</c> (A VERIFIER 1).
    /// </param>
    public CreatureFarmService(ICreatureFarmStore store, Func<DateTime> localNow = null,
        Navislamia.Game.Scripting.IScriptService scripts = null)
    {
        _store = store;
        _localNow = localNow ?? (() => DateTime.Now);
        _scripts = scripts;
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

    /// <summary>
    /// The nursing path of §5.1: decide with <see cref="CreatureFarmRules.CanNurse"/>, write the nursing
    /// time (<c>DB_UpdateNursingTime</c>, and nothing else — no flag, no ticket), then let the farm's script
    /// name the verdict. Every step below corresponds to one line of the reference's <c>NurseSummon</c> plus
    /// its caller; nothing here decides the gift, which the script inserts itself.
    /// </summary>
    public async Task<NurseResult> NurseAsync(GameClient client, uint creatureCardHandle)
    {
        var characterName = client?.ConnectionInfo?.CharacterName;
        if (string.IsNullOrEmpty(characterName))
        {
            _logger.Warning("Refused a nursing for {clientTag}: the session carries no character",
                client?.ClientTag);
            return NurseResult.Failed;
        }

        var now = _localNow();

        FarmNursingTarget target;
        try
        {
            target = await _store.LoadNursingTargetAsync(characterName, creatureCardHandle);
        }
        catch (Exception exception)
        {
            // The verdict is still owed to the client: a failed read answers FAILED (silence), never a torn
            // session.
            _logger.Error(exception, "Could not read the nursing target of {characterName}", characterName);
            return Answer(client, NurseResult.Failed);
        }

        if (!CreatureFarmRules.CanNurse(target, now))
        {
            _logger.Debug("Refused the nursing of card {cardHandle} for {characterName}: {reason}",
                creatureCardHandle, characterName, RefusalReason(target, now));
            return Answer(client, NurseResult.Failed);
        }

        bool stored;
        try
        {
            stored = await _store.SetNursingTimeAsync(characterName, target.CardItemId, now);
        }
        catch (Exception exception)
        {
            _logger.Error(exception, "Could not write the nursing time of {characterName}", characterName);
            return Answer(client, NurseResult.Failed);
        }

        if (!stored)
        {
            // The farm entry disappeared between the read and the write: the reference's own refusal.
            _logger.Warning("Refused the nursing of {characterName}: the card {cardId} names no farm entry",
                characterName, target.CardItemId);
            return Answer(client, NurseResult.Failed);
        }

        // The write precedes the script, as in the reference: NurseSummon stores the time and returns true,
        // only then does the caller run the chunk and read its verdict (StructPlayer.cpp:11458-11462,
        // GameMessage.cpp:11931-11938).
        var verdict =
            CreatureFarmRules.NurseVerdict(_scripts?.CallGlobalFunction(CreatureFarmRules.NurseHandlerFunction));
        _logger.Debug("TM_SC_RESULT_NURSE ({id}) for {characterName}: card {cardId} nursed at {nursingTime}, " +
            "farm script verdict {verdict}",
            (ushort)Network.Packets.Enums.GamePackets.TM_SC_RESULT_NURSE, characterName, target.CardItemId, now,
            (byte)verdict);
        return Answer(client, verdict);
    }

    /// <summary>The <c>6007</c> the client waits for: always sent, and returned for the caller's log.</summary>
    private static NurseResult Answer(GameClient client, NurseResult result)
    {
        client?.Connection?.Send(GameFarmPackets.BuildResultNurse(result));
        return result;
    }

    /// <summary>The reference's order of the four conditions, so a log says which one refused.</summary>
    private static string RefusalReason(FarmNursingTarget target, DateTime now) => target is null
        ? "the handle resolves no card of the character"
        : !CreatureFarmRules.IsFarmed(target.Flag) ? "the card is not deposited (bit 27 clear)"
        : !target.IsInFarm ? "no farm entry names the card"
        : "already nursed since the last 06:00";
}
