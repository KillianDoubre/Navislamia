using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Navislamia.Game.Network.Clients;
using Navislamia.Game.Network.Packets;
using Navislamia.Game.Network.Packets.Game;
using Navislamia.Game.Services.Interfaces;
using Serilog;

namespace Navislamia.Game.Services.Compete;

/// <summary><c>_COMPETE_END_TYPE</c>, as the official callers of <c>RetireCompeteWithPlayer</c> pass it.</summary>
public enum CompeteEndType : byte
{
    /// <summary>The loser died (<c>StructPlayer::onDead</c>).</summary>
    Win = 0,

    /// <summary>A competitor logged out (<c>LogoutNow</c>).</summary>
    Logout = 1,

    /// <summary>A competitor went farther than 500 from where the duel started (<c>Validate</c>).</summary>
    OutOfRange = 2,

    /// <summary>The answer or the duel ran out of time (<c>OnAnswerTimeout</c>, <c>OnCompeteTimeout</c>).</summary>
    Timeout = 3,

    /// <summary>Someone else hurt a competitor (<c>StructPlayer::onDamage</c>).</summary>
    Interfered = 4,

    /// <summary>A competitor changed location (<c>ChangeLocation</c>): a warp.</summary>
    LeftField = 5
}

public enum CompeteStatus
{
    Requested,
    Countdown,
    Started
}

/// <summary>One duel between two players, guarded by the service's lock.</summary>
public sealed class CompeteInfo
{
    public GameClient Requester { get; init; }
    public GameClient Requestee { get; init; }
    public byte CompeteType { get; init; }
    public CompeteStatus Status { get; set; }
    public DateTime Deadline { get; set; }
    public float StartX { get; set; }
    public float StartY { get; set; }

    public GameClient Other(GameClient one) => ReferenceEquals(one, Requester) ? Requestee : Requester;

    public bool Involves(GameClient client) => ReferenceEquals(client, Requester) || ReferenceEquals(client, Requestee);
}

public interface ICompeteService
{
    void Request(GameClient client, GameCompetePackets.CompeteRequest request);

    void Answer(GameClient client, GameCompetePackets.CompeteAnswer answer);

    /// <summary>Whether the two are each other's opponent in a duel that has started.</summary>
    bool AreCompeting(GameClient a, GameClient b);

    /// <summary>The loser of a started duel died at the winner's hand: the duel ends with <see cref="CompeteEndType.Win"/>.</summary>
    void OnKilledBy(GameClient loser, GameClient winner);

    /// <summary>A competitor took damage from anyone but its opponent (a monster: <paramref name="attacker"/> null).</summary>
    void OnDamagedByOther(GameClient target, GameClient attacker);

    /// <summary>A competitor leaves (logout, lobby, warp): the duel ends, and the leaver loses it.</summary>
    void Leave(GameClient client, CompeteEndType reason);

    /// <summary>
    /// <c>ResurrectByCompete</c> (513 type 3): only a player who lost a duel by dying may use it; true once per loss.
    /// </summary>
    bool ConsumeLoss(GameClient client);

    /// <summary>Timeouts, countdown, start, range; the tick calls it, the tests too.</summary>
    void Process(DateTime now);
}

/// <summary>
/// The duel of the official server (<c>CompeteManager</c>, <c>PlayerCompeteInfo</c>, 2012-11
/// <c>0x140250770</c>…<c>0x14024e660</c>; docs/packet-specs/socle-competition-joueurs.md §10): an invitation the
/// target answers within 60 s, a countdown of 10 s, a duel of at most 900 s within 500 units of where it began,
/// ended by a death, a departure, the range, the clock or a third party.
/// </summary>
public sealed class CompeteService : ICompeteService
{
    public static readonly TimeSpan AnswerTime = TimeSpan.FromSeconds(60);
    public static readonly TimeSpan CountdownTime = TimeSpan.FromSeconds(10);
    public static readonly TimeSpan DuelTime = TimeSpan.FromSeconds(900);

    /// <summary>The distance an invitation reaches and the range a duel keeps around its start (both 500).</summary>
    public const float Range = 500f;

    /// <summary>The <c>answer_type</c> the server sends when nobody answered.</summary>
    public const byte AnswerTimedOut = 3;

    /// <summary>The <c>answer_type</c> of an acceptance (<c>battle_start</c>).</summary>
    public const byte AnswerAccept = 0;

    private readonly ILogger _logger = Log.ForContext<CompeteService>();
    private readonly IPlayerVisibilityService _players;
    private readonly Func<DateTime> _clock;
    private readonly object _lock = new();
    private readonly List<CompeteInfo> _competes = new();
    private readonly HashSet<GameClient> _losers = new();

