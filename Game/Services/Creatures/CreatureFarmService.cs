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
/// <c>TM_SC_FARM_INFO</c> (6001) filled from the farm's storage, <c>TM_CS_RETRIEVE_CREATURE</c> (6004) with the
/// <c>TM_SC_RESULT_RETRIEVE</c> (6005) of the gesture, and <c>TM_CS_NURSE_CREATURE</c> (6006) with a
/// <c>TM_SC_RESULT_NURSE</c> (6007).
/// </summary>
public interface ICreatureFarmService
{
    /// <summary>
    /// Sends the farm of the calling character. Returns false — and sends nothing — when the session
    /// carries no character name, which is the state the client is in before the world entry completes.
    /// <para>
    /// An entry whose ticket has expired is retrieved <b>before</b> the answer and left out of the list
    /// (<c>onRequestFarmInfo</c> → <c>RegainSummon</c>, §5.8).
    /// </para>
    /// </summary>
    Task<bool> SendFarmInfoAsync(GameClient client);

    /// <summary>
    /// <c>StructPlayer::RegainSummon</c> (<c>StructPlayer.cpp:11332-11421</c>, 7.3 binary <c>0x1400d5e60</c>):
    /// takes the card named by <paramref name="creatureCardHandle"/> back out of the farm, grants the farmed
    /// experience it accumulated and removes its farm row. Returns false — and changes nothing — on any
    /// refused exit: the card is not the character's, it does not carry
    /// <c>ITEM_FLAG_FARMED_SUMMON</c>, no farm row names it, it carries no summon, or its summon's form is
    /// neither the first nor the second one.
    /// See docs/packet-specs/6004-retrieve-creature.md §5.5.
    /// </summary>
    Task<bool> RetrieveCreatureAsync(GameClient client, uint creatureCardHandle);

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
/// The retrieval (<c>6004</c> and the expired entries of a <c>6000</c>) is the same
/// <c>RegainSummon</c> gesture: one private seam serves both callers, so a retrieval cannot grant
/// different experience depending on which frame triggered it (§5.8). Every refused exit leaves the card
/// and its row untouched — the reference's checks all precede its first write.
/// </para>
/// </summary>
public sealed class CreatureFarmService : ICreatureFarmService
{
    private readonly ILogger _logger = Log.ForContext<CreatureFarmService>();
    private readonly ICreatureFarmStore _store;
    private readonly Func<DateTime> _localNow;
    private readonly ICreatureService _creatures;
    private readonly ICreatureCatalog _catalog;
    private readonly Navislamia.Game.Scripting.IScriptService _scripts;

