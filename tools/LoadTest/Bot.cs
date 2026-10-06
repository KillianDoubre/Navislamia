using System.Diagnostics;

namespace Navislamia.LoadTest;

public sealed class BotFailure : Exception
{
    public BotFailure(string kind) : base(kind) { }
}

/// <summary>
/// One headless player: the client's own login sequence (auth, server list, one-time key, game login, lobby,
/// world entry), then a life of walking near its spawn, local chat and, unless disabled, fighting the monsters
/// it sees. It answers the time sync, follows its own position from the server's echoes and resurrects in town
/// when it dies. Every answer it waits for is timed into <see cref="RunMetrics"/>.
/// </summary>
public sealed class Bot
{
    /// <summary>Walking pace the bot assumes, a little under the server's estimate (17 × 100 / 30 ≈ 57 units/s).</summary>
    private const float UnitsPerSecond = 50f;

    private static readonly TimeSpan LoginTimeout = TimeSpan.FromSeconds(30);

    private readonly int _index;
    private readonly LoadOptions _options;
    private readonly CharacterTemplates _templates;
    private readonly RunMetrics _metrics;
    private readonly Random _random;
    private readonly object _state = new();
    private readonly Dictionary<uint, (float X, float Y)> _monsters = new();

    private PacketLink? _game;
    private uint _handle;
    private float _x, _y, _homeX, _homeY;
    private bool _dead;
    private long _deadSince;
    private long _pendingMove, _pendingChat, _pendingAttack;
    private uint _target;
    private long _attackAt;
    private int _chatSequence;

    public Bot(int index, LoadOptions options, CharacterTemplates templates, RunMetrics metrics)
    {
        _index = index;
        _options = options;
        _templates = templates;
        _metrics = metrics;
        _random = new Random(options.Seed + index);
    }

    public string Account => LoadOptions.AccountName(_options.Prefix, _index);

    public async Task RunAsync(CancellationToken stop)
    {
        _metrics.BeginConnecting();
        var connecting = true;
        var inWorld = false;
        try
        {
            var (ip, port, key) = await AuthenticateAsync(stop);
            _game = new PacketLink(ip, port, _options.GameKey, _metrics);
            await EnterWorldAsync(key, stop);
            _metrics.EndConnecting();
            connecting = false;
            _metrics.EnterWorld();
            inWorld = true;

            var receive = ReceiveAsync(stop);
            var act = ActAsync(stop);
            await Task.WhenAny(receive, act);
            if (!stop.IsCancellationRequested) _metrics.Problem("monde : déconnecté par le serveur");
        }
        catch (OperationCanceledException) when (stop.IsCancellationRequested)
        {
        }
        catch (BotFailure failure)
        {
            _metrics.Problem(failure.Message);
        }
        catch (TimeoutException timeout)
        {
            _metrics.Problem($"{(inWorld ? "monde" : "connexion")} : délai dépassé ({timeout.Message})");
        }
        catch (Exception exception) when (exception is IOException or System.Net.Sockets.SocketException)
        {
            _metrics.Problem($"{(inWorld ? "monde" : "connexion")} : {exception.GetType().Name} {exception.Message}");
        }
        finally
        {
            if (connecting) _metrics.EndConnecting();
            if (inWorld) _metrics.LeaveWorld();
            _game?.Dispose();
        }
    }