    public CompeteService(IPlayerVisibilityService players, Func<DateTime> clock = null, bool runTicks = true)
    {
        _players = players;
        _clock = clock ?? (() => DateTime.UtcNow);
        if (runTicks)
        {
            _ = RunAsync();
        }
    }

    /// <summary>
    /// <c>RequestCompeteToPlayer</c>, in its order: the requester already invited someone (63) or is fighting (61),
    /// the target is answering another (67) or fighting (65), the target is farther than 500 (2). Fields and battle
    /// fields are not modelled: every place allows a duel.
    /// </summary>
    public static ResultCode CheckRequest(CompeteStatus? requester, CompeteStatus? requestee, float distance)
    {
        if (requester is { } own)
        {
            return own == CompeteStatus.Requested ? ResultCode.WaitingCompeteRequestAnswer : ResultCode.AlreadyInCompete;
        }

        if (requestee is { } other)
        {
            return other == CompeteStatus.Requested
                ? ResultCode.TargetWaitingCompeteRequestAnswer
                : ResultCode.TargetAlreadyInCompete;
        }

        return distance > Range ? ResultCode.TooFar : ResultCode.Success;
    }

    public void Request(GameClient client, GameCompetePackets.CompeteRequest request)
    {
        const ushort requestId = (ushort)Network.Packets.Enums.GamePackets.TM_CS_COMPETE_REQUEST;
        var info = client.ConnectionInfo;
        var target = _players?.Registry.Clients.FirstOrDefault(other =>
            !ReferenceEquals(other, client)
            && string.Equals(other.ConnectionInfo.CharacterName, request.Requestee, StringComparison.OrdinalIgnoreCase));
        if (target is null)
        {
            client.SendResult(requestId, (ushort)ResultCode.NotExist);
            return;
        }

        var distance = CombatRange.Distance(info.X, info.Y, target.ConnectionInfo.X, target.ConnectionInfo.Y);
        ResultCode result;
        lock (_lock)
        {
            result = CheckRequest(Find(client)?.Status, Find(target)?.Status, distance);
            if (result == ResultCode.Success)
            {
                _competes.Add(new CompeteInfo
                {
                    Requester = client,
                    Requestee = target,
                    CompeteType = (byte)request.CompeteType,
                    Status = CompeteStatus.Requested,
                    Deadline = _clock() + AnswerTime
                });
            }
        }

        client.SendResult(requestId, (ushort)result);
        if (result == ResultCode.Success)
        {
            target.Connection.Send(GameCompeteServerPackets.BuildRequest((byte)request.CompeteType, info.CharacterName));
            _logger.Debug("{requester} invited {requestee} to a duel", info.CharacterName, target.ConnectionInfo.CharacterName);
        }
    }

    public void Answer(GameClient client, GameCompetePackets.CompeteAnswer answer)
    {
        const ushort requestId = (ushort)Network.Packets.Enums.GamePackets.TM_CS_COMPETE_ANSWER;
        CompeteInfo compete;
        lock (_lock)
        {
            compete = _competes.FirstOrDefault(c => ReferenceEquals(c.Requestee, client) && c.Status == CompeteStatus.Requested);
            if (compete is null)
            {
                client.SendResult(requestId, (ushort)ResultCode.NotInCompete);
                return;
            }

            if (answer.AnswerType != AnswerAccept)
            {
                _competes.Remove(compete);
            }
            else
            {
                // OnAnswerRequest: the countdown starts; the duel's ground is where the two stand.
                compete.Status = CompeteStatus.Countdown;
                compete.Deadline = _clock() + CountdownTime;
                compete.StartX = (compete.Requester.ConnectionInfo.X + client.ConnectionInfo.X) / 2f;
                compete.StartY = (compete.Requester.ConnectionInfo.Y + client.ConnectionInfo.Y) / 2f;
            }
        }

        var requester = compete.Requester;
        requester.Connection.Send(GameCompeteServerPackets.BuildAnswer(compete.CompeteType, (byte)answer.AnswerType,
            client.ConnectionInfo.CharacterName));
        if (answer.AnswerType != AnswerAccept)
        {
            return;
        }

        requester.Connection.Send(GameCompeteServerPackets.BuildCountdown(compete.CompeteType,
            client.ConnectionInfo.CharacterName, client.ConnectionInfo.CharacterHandle));
        client.Connection.Send(GameCompeteServerPackets.BuildCountdown(compete.CompeteType,
            requester.ConnectionInfo.CharacterName, requester.ConnectionInfo.CharacterHandle));
    }

    public bool AreCompeting(GameClient a, GameClient b)
    {
        if (a is null || b is null || ReferenceEquals(a, b))
        {
            return false;
        }

        lock (_lock)
        {
            return _competes.Any(c => c.Status == CompeteStatus.Started && c.Involves(a) && c.Involves(b));
        }
    }