    /// <param name="localNow">
    /// The local server clock, as for the nursing reset at 06:00 (<c>StructPlayer.cpp:11439-11456</c>).
    /// </param>
    /// <param name="creatures">
    /// The session's cards: the retrieval resolves the card named by the frame through
    /// <see cref="ICreatureService.FindCard"/>, which is what ties the handle to the calling character.
    /// </param>
    /// <param name="catalog">
    /// The summons and the experience curve: the form served by the retrieval, the hourly rate it applies
    /// and the cap its curve imposes.
    /// </param>
    /// <param name="scripts">
    /// The interpreter the nursing gesture asks for its verdict, as the official server does
    /// (<c>return NPC_Creature_Farm_nurse_handler()</c>). Null — or a function that is not loaded — reads as
    /// "no script", which the verdict maps to <c>NO_REWARD</c> (A VERIFIER 1).
    /// </param>
    public CreatureFarmService(ICreatureFarmStore store, Func<DateTime> localNow = null,
        ICreatureService creatures = null, ICreatureCatalog catalog = null,
        Navislamia.Game.Scripting.IScriptService scripts = null)
    {
        _store = store;
        _localNow = localNow ?? (() => DateTime.Now);
        _creatures = creatures;
        _catalog = catalog;
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
        var entries = new List<GameFarmPackets.FarmSummonInfo>(farm.Count);
        foreach (var row in farm)
        {
            // onRequestFarmInfo (GameMessage.cpp:11747-11751): an entry whose ticket has expired is
            // retrieved before the answer, announced by the @1158 token, and left out of the list — neither
            // creature_count nor the frame's size counts it.
            if (IsExpired(row, now))
            {
                try
                {
                    if (!await RegainAsync(client, characterName, row, now))
                    {
                        _logger.Warning(
                            "The expired farm entry {slot} of {characterName} could not be retrieved",
                            row.Slot, characterName);
                    }
                }
                catch (Exception exception)
                {
                    _logger.Error(exception,
                        "Could not retrieve the expired farm entry {slot} of {characterName}", row.Slot,
                        characterName);
                }

                client.Connection.Send(GameChatPackets.BuildChat("@NOTICE",
                    (byte)Network.Packets.Enums.ChatType.Notice, NoticeFarmRegained));
                continue;
            }

            entries.Add(CreatureFarmRules.SummonInfo(row.Slot, row.Experience, row.Name, row.Duration,
                row.RegistrationTime, row.NursingTime, row.IsCash, row.IsUsingCracker, row.CardInfo, now));
        }

        client.Connection.Send(GameFarmPackets.BuildFarmInfo(entries));
        _logger.Debug("TM_SC_FARM_INFO ({id}) sent to {clientTag} for {characterName} with {count} entries",
            (ushort)Network.Packets.Enums.GamePackets.TM_SC_FARM_INFO, client.ClientTag, characterName,
            entries.Count);
        return true;
    }