    private async Task<(string Ip, int Port, long Key)> AuthenticateAsync(CancellationToken stop)
    {
        var start = Stopwatch.GetTimestamp();
        using var auth = new PacketLink(_options.AuthIp, _options.AuthPort, _options.AuthKey, _metrics);
        auth.Send(Frames.AuthVersionFrame());
        auth.Send(Frames.AuthAccountFrame(Account, _options.Password));
        var result = Frames.AuthResultCode(await auth.WaitForAsync(Frames.AuthResult, LoginTimeout, stop));
        if (result != 0) throw new BotFailure($"auth : compte refusé (code {result}) — lancer « seed » ?");

        auth.Send(Frames.AuthServerListFrame());
        var servers = Frames.ServerList(await auth.WaitForAsync(Frames.AuthServerList, LoginTimeout, stop));
        if (servers.Count == 0) throw new BotFailure("auth : aucun serveur de jeu enregistré");
        var server = _options.ServerIndex is { } wanted
            ? servers.FirstOrDefault(s => s.Index == wanted)
            : servers[0];
        if (server.Ip is null) throw new BotFailure($"auth : serveur {_options.ServerIndex} absent de la liste");

        auth.Send(Frames.AuthSelectServerFrame(server.Index));
        var (code, key) = Frames.SelectServer(await auth.WaitForAsync(Frames.AuthSelectServerResult, LoginTimeout, stop));
        if (code != 0) throw new BotFailure($"auth : sélection du serveur refusée (code {code})");

        _metrics.AuthLogin.Record(Stopwatch.GetElapsedTime(start).TotalMilliseconds);
        return (_options.GameIp ?? server.Ip, _options.GamePort ?? server.Port, key);
    }

    private async Task EnterWorldAsync(long key, CancellationToken stop)
    {
        var game = _game!;
        game.Send(Frames.VersionFrame());
        game.Send(Frames.AccountWithAuthFrame(Account, key));
        var (_, verified) = Frames.ResultOf(await game.WaitForAsync(Frames.Result, LoginTimeout, stop,
            f => Frames.ResultOf(f).Request == Frames.AccountWithAuth));
        if (verified != 0) throw new BotFailure($"jeu : clé refusée (code {verified})");

        var characters = await CharacterListAsync(game, stop);
        if (characters.Count == 0)
        {
            var name = LoadOptions.CharacterName(_options.Prefix, _index);
            game.Send(Frames.CreateCharacterFrame(_templates.For(_index, name)));
            var (_, created) = Frames.ResultOf(await game.WaitForAsync(Frames.Result, LoginTimeout, stop,
                f => Frames.ResultOf(f).Request == Frames.CreateCharacter));
            if (created != 0) throw new BotFailure($"jeu : création de {name} refusée (code {created})");
            characters = await CharacterListAsync(game, stop);
            if (characters.Count == 0) throw new BotFailure("jeu : personnage créé mais absent de la liste");
        }

        var (characterName, race) = characters[0];
        var start = Stopwatch.GetTimestamp();
        game.Send(Frames.LoginFrame(characterName, race));
        var login = Frames.LoginResultOf(await game.WaitForAsync(Frames.LoginResult, LoginTimeout, stop, other: f =>
        {
            if (Frames.Id(f) == Frames.Result && Frames.ResultOf(f) is { Request: Frames.Login } refused)
                throw new BotFailure($"jeu : entrée refusée (code {refused.Code})");
            Handle(f);
        }));
        if (login.Result != 0) throw new BotFailure($"jeu : entrée refusée (code {login.Result})");

        _metrics.WorldEntry.Record(Stopwatch.GetElapsedTime(start).TotalMilliseconds);
        lock (_state)
        {
            _handle = login.Handle;
            (_x, _y) = (login.X, login.Y);
            (_homeX, _homeY) = (login.X, login.Y);
            _dead = login.Hp <= 0;
        }
    }

    private async Task<List<(string Name, int Race)>> CharacterListAsync(PacketLink game, CancellationToken stop)
    {
        game.Send(Frames.CharacterListFrame(Account));
        return Frames.Characters(await game.WaitForAsync(Frames.CharacterListResult, LoginTimeout, stop));
    }

    private async Task ReceiveAsync(CancellationToken stop)
    {
        await foreach (var frame in _game!.Inbox.ReadAllAsync(stop))
        {
            Handle(frame);
        }
    }