    public void OnKilledBy(GameClient loser, GameClient winner)
    {
        CompeteInfo compete;
        lock (_lock)
        {
            compete = _competes.FirstOrDefault(c => c.Status == CompeteStatus.Started && c.Involves(loser) && c.Involves(winner));
            if (compete is null)
            {
                return;
            }

            _competes.Remove(compete);
            _losers.Add(loser);
        }

        SendEnd(compete, CompeteEndType.Win, winner, loser);
    }

    public void OnDamagedByOther(GameClient target, GameClient attacker)
    {
        CompeteInfo compete;
        lock (_lock)
        {
            compete = _competes.FirstOrDefault(c => c.Status == CompeteStatus.Started && c.Involves(target));
            if (compete is null || attacker is not null && compete.Involves(attacker))
            {
                return;
            }

            _competes.Remove(compete);
        }

        // The hurt competitor is the one written as the loser: the duel is void.
        SendEnd(compete, CompeteEndType.Interfered, compete.Other(target), target);
    }

    public void Leave(GameClient client, CompeteEndType reason)
    {
        CompeteInfo compete;
        lock (_lock)
        {
            _losers.Remove(client);
            compete = _competes.FirstOrDefault(c => c.Involves(client));
            if (compete is null)
            {
                return;
            }

            _competes.Remove(compete);
        }

        if (compete.Status == CompeteStatus.Requested)
        {
            return;
        }

        SendEnd(compete, reason, compete.Other(client), client);
    }

    public bool ConsumeLoss(GameClient client)
    {
        lock (_lock)
        {
            return _losers.Remove(client);
        }
    }

    public void Process(DateTime now)
    {
        var ended = new List<(CompeteInfo Compete, CompeteEndType Type, GameClient Winner, GameClient Loser)>();
        var timedOut = new List<CompeteInfo>();
        var started = new List<CompeteInfo>();

        lock (_lock)
        {
            foreach (var compete in _competes.ToArray())
            {
                switch (compete.Status)
                {
                    case CompeteStatus.Requested when now >= compete.Deadline:
                        _competes.Remove(compete);
                        timedOut.Add(compete);
                        continue;
                    case CompeteStatus.Countdown when now >= compete.Deadline:
                        compete.Status = CompeteStatus.Started;
                        compete.Deadline = now + DuelTime;
                        started.Add(compete);
                        break;
                    case CompeteStatus.Started when now >= compete.Deadline:
                        _competes.Remove(compete);
                        ended.Add((compete, CompeteEndType.Timeout, compete.Requester, compete.Requestee));
                        continue;
                }

                if (compete.Status == CompeteStatus.Requested)
                {
                    continue;
                }

                // Validate: each competitor must stay within 500 of where the duel began.
                foreach (var competitor in new[] { compete.Requester, compete.Requestee })
                {
                    var info = competitor.ConnectionInfo;
                    if (CombatRange.Distance(compete.StartX, compete.StartY, info.X, info.Y) > Range)
                    {
                        _competes.Remove(compete);
                        started.Remove(compete);
                        ended.Add((compete, CompeteEndType.OutOfRange, compete.Other(competitor), competitor));
                        break;
                    }
                }
            }
        }

        foreach (var compete in timedOut)
        {
            var name = compete.Requestee.ConnectionInfo.CharacterName;
            compete.Requester.Connection.Send(GameCompeteServerPackets.BuildAnswer(compete.CompeteType, AnswerTimedOut, name));
            compete.Requestee.Connection.Send(GameCompeteServerPackets.BuildAnswer(compete.CompeteType, AnswerTimedOut, name));
        }

        foreach (var compete in started)
        {
            compete.Requester.Connection.Send(GameCompeteServerPackets.BuildStart(compete.CompeteType,
                compete.Requestee.ConnectionInfo.CharacterName));
            compete.Requestee.Connection.Send(GameCompeteServerPackets.BuildStart(compete.CompeteType,
                compete.Requester.ConnectionInfo.CharacterName));
        }

        foreach (var (compete, type, winner, loser) in ended)
        {
            SendEnd(compete, type, winner, loser);
        }
    }

    private CompeteInfo Find(GameClient client) => _competes.FirstOrDefault(c => c.Involves(client));

    private static void SendEnd(CompeteInfo compete, CompeteEndType type, GameClient winner, GameClient loser)
    {
        var frame = GameCompeteServerPackets.BuildEnd(compete.CompeteType, (byte)type,
            winner.ConnectionInfo.CharacterName, loser.ConnectionInfo.CharacterName);
        compete.Requester.Connection.Send(frame);
        compete.Requestee.Connection.Send(frame);
    }

    private async Task RunAsync()
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(1));
        while (await timer.WaitForNextTickAsync())
        {
            try
            {
                Process(_clock());
            }
            catch (Exception exception)
            {
                _logger.Error(exception, "The duel tick failed");
            }
        }
    }
}
