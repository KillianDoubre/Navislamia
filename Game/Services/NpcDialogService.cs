using System;
using System.Collections.Frozen;
using System.Collections.Generic;
using Microsoft.Extensions.Options;
using Navislamia.Configuration.Options;
using Navislamia.Game.Network.Clients;
using Navislamia.Game.Network.Packets.Game;
using Navislamia.Game.Services.Interfaces;
using Navislamia.Game.Services.Props;
using Serilog;

namespace Navislamia.Game.Services;

public class NpcDialogService : INpcDialogService
{
    private const int MaxTriggerLength = 1024;
    private readonly ILogger _logger = Log.ForContext<NpcDialogService>();
    private readonly FrozenDictionary<int, string> _contacts;
    private readonly FrozenDictionary<string, CompiledDialog> _dialogs;
    private readonly IWarpService _warpService;

    public NpcDialogService(IOptions<NpcDialogOptions> options, IWarpService warpService)
    {
        _warpService = warpService;
        _contacts = CompileContacts(options.Value.Npcs);
        _dialogs = CompileDialogs(options.Value.Dialogs);
        _logger.Information("Loaded {npcCount} NPC dialog links and {dialogCount} dialog definitions",
            _contacts.Count, _dialogs.Count);
    }

    public void Contact(GameClient client, byte[] packet)
    {
        if (!GameNpcDialogPackets.TryReadContact(packet, out var handle))
        {
            _logger.Warning("Malformed NPC contact from {clientTag}", client.ClientTag);
            return;
        }

        long npcId;
        var info = client.ConnectionInfo;
        lock (info.NpcVisibilityLock)
        {
            if (!info.SpawnedNpcIdsByHandle.TryGetValue(handle, out npcId))
            {
                _logger.Warning("Rejected contact with unknown NPC handle {handle} from {clientTag}", handle,
                    client.ClientTag);
                return;
            }
        }

        if (!_contacts.TryGetValue((int)npcId, out var function))
        {
            _logger.Debug("NPC {npcId} has no renderable Epic 7.3 contact dialog", npcId);
            return;
        }

        if (!TryShow(client, handle, function))
        {
            _logger.Debug("NPC {npcId} contact {function} has no renderable Epic 7.3 dialog", npcId, function);
        }
    }

    public void Select(GameClient client, byte[] packet)
    {
        if (!GameNpcDialogPackets.TryReadSelection(packet, MaxTriggerLength, out var trigger))
        {
            _logger.Warning("Malformed NPC dialog selection from {clientTag}", client.ClientTag);
            return;
        }

        var info = client.ConnectionInfo;
        if (trigger.Length == 0)
        {
            lock (info.NpcVisibilityLock)
            {
                info.ClearNpcDialog();
            }
            return;
        }

        uint npcHandle;
        lock (info.NpcVisibilityLock)
        {
            if (info.NpcDialogHandle == 0 || !info.NpcDialogTriggers.Contains(trigger))
            {
                _logger.Warning("Rejected unexpected NPC dialog trigger from {clientTag}", client.ClientTag);
                return;
            }

            npcHandle = info.NpcDialogHandle;
        }

        // A teleport trigger carries its destination in the trigger itself, so it is resolved rather
        // than looked up as a follow-up dialog page. The guard above already proved the current
        // dialog advertised it.
        var action = PropScript.Parse(trigger);
        if (action.Kind == PropActionKind.RunTeleport)
        {
            lock (info.NpcVisibilityLock)
            {
                info.ClearNpcDialog();
            }

            _warpService.Warp(client, action.X, action.Y);
            return;
        }

        var function = ReadFunctionName(trigger);
        if (!TryShow(client, npcHandle, function))
        {
            lock (info.NpcVisibilityLock)
            {
                info.ClearNpcDialog();
            }
            _logger.Debug("NPC dialog action {function} is not implemented yet", function);
        }
    }

    private bool TryShow(GameClient client, uint npcHandle, string function)
    {
        if (string.IsNullOrEmpty(function) || !_dialogs.TryGetValue(function, out var dialog))
        {
            return false;
        }

        var info = client.ConnectionInfo;
        lock (info.NpcVisibilityLock)
        {
            if (!info.SpawnedNpcIdsByHandle.ContainsKey(npcHandle))
            {
                return false;
            }

            info.NpcDialogHandle = npcHandle;
            info.NpcDialogTriggers.Clear();
            foreach (var trigger in dialog.Triggers)
            {
                info.NpcDialogTriggers.Add(trigger);
            }

            client.Connection.Send(GameNpcDialogPackets.CopyWithNpcHandle(dialog.PacketTemplate, npcHandle));
        }
        return true;
    }

    private static FrozenDictionary<int, string> CompileContacts(Dictionary<int, string> contacts)
    {
        var compiled = new Dictionary<int, string>(contacts.Count);
        foreach (var (npcId, expression) in contacts)
        {
            var function = ReadFunctionName(expression);
            if (function.Length > 0)
            {
                compiled[npcId] = function;
            }
        }

        return compiled.ToFrozenDictionary();
    }

    private static FrozenDictionary<string, CompiledDialog> CompileDialogs(
        Dictionary<string, NpcDialogDefinition> dialogs)
    {
        var compiled = new Dictionary<string, CompiledDialog>(dialogs.Count, StringComparer.Ordinal);
        foreach (var (function, dialog) in dialogs)
        {
            var menu = new List<NpcDialogMenuEntry>(dialog.Menu.Count);
            var triggers = new HashSet<string>(StringComparer.Ordinal);
            foreach (var entry in dialog.Menu)
            {
                if (entry.Label.Contains('\t') || entry.Trigger.Contains('\t'))
                {
                    continue;
                }

                menu.Add(entry);
                if (entry.Trigger.Length > 0)
                {
                    triggers.Add(entry.Trigger);
                }
            }

            var packet = GameNpcDialogPackets.BuildDialog(0, dialog.Title, dialog.Text, menu);
            var triggerArray = new string[triggers.Count];
            triggers.CopyTo(triggerArray);
            compiled[function] = new CompiledDialog(packet, triggerArray);
        }

        return compiled.ToFrozenDictionary(StringComparer.Ordinal);
    }

    private static string ReadFunctionName(string expression)
    {
        var value = expression.AsSpan().Trim();
        if (value.Length == 0 || (!char.IsAsciiLetter(value[0]) && value[0] != '_'))
        {
            return string.Empty;
        }

        var length = 1;
        while (length < value.Length &&
               (char.IsAsciiLetterOrDigit(value[length]) || value[length] == '_'))
        {
            length++;
        }

        return value.Slice(0, length).ToString();
    }

    private sealed record CompiledDialog(byte[] PacketTemplate, string[] Triggers);
}
