using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Navislamia.Game.DataAccess.Contexts;
using Navislamia.Game.DataAccess.Entities.Enums;
using Navislamia.Game.DataAccess.Entities.Telecaster;
using Navislamia.Game.Network.Clients;
using Navislamia.Game.Network.Packets;
using Navislamia.Game.Network.Packets.Game;

namespace Navislamia.Game.Services;

/// <summary>The donation debit and its ranking credit share one database transaction and character gate.</summary>
public sealed class DonationStore
{
    private readonly DbContextOptions<TelecasterContext> _options;
    private readonly CharacterGate _gate;
    private readonly IItemSellCatalog _catalog;
    private readonly TimeProvider _time;
    public DonationStore(DbContextOptions<TelecasterContext> options, CharacterGate gate,
        IItemSellCatalog catalog, TimeProvider time = null)
    { _options = TelecasterOptions.WithoutRetry(options); _gate = gate; _catalog = catalog; _time = time ?? TimeProvider.System; }

    public async Task<ResultCode> DonateAsync(GameClient client, GameActionPackets.DonateItemRequest request)
    {
        var info = client.ConnectionInfo;
        var name = info.CharacterName; var handle = info.CharacterHandle;
        return await _gate.RunAsync(name, async () =>
        {
            var shape = DonateRules.CheckShape(request);
            if (shape != ResultCode.Success) return shape;
            if (handle == 0 || info.CharacterHandle != handle || info.CharacterName != name) return ResultCode.NotExist;
            await using var db = new TelecasterContext(_options);
            await using var tx = db.Database.IsRelational() ? await db.Database.BeginTransactionAsync() : null;
            var character = await db.Characters.SingleOrDefaultAsync(c => c.Id == handle && c.CharacterName == name);
            if (character is null) return ResultCode.NotExist;
            var entries = request.Items ?? Array.Empty<GameActionPackets.DonateItemEntry>();
            var owned = await db.Items.Where(i => i.CharacterId == character.Id && i.AccountId == null
                && i.AuctionId == null && i.StorageId == null).ToArrayAsync();
            var stacks = new List<(ItemEntity Item, GameActionPackets.DonateItemEntry Entry)>();
            var reward = ItemDonateService.MoralPoints(request.Gold);
            foreach (var entry in entries)
            {
                var item = owned.SingleOrDefault(i => i.Id == entry.Handle);
                if (item is null || item.Amount < entry.Count || item.WearInfo != ItemWearType.None
                    || item.EquippedBySummonId != null || (character.BeltItemIds ?? Array.Empty<long>()).Contains(item.Id)
                    || (character.SummonSlotItemIds ?? Array.Empty<long>()).Contains(item.Id)) return ResultCode.NotExist;
                if (_catalog.TryGetTemplate((int)item.ItemResourceId, out var template))
                    reward += ItemDonateService.MoralPoints((long)template.Price) * entry.Count;
                stacks.Add((item, entry));
            }
            long jpBefore; decimal immoralBefore;
            lock (info.ProgressLock)
            {
                jpBefore = info.CharacterJp; immoralBefore = info.ImmoralPoint;
                if (request.Jp > jpBefore) return ResultCode.NotEnoughJP;
                lock (info.GoldLock)
                {
                    if (!info.TryDebitGold(request.Gold)) return ResultCode.NotEnoughMoney;
                }
                info.CharacterJp -= request.Jp;
                info.ImmoralPoint = MoralityRules.Normalize(immoralBefore - reward);
                reward = immoralBefore - info.ImmoralPoint;
                character.Gold = info.CharacterGold; character.Jp = info.CharacterJp;
                character.ImmoralPoint = info.ImmoralPoint;
            }
            var committed = false;
            try
            {
                foreach (var (item, entry) in stacks)
                { item.Amount -= entry.Count; if (item.Amount == 0) db.Items.Remove(item); }
                if (reward > 0)
                {
                    var period = DonationRankingService.Period(_time.GetUtcNow());
                    var score = await db.DonationScores.SingleOrDefaultAsync(r => r.CharacterId == handle && r.Period == period);
                    if (score is null) { score = new DonationScoreEntity { CharacterId = handle, Period = period }; db.DonationScores.Add(score); }
                    score.Score = Math.Min(99999999999999.9999m, score.Score + reward);
                    if (character.GuildId is > 0)
                    {
                        // GuildManager takes an int, truncating this donation's fractional moral points.
                        var points = (int)Math.Min(int.MaxValue, decimal.Truncate(reward));
                        if (db.Database.IsRelational())
                            await db.Guilds.Where(g => g.Id == character.GuildId).ExecuteUpdateAsync(update => update
                                .SetProperty(g => g.DonationPoint, g => (long)g.DonationPoint + points > int.MaxValue
                                    ? int.MaxValue : g.DonationPoint + points));
                        else
                        {
                            var guild = await db.Guilds.SingleOrDefaultAsync(g => g.Id == character.GuildId);
                            if (guild is not null) guild.DonationPoint = (int)Math.Min(int.MaxValue, (long)guild.DonationPoint + points);
                        }
                    }
                }
                await db.SaveChangesAsync();
                if (tx is not null) await tx.CommitAsync();
                committed = true;
            }
            finally
            {
                if (!committed) lock (info.ProgressLock)
                { info.AddGold(request.Gold); info.CharacterJp += request.Jp; info.ImmoralPoint += reward; }
            }
            // Session still belongs to the donor; the durable change remains valid if it left meanwhile.
            if (info.CharacterHandle == handle && info.CharacterName == name)
            {
                foreach (var (item, entry) in stacks) client.Connection.Send(item.Amount == 0
                    ? GameCharacterPackets.BuildDestroyItem(entry.Handle)
                    : GameCharacterPackets.BuildUpdateItemCount(entry.Handle, item.Amount));
                client.Connection.Send(GameCharacterPackets.BuildGoldUpdate(info.CharacterGold, info.CharacterChaos));
                client.Connection.Send(GameCharacterPackets.BuildExpUpdate(handle, info.CharacterExp, info.CharacterJp));
                MoralityRules.Set(client, info.ImmoralPoint);
                client.SendActorStatus();
            }
            return ResultCode.Success;
        });
    }
}