    private void Handle(byte[] f)
    {
        var id = Frames.Id(f);
        lock (_state)
        {
            switch (id)
            {
                case Frames.TimeSync:
                    _game!.Send(Frames.TimeSyncFrame(ClientTick()));
                    break;

                case Frames.Move when f.Length >= 19:
                {
                    var mover = Frames.U32(f, 11);
                    if (Frames.MoveDestination(f) is not { } to) break;
                    if (mover == _handle)
                    {
                        (_x, _y) = to;
                        if (_pendingMove != 0)
                        {
                            _metrics.MoveEcho.Record(Stopwatch.GetElapsedTime(_pendingMove).TotalMilliseconds);
                            _pendingMove = 0;
                        }
                    }
                    else if (_monsters.ContainsKey(mover))
                    {
                        _monsters[mover] = to;
                    }

                    break;
                }

                case Frames.Enter when f.Length >= 26:
                    if (f[25] == 3) _monsters[Frames.U32(f, 8)] = (Frames.F32(f, 12), Frames.F32(f, 16));
                    break;

                case Frames.Leave when f.Length >= 11:
                {
                    var gone = Frames.U32(f, 7);
                    _monsters.Remove(gone);
                    if (gone == _target) _target = 0;
                    break;
                }

                case Frames.StatusChange when f.Length >= 15:
                {
                    var who = Frames.U32(f, 7);
                    if ((Frames.U32(f, 11) & (1u << 8)) != 0 && _monsters.Remove(who) && who == _target) _target = 0;
                    break;
                }

                case Frames.AttackEvent when f.Length >= 22:
                {
                    var (attacker, target, hp) = Frames.AttackOf(f);
                    if (attacker == _handle)
                    {
                        if (hp is not null) _metrics.SwingsLanded.Increment();
                        if (_pendingAttack != 0)
                        {
                            _metrics.AttackFirstSwing.Record(Stopwatch.GetElapsedTime(_pendingAttack).TotalMilliseconds);
                            _pendingAttack = 0;
                        }

                        if (hp == 0)
                        {
                            _metrics.Kills.Increment();
                            _monsters.Remove(target);
                            if (target == _target) _target = 0;
                        }
                    }
                    else if (target == _handle && hp == 0)
                    {
                        Die();
                    }

                    break;
                }

                case Frames.Property when f.Length >= 36:
                {
                    var (who, name, value) = Frames.PropertyOf(f);
                    if (who != _handle || name != "hp") break;
                    if (value <= 0) Die();
                    else _dead = false;
                    break;
                }

                case Frames.ChatLocal when f.Length >= 11:
                    if (Frames.U32(f, 7) == _handle && _pendingChat != 0)
                    {
                        _metrics.ChatEcho.Record(Stopwatch.GetElapsedTime(_pendingChat).TotalMilliseconds);
                        _pendingChat = 0;
                    }

                    break;

                case Frames.Warp when f.Length >= 15:
                    (_x, _y) = (Frames.F32(f, 7), Frames.F32(f, 11));
                    (_homeX, _homeY) = (_x, _y);
                    _monsters.Clear();
                    _target = 0;
                    break;

                case Frames.Result when f.Length >= 11:
                {
                    var (request, code) = Frames.ResultOf(f);
                    if (code != 0) _metrics.Problem($"refus : requête {request} → code {code}");
                    if (code != 0 && request == Frames.AttackRequest) _target = 0;
                    break;
                }

                case Frames.CantAttack when f.Length >= 19:
                    _metrics.Problem($"refus : attaque impossible (raison {Frames.I32(f, 15)})");
                    _target = 0;
                    break;

                case Frames.Disconnect:
                    _metrics.Problem("monde : TS_SC_DISCONNECT_DESC reçu");
                    break;
            }
        }
    }

    private void Die()
    {
        if (_dead) return;
        _dead = true;
        _deadSince = Stopwatch.GetTimestamp();
        _target = 0;
        _attackAt = 0;
        _pendingAttack = 0;
        _metrics.Deaths.Increment();
    }

