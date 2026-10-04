using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Navislamia.Game.Network.Clients;
using Navislamia.Game.Network.Packets.Enums;
using Navislamia.Game.Network.Packets.Game;
using Serilog;

namespace Navislamia.Game.Services.Friends;

/// <summary>
/// Friends and the block list (docs/packet-specs/socle-amis.md). The 7.3 client has no opcode for them: its
/// messenger window sends <c>/add_friend</c>, <c>/del_friend</c>, <c>/add_denial</c> and <c>/del_denial</c> as chat
/// lines and reads the <c>@FRIEND</c> answers. The rules are the official server's (<c>GameMessage.cpp</c>
/// <c>onAddFriend</c>…<c>onDelDenial</c>, <c>DB_Friends.cpp</c>, <c>SendMessage.cpp</c> <c>SendFriendsList</c>,
/// <c>SendDenialsList</c>, <c>SendStatusMessageToFriendOfPlayer</c>).
/// </summary>
public interface IFriendService
{
    /// <summary>Runs a friend chat command; false when the line is not one.</summary>
    bool TryHandleCommand(GameClient client, string message);

    /// <summary>World entry: loads the lists, sends them, and tells the players who list this one as a friend.</summary>
    Task OnWorldEntryAsync(GameClient client);

    /// <summary>Lobby or disconnection: the players who list this one as a friend see them go offline.</summary>
    void OnWorldExit(GameClient client);

    /// <summary>Whether <paramref name="target"/> blocks <paramref name="sender"/> (a whisper is then refused).</summary>
    bool Blocks(GameClient target, GameClient sender);

    /// <summary>A character was deleted: it leaves the lists of the players online (<c>DB_DeleteCharacter</c>).</summary>
    void OnCharacterDeleted(long characterId);
}

public sealed class FriendService : IFriendService
{
    /// <summary><c>onAddFriend</c>/<c>onAddDenial</c> refuse at <c>GetFriendCount() &gt;= 25</c>.</summary>
    public const int MaxEntries = 25;

    /// <summary><c>GameRule::IsValidName(…, 4, 18)</c>, the character name rule.</summary>
    public const int NameMinLength = 4;
    public const int NameMaxLength = 18;

    public const string Sender = "@FRIEND";

    private readonly ILogger _logger = Log.ForContext<FriendService>();
    private readonly IFriendStore _store;
    private readonly object _gate = new();
    private readonly Dictionary<long, Book> _books = new();

    public FriendService(IFriendStore store) => _store = store;

    /// <summary>The lists of a player in the world, and the lock that serialises their commands.</summary>
    private sealed class Book
    {
        public Book(GameClient client, long id, string name, FriendLists lists)
        {
            Client = client;
            Id = id;
            Name = name;
            Friends = lists.Friends.ToList();
            Denials = lists.Denials.ToList();
        }

        public GameClient Client { get; }
        public long Id { get; }
        public string Name { get; }
        public List<FriendEntry> Friends { get; }
        public List<FriendEntry> Denials { get; }
        public SemaphoreSlim Busy { get; } = new(1, 1);

        public List<FriendEntry> List(bool denial) => denial ? Denials : Friends;
    }

    public bool TryHandleCommand(GameClient client, string message)
    {
        var tokens = message.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (tokens.Length == 0)
        {
            return false;
        }

        Func<GameClient, string, Task> handler = tokens[0].ToLowerInvariant() switch
        {
            "/add_friend" => (c, name) => AddAsync(c, name, denial: false),
            "/del_friend" => (c, name) => RemoveAsync(c, name, denial: false),
            "/add_denial" => (c, name) => AddAsync(c, name, denial: true),
            "/del_denial" => (c, name) => RemoveAsync(c, name, denial: true),
            _ => null
        };
        if (handler is null)
        {
            return false;
        }

        // if( vToken.size() != 2 ) return; — a malformed line is consumed and answered with nothing.
        if (tokens.Length == 2)
        {
            _ = RunAsync(client, tokens[0], () => handler(client, tokens[1]));
        }

        return true;
    }

    private async Task RunAsync(GameClient client, string command, Func<Task> action)
    {
        try
        {
            await action();
        }
        catch (Exception exception)
        {
            _logger.Error(exception, "{command} of {clientTag} failed", command, client.ClientTag);
        }
    }

    private Book BookOf(GameClient client)
    {
        lock (_gate)
        {
            return _books.TryGetValue(client.ConnectionInfo.CharacterHandle, out var book)
                && ReferenceEquals(book.Client, client) ? book : null;
        }
    }

