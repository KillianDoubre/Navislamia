using System;
using System.Collections.Frozen;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Extensions.Options;
using Navislamia.Configuration.Options;
using Serilog;

namespace Navislamia.Game.Services;

public readonly record struct DropEntry(int ItemId, double Chance, int MinCount, int MaxCount);

public readonly record struct DroppedItem(int ItemId, long Count);

public class MonsterDropCatalog : IMonsterDropCatalog
{
    private static readonly DropEntry[] NoDrops = Array.Empty<DropEntry>();

    private readonly ILogger _logger = Log.ForContext<MonsterDropCatalog>();
    private readonly FrozenDictionary<int, DropEntry[]> _byMonster;

    public MonsterDropCatalog(IOptions<MonsterDropOptions> options) : this(options.Value)
    {
    }

    public MonsterDropCatalog(MonsterDropOptions options)
    {
        var tables = options.Tables.ToDictionary(
            table => table.Key,
            table => table.Value
                .Where(entry => entry.ItemId > 0 && entry.Chance > 0)
                .Select(entry => new DropEntry(entry.ItemId, entry.Chance,
                    Math.Max(1, entry.MinCount), Math.Max(1, Math.Max(entry.MinCount, entry.MaxCount))))
                .ToArray());

        _byMonster = options.Monsters
            .Where(link => tables.ContainsKey(link.Value) && tables[link.Value].Length > 0)
            .ToFrozenDictionary(link => link.Key, link => tables[link.Value]);

        _logger.Debug("Loaded drop tables for {count} monsters", _byMonster.Count);
    }

    public int MonsterCount => _byMonster.Count;

    public IReadOnlyList<DropEntry> GetDrops(int monsterId)
    {
        return _byMonster.TryGetValue(monsterId, out var entries) ? entries : NoDrops;
    }
}