    private async Task ActAsync(CancellationToken stop)
    {
        var nextMove = Stopwatch.GetTimestamp() + Seconds(_random.NextDouble() * _options.MoveInterval);
        var nextChat = Stopwatch.GetTimestamp() + Seconds(_random.NextDouble() * _options.ChatInterval);
        var nextHunt = Stopwatch.GetTimestamp() + Seconds(2 + _random.NextDouble() * 3);
        long attackUntil = 0, resurrectAt = 0;
        var game = _game!;

        while (!stop.IsCancellationRequested && game.Connected)
        {
            await Task.Delay(200, stop);
            var now = Stopwatch.GetTimestamp();
            lock (_state)
            {
                if (_pendingMove != 0 && Stopwatch.GetElapsedTime(_pendingMove).TotalSeconds > 5)
                {
                    _metrics.MovesLost.Increment();
                    _pendingMove = 0;
                }

                if (_pendingChat != 0 && Stopwatch.GetElapsedTime(_pendingChat).TotalSeconds > 5)
                {
                    _metrics.Problem("monde : chat sans écho en 5 s");
                    _pendingChat = 0;
                }

                if (_dead)
                {
                    // TM_CS_RESURRECTION type 0: back at the return point, which the server answers with a warp.
                    if (Stopwatch.GetElapsedTime(_deadSince).TotalSeconds > 3 && now > resurrectAt)
                    {
                        game.Send(Frames.ResurrectionFrame(_handle));
                        resurrectAt = now + Seconds(10);
                    }

                    continue;
                }

                if (now > nextChat)
                {
                    game.Send(Frames.ChatFrame($"loadtest {_index} {++_chatSequence}"));
                    _metrics.ChatsSent.Increment();
                    _pendingChat = now;
                    nextChat = now + Seconds(_options.ChatInterval * (0.75 + _random.NextDouble() * 0.5));
                }

                if (_target != 0 && _attackAt != 0 && now >= _attackAt)
                {
                    // Sent once the walk is over, as the client does: the time measured is the server's, not the walk's.
                    game.Send(Frames.AttackFrame(_handle, _target));
                    _metrics.AttacksSent.Increment();
                    _pendingAttack = now;
                    _attackAt = 0;
                }

                if (_target != 0)
                {
                    if (now > attackUntil)
                    {
                        game.Send(Frames.CancelActionFrame(_handle));
                        _target = 0;
                        nextMove = now;
                    }

                    continue;
                }

                if (_options.Combat && now > nextHunt && TryPickMonster(out var monster, out var at))
                {
                    var travel = WalkTo(game, now, at.X, at.Y, keepDistance: 10);
                    _target = monster;
                    _attackAt = now + Seconds(travel);
                    attackUntil = now + Seconds(travel + _options.AttackSeconds);
                    nextHunt = attackUntil + Seconds(2 + _random.NextDouble() * 4);
                    continue;
                }

                if (now > nextMove)
                {
                    var angle = _random.NextDouble() * Math.PI * 2;
                    var radius = _random.NextDouble() * _options.WalkRadius;
                    var travel = WalkTo(game, now, _homeX + (float)(Math.Cos(angle) * radius),
                        _homeY + (float)(Math.Sin(angle) * radius), keepDistance: 0);
                    nextMove = now + Seconds(travel + _options.MoveInterval * (0.5 + _random.NextDouble()));
                }
            }
        }
    }

    /// <summary>Sends one straight walk and returns how long it takes, in seconds. The caller holds the state lock.</summary>
    private double WalkTo(PacketLink game, long now, float x, float y, float keepDistance)
    {
        var dx = x - _x;
        var dy = y - _y;
        var length = MathF.Sqrt(dx * dx + dy * dy);
        if (length <= keepDistance + 1) return 0;
        var ratio = (length - keepDistance) / length;
        var toX = _x + dx * ratio;
        var toY = _y + dy * ratio;
        game.Send(Frames.MoveRequestFrame(_handle, _x, _y, ClientTick(), toX, toY));
        _metrics.MovesSent.Increment();
        if (_pendingMove == 0) _pendingMove = now;
        return (length - keepDistance) / UnitsPerSecond;
    }

    private bool TryPickMonster(out uint handle, out (float X, float Y) at)
    {
        handle = 0;
        at = default;
        var best = _options.HuntRange * _options.HuntRange;
        foreach (var (h, p) in _monsters)
        {
            var d = (p.X - _x) * (p.X - _x) + (p.Y - _y) * (p.Y - _y);
            if (d < best)
            {
                best = d;
                handle = h;
                at = p;
            }
        }

        return handle != 0;
    }

    private static long Seconds(double seconds) => (long)(seconds * Stopwatch.Frequency);

    /// <summary>The client's own ar_time: a 10 ms tick (CLAUDE.md, Client clock).</summary>
    private static uint ClientTick() => unchecked((uint)(Environment.TickCount64 / 10));
}