    private async Task AddAsync(GameClient client, string name, bool denial)
    {
        var book = BookOf(client);
        if (book is null)
        {
            return;
        }

        await book.Busy.WaitAsync();
        try
        {
            if (string.Equals(name, book.Name, StringComparison.OrdinalIgnoreCase))
            {
                Say(book, "@482");
                return;
            }

            // onAddDenial checks the name the same way; game.allowed_names_as_friend (an empty list by default) is not
            // modelled.
            if (!IsValidName(name))
            {
                Say(book, "@481");
                return;
            }

            lock (_gate)
            {
                if (book.List(denial).Count >= MaxEntries)
                {
                    Say(book, "@478");
                    return;
                }
            }

            var target = await _store.FindCharacterAsync(name);
            if (target is null)
            {
                Say(book, "@481");
                return;
            }

            if (target.Id == book.Id)
            {
                Say(book, "@482");
                return;
            }

            lock (_gate)
            {
                if (book.List(denial).Any(entry => entry.Id == target.Id))
                {
                    Say(book, denial ? Named(473, "denial_name", target.Name) : Named(469, "friend_name", target.Name));
                    return;
                }
            }

            try
            {
                await _store.AddAsync(book.Id, target.Id, denial);
            }
            catch (Exception exception)
            {
                // DB_InsertFriend::onFail: GameContent::GetString( 103 ) followed by "(4)".
                _logger.Error(exception, "Could not add {target} to the lists of {owner}", target.Name, book.Name);
                Say(book, "@103");
                return;
            }

            lock (_gate)
            {
                book.List(denial).Add(target);
                if (denial)
                {
                    SendDenials(book);
                    // The blocked player, if they list this one as a friend, now sees them offline.
                    if (_books.TryGetValue(target.Id, out var blocked) && blocked.Friends.Any(f => f.Id == book.Id))
                    {
                        SendStatus(blocked, book.Name, online: false);
                    }
                }
                else
                {
                    SendFriends(book);
                }
            }

            Say(book, denial ? Named(472, "denial_name", target.Name) : Named(467, "friend_name", target.Name));
        }
        finally
        {
            book.Busy.Release();
        }
    }

    private async Task RemoveAsync(GameClient client, string name, bool denial)
    {
        var book = BookOf(client);
        if (book is null)
        {
            return;
        }

        await book.Busy.WaitAsync();
        try
        {
            FriendEntry entry;
            lock (_gate)
            {
                entry = book.List(denial).FirstOrDefault(e => string.Equals(e.Name, name, StringComparison.OrdinalIgnoreCase));
            }

            if (entry is null)
            {
                Say(book, denial ? Named(475, "denial_name", name) : Named(471, "friend_name", name));
                return;
            }

            try
            {
                await _store.RemoveAsync(book.Id, entry.Id, denial);
            }
            catch (Exception exception)
            {
                // DB_DeleteFriend::onFail: GameContent::GetString( 103 ) followed by "(5)".
                _logger.Error(exception, "Could not remove {target} from the lists of {owner}", entry.Name, book.Name);
                Say(book, "@103");
                return;
            }

            lock (_gate)
            {
                book.List(denial).RemoveAll(e => e.Id == entry.Id);
                if (denial)
                {
                    SendDenials(book);
                    // Unblocked: if they list this one as a friend, they see them online again.
                    if (_books.TryGetValue(entry.Id, out var unblocked) && unblocked.Friends.Any(f => f.Id == book.Id))
                    {
                        SendStatus(unblocked, book.Name, online: true);
                    }
                }
                else
                {
                    SendFriends(book);
                }
            }

            Say(book, denial ? Named(474, "denial_name", entry.Name) : Named(470, "friend_name", entry.Name));
        }
        finally
        {
            book.Busy.Release();
        }
    }

    public async Task OnWorldEntryAsync(GameClient client)
    {
        var info = client.ConnectionInfo;
        var id = info.CharacterHandle;
        var name = info.CharacterName;
        if (id == 0 || string.IsNullOrEmpty(name))
        {
            return;
        }

        FriendLists lists;
        try
        {
            lists = await _store.LoadAsync(id);
        }
        catch (Exception exception)
        {
            _logger.Error(exception, "Could not load the friend lists of {name}", name);
            lists = new FriendLists(Array.Empty<FriendEntry>(), Array.Empty<FriendEntry>());
        }

        // The session may have left while the lists were read.
        if (info.CharacterHandle != id)
        {
            return;
        }

        lock (_gate)
        {
            var book = new Book(client, id, name, lists);
            _books[id] = book;
            SendFriends(book);
            SendDenials(book);
            NotifyFriendOf(book, online: true);
        }
    }