    public async Task<bool> RetrieveCreatureAsync(GameClient client, uint creatureCardHandle)
    {
        var characterName = client?.ConnectionInfo?.CharacterName;
        if (string.IsNullOrEmpty(characterName))
        {
            _logger.Warning("Refused a farm retrieval for {clientTag}: the session carries no character",
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
            _logger.Error(exception, "Could not read the creature farm of {characterName}", characterName);
            return false;
        }

        // RegainSummon's step 3 (StructPlayer.cpp:11343-11348): a farm entry has to name the card. The row
        // carries the item's own id, which is what the wire handle is (CreatureCard.Handle).
        var row = farm.FirstOrDefault(f => f.CardItemId == creatureCardHandle);
        if (row is null)
        {
            _logger.Debug(
                "Refused the retrieval of card {cardHandle} for {characterName}: no farm entry names it",
                creatureCardHandle, characterName);
            return false;
        }

        return await RegainAsync(client, characterName, row, _localNow());
    }

    /// <summary>
    /// There is one retrieval gesture for the two callers: the client's <c>6004</c> and the expired entry of a
    /// <c>6000</c> (§5.8). Steps 1 to 4 are checks, step 5 and onward are the writes, so a refusal never leaves
    /// a partial state (§5.2).
    /// </summary>
    private async Task<bool> RegainAsync(GameClient client, string characterName, FarmedSummon row, DateTime now)
    {
        if (_creatures is null || _catalog is null)
        {
            _logger.Error("A farm retrieval was asked of a service built without its creature service or catalogue");
            return false;
        }

        // Steps 1, 2 and 4: the card, farmed, and carrying a summon. FindCard already refuses an unknown
        // handle and a card without a summon.
        var card = _creatures.FindCard(client.ConnectionInfo, (uint)row.CardItemId);
        if (card is null)
        {
            _logger.Warning("Refused the retrieval of farm entry {slot} of {characterName}: card {cardItemId} " +
                "is not in the session's bag or carries no summon", row.Slot, characterName, row.CardItemId);
            return false;
        }

        if (!CreatureFarmRules.IsFarmed(card.Flag))
        {
            _logger.Warning("Refused the retrieval of farm entry {slot} of {characterName}: card {cardItemId} " +
                "does not carry ITEM_FLAG_FARMED_SUMMON", row.Slot, characterName, row.CardItemId);
            return false;
        }

        if (!_catalog.TryGetSummon(card.SummonCode, out var summon))
        {
            _logger.Warning("Refused the retrieval of farm entry {slot} of {characterName}: summon {summonCode} " +
                "is not in the catalogue", row.Slot, characterName, card.SummonCode);
            return false;
        }

        // Step 11: GetTransformLevel() serves the first and the second form and refuses everything else — an
        // evolved summon (third form) cannot be retrieved in this era (0x1400d605b).
        var form = summon.Form;
        if (form != 1 && form != 2)
        {
            _logger.Warning("Refused the retrieval of farm entry {slot} of {characterName}: form {form} of " +
                "summon {summonCode} is not served", row.Slot, characterName, form, card.SummonCode);
            return false;
        }

        // Steps 8, 10 and 11a/11b: the whole hours farmed, the frozen max_level capped by 100 then by the
        // form's own ceiling, the form's hourly rate and the cracker's multiplier.
        var levelLimit = CreatureFarmRules.LevelLimit(row.MaxLevel,
            form == 2 ? CreatureFarmRules.GrowthFormLevelCap : CreatureFarmRules.NormalFormLevelCap);
        var hours = CreatureFarmRules.FarmedHours(row.RegistrationTime, row.Duration, now);
        var gain = CreatureFarmRules.GainedExp(hours,
            form == 2 ? CreatureFarmRules.GrowthExpPerHour : CreatureFarmRules.NormalExpPerHour,
            row.IsUsingCracker);

        // Steps 12 and 13: the curve of the summon itself, at the level limit the deposition froze. Without
        // this min the gain would only be capped by the form's curve (60 or 115), i.e. above a max_level the
        // player had not reached when the card was deposited.
        var expLimit = _catalog.NeedExp(levelLimit) - 1;
        gain = expLimit <= card.Exp ? 0 : Math.Min(gain, expLimit - card.Exp);

        // Step 13: AddExp(nExp, 0, false, true) — the farmed gain is not capped by level² × 200, only by the
        // curve above, hence force: true. Nothing to re-push here: the repository keeps the card in the bag,
        // ITEM_FLAG_FARMED_SUMMON being the only mark (§6.3).
        if (gain > 0)
        {
            _creatures.GainExperience(client, card, gain, force: true);
        }

        // Step 15: the row disappears, with the card's flag cleared in the same write. It happens even when no
        // experience was granted — result = 1 means "the card is taken back", not "exp was gained" (§5.2).
        var removed = await _store.RemoveAsync(characterName, row.CardItemId);
        if (!removed)
        {
            _logger.Warning("Farm entry {slot} of {characterName} was retrieved but its row was not removed",
                row.Slot, characterName);
        }

        _logger.Debug("Farm entry {slot} of {characterName} retrieved: card {cardItemId}, form {form}, " +
            "{hours} farmed hours, {gain} experience granted", row.Slot, characterName, row.CardItemId, form,
            hours, gain);
        return true;
    }

    /// <summary>
    /// <c>pInfo->registration_time + pInfo->duration &lt; time( NULL )</c> (<c>GameMessage.cpp:11747</c>): the
    /// ticket covers a window, and an entry past its end is retrieved on the next farm read.
    /// </summary>
    private static bool IsExpired(FarmedSummon row, DateTime now) =>
        row.RegistrationTime.AddSeconds(row.Duration > 0 ? row.Duration : 0) < now;

    /// <summary>
    /// The notice of a retrieval the player did not ask for: the reference sends the token
    /// <c>"@1158"</c> on <c>CHAT_NOTICE</c> with the sender <c>@NOTICE</c>
    /// (<c>PrintfChatMessage(false, CHAT_NOTICE, "@NOTICE", pClient, "@1158")</c>,
    /// <c>GameMessage.cpp:11750</c>). The client resolves the token from its own string table; the server
    /// has no such table (see §7 and A VERIFIER 8).
    /// </summary>
    private const string NoticeFarmRegained = "@1158";

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
