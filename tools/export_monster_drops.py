"""Regenerate DevConsole/monster-drops.73.json from a CSV export of the resource tables.

The source is the Epic 7 tables by default (data/epic7, written by tools/rdu.py from the Epic 7 Part 4 dump),
the data closest to the 7.3 client; data/sqlserver/Arcadia (the 9.4 export) reads the same way.

Negative drop_item_id values are NOT junk: they are references to DropGroupResource (keyed by the negative
id), a weighted "pick exactly one item" group, and they are where most drops live. Discarding them is why
monsters once almost never dropped anything.

This writes three sections:
  Tables:  monster/table id -> entries [{ItemId, Chance, MinCount, MaxCount}], ItemId < 0 = group ref
  Groups:  group id (negative) -> entries [{ItemId, Weight, MinCount, MaxCount}], weights sum to ~1
  Monsters: monster id -> table id (MonsterResource.drop_table_link_id)

With --client-items (the 7.3 client's db_item.rdb), an item the client does not know — it would render
nothing on the ground — is filtered: a table slot holding one is removed, and a group member holding one
becomes ItemId 0, a blank share, so that its weight still drops nothing instead of being handed to the
other members (MonsterDropCatalog keeps it, DropRoll resolves it to nothing).

drop_min_level_NN / drop_max_level_NN (a slot restricted to a killer level range) are not exported: the
runtime has no field for them yet.

Usage:
    python tools/export_monster_drops.py [--source data/epic7] [--client-items db_item.rdb]
"""

import csv
import json
import pathlib
import struct
import sys
from datetime import datetime, timezone

ROOT = pathlib.Path(__file__).resolve().parent.parent
OUTPUT = ROOT / "DevConsole" / "monster-drops.73.json"
SLOTS = 10


def client_items(path):
    """Item ids of the client's db_item.rdb: a 128-byte header, a u32 count, fixed-size records."""
    data = pathlib.Path(path).read_bytes()
    count = struct.unpack_from("<I", data, 128)[0]
    stride, rest = divmod(len(data) - 132, count)
    if rest:
        sys.exit(f"{path}: {len(data)} bytes do not split into {count} records")
    return {struct.unpack_from("<i", data, 132 + i * stride)[0] for i in range(count)}


def rows(source, table):
    with open(source / f"{table}.csv", encoding="utf-8") as stream:
        yield from csv.DictReader(stream)


def number(value):
    return float(value) if value else 0.0


def slots(row):
    """(itemId, value, minCount, maxCount) for each non-empty slot of a drop row."""
    for i in range(SLOTS):
        s = f"{i:02d}"
        item = int(number(row[f"drop_item_id_{s}"]))
        value = number(row[f"drop_percentage_{s}"])
        if item == 0 or value <= 0:
            continue
        lo = max(1, int(number(row[f"drop_min_count_{s}"])))
        hi = max(lo, int(number(row[f"drop_max_count_{s}"])))
        yield item, value, lo, hi


def build(source, allowed, stats):
    # A monster's table is every row with its id across sub_ids, ten slots each.
    tables = {}
    for row in sorted(rows(source, "MonsterDropTableResource"), key=lambda r: (int(r["id"]), int(r["sub_id"]))):
        entries = tables.setdefault(int(row["id"]), [])
        for item, chance, lo, hi in slots(row):
            if item > 0 and allowed is not None and item not in allowed:
                stats["table slots not in client"] += 1
                continue
            entries.append({"ItemId": item, "Chance": round(chance, 8), "MinCount": lo, "MaxCount": hi})

    groups = {}
    for row in sorted(rows(source, "DropGroupResource"), key=lambda r: int(r["id"])):
        entries = groups.setdefault(int(row["id"]), [])
        for item, weight, lo, hi in slots(row):
            if item > 0 and allowed is not None and item not in allowed:
                stats["group members blanked (not in client)"] += 1
                item = 0
            entries.append({"ItemId": item, "Weight": round(weight, 8), "MinCount": lo, "MaxCount": hi})

    monsters = {}
    for row in rows(source, "MonsterResource"):
        link = int(number(row["drop_table_link_id"]))
        if link:
            monsters[int(row["id"])] = link

    tables = {k: v for k, v in tables.items() if v}
    groups = {k: v for k, v in groups.items() if v}
    return tables, groups, monsters


def used_group_ids(tables, groups):
    """Every group id reachable from a table entry, following nested group refs."""
    used = set()
    stack = [e["ItemId"] for entries in tables.values() for e in entries if e["ItemId"] < 0]
    while stack:
        gid = stack.pop()
        if gid in used:
            continue
        used.add(gid)
        stack.extend(e["ItemId"] for e in groups.get(gid, []) if e["ItemId"] < 0)
    return used


def main(argv):
    source = pathlib.Path(argv[argv.index("--source") + 1]) if "--source" in argv else ROOT / "data" / "epic7"
    if not source.is_absolute():
        source = ROOT / source
    allowed = client_items(argv[argv.index("--client-items") + 1]) if "--client-items" in argv else None
    stats = {"table slots not in client": 0, "group members blanked (not in client)": 0}

    tables, groups, monsters = build(source, allowed, stats)
    used = used_group_ids(tables, groups)
    missing = sorted(g for g in used if g not in groups)
    linked = {m: t for m, t in monsters.items() if t in tables}
    direct = sum(1 for e in tables.values() for x in e if x["ItemId"] > 0)
    group_refs = sum(1 for e in tables.values() for x in e if x["ItemId"] < 0)

    print(f"source: {source}")
    print(f"tables: {len(tables)}, direct entries: {direct}, group-ref entries: {group_refs}")
    print(f"groups: {len(groups)}, used by tables (incl. nested): {len(used)}, missing: {len(missing)}")
    print(f"monsters linked to a real table: {len(linked)} of {len(monsters)}")
    for key, value in stats.items():
        print(f"{key}: {value}")

    epic7 = source.name == "epic7"
    catalog = {
        "Metadata": {
            "GeneratedAt": datetime.now(timezone.utc).isoformat(),
            "Source": ("Epic 7 Part 4 resource tables (tools/rdu.py)" if epic7 else f"CSV export {source.name}")
                      + ": MonsterDropTableResource + DropGroupResource + MonsterResource.drop_table_link_id",
            "Tables": len(tables),
            "DirectEntries": direct,
            "GroupRefEntries": group_refs,
            "Groups": len(groups),
            "MonstersLinked": len(linked),
            "ChanceScale": "table Chance and group Weight are probabilities in [0,1]; "
                           "a negative ItemId is a DropGroupResource reference resolved by weighted pick",
            "Epic73ItemFilter": ("client db_item.rdb: " + ", ".join(f"{k} {v}" for k, v in stats.items())
                                 if allowed is not None else "not applied"),
        },
        "MonsterDropCatalog": {
            "Tables": {str(k): v for k, v in sorted(tables.items())},
            "Groups": {str(k): v for k, v in sorted(groups.items())},
            "Monsters": {str(k): v for k, v in sorted(monsters.items())},
        },
    }

    with open(OUTPUT, "w", encoding="utf-8") as stream:
        json.dump(catalog, stream, separators=(",", ":"))
    print(f"wrote {OUTPUT}")


if __name__ == "__main__":
    main(sys.argv)
