using System;
using System.Linq;
using System.Collections.Generic;
using System.Threading.Tasks;
using Navislamia.Game.Network.Clients;
using Navislamia.Game.Network.Packets;
using Navislamia.Game.Network.Packets.Enums;
using Navislamia.Game.Network.Packets.Game;
using Navislamia.Game.Services.Stats;

namespace Navislamia.Game.Services.Creatures;

public sealed partial class CreatureService
{
    public int NameIdOf(CreatureCard card) => _catalog.TryGetSummon(card.SummonCode, out var row) ? row.NameId : 0;

    public bool ShowNameChange(GameClient client, uint summonOrSlot)
    {
        var info = client.ConnectionInfo;
        lock (info.SummonLock)
        {
            if (summonOrSlot < CreatureRules.MaxSlots && summonOrSlot >= info.SummonSlots.Length) return false;
            var card = summonOrSlot < CreatureRules.MaxSlots
                ? info.CreatureCards.GetValueOrDefault(info.SummonSlots[summonOrSlot])
                : info.CreatureCards.Values.FirstOrDefault(c => c.SummonHandle == summonOrSlot);
            if (card is not { HasSummon: true, Amount: > 0 } || card.SummonHandle == 0
                || !_catalog.TryGetSummon(card.SummonCode, out var row) || row.RidingKind == 2) return false;
            info.SummonNameChangeTarget = card.SummonHandle;
            client.Connection.Send(GameSmallPackets.ShowSummonNameChange(card.SummonHandle));
            return true;
        }
    }

    public Task ChangeNameAsync(GameClient client, string name)
    {
        var info = client.ConnectionInfo;
        lock (info.NameChangeLock)
        {
            uint target;
            lock (info.SummonLock)
            {
                target = info.SummonNameChangeTarget;
                info.SummonNameChangeTarget = 0; // Every reply consumes the window, including a refused name.
            }
            if (target == 0) return Task.CompletedTask;
            return info.NameChangeCompletion = RenameAndPublishAsync(client, target, name, info.NameChangeCompletion);
        }
    }

    private async Task RenameAndPublishAsync(GameClient client, uint target, string name, Task previous)
    {
        await previous;
        var info = client.ConnectionInfo;
        var character = info.CharacterName; var handle = info.CharacterHandle;
        if (handle == 0) return;
        CreatureCard card;
        lock (info.SummonLock)
            card = info.CreatureCards.Values.FirstOrDefault(c => c.SummonHandle == target && c.HasSummon && c.Amount > 0);
        if (card is null) return;
        if (name.Length < 4) { NameNotice(client, "@117"); return; }
        if (name == card.SummonName) { NameNotice(client, "@118"); return; }
        if (!CharacterNameRules.Valid(name) || string.Equals(name, card.SummonName, StringComparison.OrdinalIgnoreCase))
        { NameNotice(client, "@17"); return; }
        var cost = checked((long)card.Level * 1000);
        if (!info.TryDebitGold(cost)) { NameNotice(client, "@129"); return; }
        ResultCode result;
        try { result = await _characters.RenameSummonAsync(character, card.SummonId, name, info.CharacterGold); }
        catch (Exception exception)
        {
            _logger.Error(exception, "Could not rename the summon of {Character}", character);
            result = ResultCode.DBError;
        }
        if (result != ResultCode.Success)
        {
            info.AddGold(cost);
            NameNotice(client, result == ResultCode.InvalidText ? "@17" : "@129");
            return;
        }
        if (info.CharacterName != character || info.CharacterHandle != handle) return;
        lock (info.SummonLock) card.SummonName = name;
        var presence = Array.Find(info.Summons, s => s.Handle == target);
        if (presence is not null) presence.Entry.Name = name;
        client.Connection.Send(GameCharacterPackets.BuildGoldUpdate(info.CharacterGold, info.CharacterChaos));
        client.Connection.Send(GameStatPackets.BuildStringProperty(target, "name", name));
        var frame = GamePetPackets.BuildChangeName(target, name);
        client.Connection.Send(frame);
        if (presence is not null)
            foreach (var observer in _players?.Observers(client) ?? Array.Empty<GameClient>()) observer.Connection.Send(frame);
    }

    private static void NameNotice(GameClient client, string text) => client.Connection.Send(
        GameChatPackets.BuildChat("@SYSTEM", (byte)ChatType.Summon, text));

    private static void SendSp(GameClient client, CreatureCard card, StatBlock stats)
    {
        card.MaxSp = Math.Clamp((int)stats.MaxSp, 0, short.MaxValue);
        card.Sp = Math.Clamp(card.Sp, 0, card.MaxSp);
        client.Connection.Send(GameSmallPackets.Sp(card.SummonHandle, card.Sp, card.MaxSp));
    }

    public void SyncSp(GameClient client, uint summonHandle, int maxSp)
    {
        lock (client.ConnectionInfo.SummonLock)
        {
            var card = client.ConnectionInfo.CreatureCards.Values.FirstOrDefault(c => c.SummonHandle == summonHandle);
            if (card is not null) SendSp(client, card, new StatBlock { MaxSp = maxSp });
        }
    }

    public void SetSp(GameClient client, CreatureCard card, int sp)
    {
        lock (client.ConnectionInfo.SummonLock)
        {
            if (!client.ConnectionInfo.CreatureCards.TryGetValue(card.ItemId, out var owned) || owned != card) return;
            var previous = card.Sp;
            card.Sp = Math.Clamp(sp, 0, card.MaxSp);
            if (previous != card.Sp)
                client.Connection.Send(GameSmallPackets.Sp(card.SummonHandle, card.Sp, card.MaxSp));
        }
    }
}
