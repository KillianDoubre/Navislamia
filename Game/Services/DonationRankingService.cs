using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Navislamia.Game.DataAccess.Contexts;
using Navislamia.Game.Network.Clients;
using Navislamia.Game.Network.Packets.Game;

namespace Navislamia.Game.Services;

public interface IDonationRankingService
{
    Task<byte[]> GetAsync(long characterId, sbyte type);
}

/// <summary>RankingManager: type 0, top ten at >=100 moral points; c_fixed10 scores on the wire.</summary>
public sealed class DonationRankingService : IDonationRankingService
{
    private readonly DbContextOptions<TelecasterContext> _options;
    private readonly TimeProvider _time;
    public DonationRankingService(DbContextOptions<TelecasterContext> options, TimeProvider time = null)
    { _options = options; _time = time ?? TimeProvider.System; }
    public static int Period(DateTimeOffset now)
    { var local = TimeZoneInfo.ConvertTime(now, TimeZoneInfo.Local); return local.Year * 100 + local.Month; }
    public async Task<byte[]> GetAsync(long characterId, sbyte type)
    {
        // 1 is the reward roster, not a score request. Hunta rankings require their own event ledger.
        if (type != 0) return null;
        await using var db = new TelecasterContext(_options);
        var period = Period(_time.GetUtcNow());
        var scores = from score in db.DonationScores.AsNoTracking()
            join character in db.Characters.AsNoTracking() on score.CharacterId equals character.Id
            where score.Period == period && score.Score > 0
            select new { score.CharacterId, score.Score, character.CharacterName };
        var requester = await scores.SingleOrDefaultAsync(s => s.CharacterId == characterId);
        var rank = requester is null ? 0 : await scores.CountAsync(s => s.Score > requester.Score
            || s.Score == requester.Score && s.CharacterId <= characterId);
        var rows = await scores.Where(s => s.Score >= 100m).OrderByDescending(s => s.Score).ThenBy(s => s.CharacterId)
            .Take(GameRankingPackets.MaxRecords).ToArrayAsync();
        var records = rows
            .Select((r, i) => new GameRankingPackets.RankingRecord((ushort)(i + 1), r.CharacterName,
                MoralityRules.WireValue(r.Score))).ToArray();
        return GameRankingPackets.BuildRankingTopRecord(type,
            requester is null ? ushort.MaxValue : (ushort)Math.Min(ushort.MaxValue, rank),
            requester is null ? 0 : MoralityRules.WireValue(requester.Score), records);
    }
}