    public void OnWorldExit(GameClient client)
    {
        lock (_gate)
        {
            var id = client.ConnectionInfo.CharacterHandle;
            if (!_books.TryGetValue(id, out var book) || !ReferenceEquals(book.Client, client))
            {
                return;
            }

            _books.Remove(id);
            NotifyFriendOf(book, online: false);
        }
    }

    public bool Blocks(GameClient target, GameClient sender)
    {
        lock (_gate)
        {
            return _books.TryGetValue(target.ConnectionInfo.CharacterHandle, out var book)
                && book.Denials.Any(d => d.Id == sender.ConnectionInfo.CharacterHandle);
        }
    }

    public void OnCharacterDeleted(long characterId)
    {
        lock (_gate)
        {
            foreach (var book in _books.Values)
            {
                if (book.Friends.RemoveAll(f => f.Id == characterId) > 0)
                {
                    SendFriends(book);
                }

                if (book.Denials.RemoveAll(d => d.Id == characterId) > 0)
                {
                    SendDenials(book);
                }
            }
        }
    }

    /// <summary>
    /// <c>SendStatusMessageToFriendOfPlayer(pClient, bIsLogin)</c>: <c>FSTATUS</c> to every online player listing
    /// <paramref name="book"/> as a friend, except those it blocks. Under the gate.
    /// </summary>
    private void NotifyFriendOf(Book book, bool online)
    {
        foreach (var other in _books.Values)
        {
            if (other.Id != book.Id && other.Friends.Any(f => f.Id == book.Id) && book.Denials.All(d => d.Id != other.Id))
            {
                SendStatus(other, book.Name, online);
            }
        }
    }

    /// <summary><c>SendFriendsList</c>: <c>FLIST|name|status|…</c>, 1 online, 2 online but blocking the reader, 0 offline.</summary>
    private void SendFriends(Book book)
    {
        var text = FriendMessages.FriendList(book.Friends.Select(f =>
        {
            var status = !_books.TryGetValue(f.Id, out var friend) ? FriendStatus.Offline
                : friend.Denials.Any(d => d.Id == book.Id) ? FriendStatus.Blocking : FriendStatus.Online;
            return (f.Name, status);
        }));
        book.Client.Connection.Send(GameChatPackets.BuildChat(Sender, (byte)ChatType.FriendSystem, text));
    }

    private static void SendDenials(Book book) => book.Client.Connection.Send(
        GameChatPackets.BuildChat(Sender, (byte)ChatType.FriendSystem, FriendMessages.DenialList(book.Denials.Select(d => d.Name))));

    private static void SendStatus(Book reader, string name, bool online) => reader.Client.Connection.Send(
        GameChatPackets.BuildChat(Sender, (byte)ChatType.FriendSystem, FriendMessages.Status(name, online)));

    private static void Say(Book book, string text) =>
        book.Client.Connection.Send(GameChatPackets.BuildChat(Sender, (byte)ChatType.Friend, text));

    private static string Named(int stringId, string key, string name) => FriendMessages.Named(stringId, key, name);

    /// <summary><c>GameRule::IsValidName(…, 4, 18)</c>: 4 to 18 letters or digits.</summary>
    public static bool IsValidName(string name) =>
        name is { Length: >= NameMinLength and <= NameMaxLength } && name.All(char.IsLetterOrDigit);
}

public enum FriendStatus
{
    Offline = 0,
    Online = 1,

    /// <summary>The friend is online but blocks the reader, who is shown them as offline.</summary>
    Blocking = 2
}

/// <summary>The <c>@FRIEND</c> lines, byte for byte the official server's.</summary>
public static class FriendMessages
{
    public static string FriendList(IEnumerable<(string Name, FriendStatus Status)> friends)
    {
        var text = new StringBuilder("FLIST|");
        foreach (var (name, status) in friends)
        {
            text.Append(name).Append('|').Append((int)status).Append('|');
        }

        return text.ToString();
    }

    public static string DenialList(IEnumerable<string> names)
    {
        var text = new StringBuilder("DLIST|");
        foreach (var name in names)
        {
            text.Append(name).Append('|');
        }

        return text.ToString();
    }

    public static string Status(string name, bool online) => $"FSTATUS|{name}|{(online ? 1 : 0)}|";

    /// <summary>A client string with one parameter: <c>@467\v#@friend_name@#\vName</c>.</summary>
    public static string Named(int stringId, string key, string name) => $"@{stringId}\v#@{key}@#\v{name}";
}
