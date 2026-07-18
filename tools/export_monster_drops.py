"""Regenerate DevConsole/monster-drops.73.json from the 9.4 SQL Server.

The previous catalog filtered drop_item_id > 0 and threw away every negative entry. Negative
drop_item_id values are NOT junk: they are references to DropGroupResource (keyed by the negative id),
a weighted "pick exactly one item" group. Roughly 91% of all monster drop entries are group references
(56,584 group vs 5,800 direct), so discarding them is why monsters almost never dropped anything.

This writes three sections:
  Tables:  monster/table id -> entries [{ItemId, Chance, MinCount, MaxCount}], ItemId < 0 = group ref
  Groups:  group id (negative) -> entries [{ItemId, Weight, MinCount, MaxCount}], weights sum to ~1
  Monsters: monster id -> table id (MonsterResource.drop_table_link_id)

The runtime rolls each table entry's Chance; when it fires and the entry is a group ref, it resolves
the group to one concrete item by weighted selection, recursing while the pick is itself negative.
"""

import json
import os
import subprocess
from datetime import datetime, timezone

SERVER = r"localhost\SQLEXPRESS"
DATABASE = "Arcadia"
OUTPUT = os.path.join(os.path.dirname(__file__), "..", "DevConsole", "monster-drops.73.json")
SLOTS = 10


def query(sql):
    result = subprocess.run(
        ["sqlcmd", "-S", SERVER, "-E", "-C", "-d", DATABASE, "-h", "-1", "-W", "-s", "\t", "-Q",
         "SET NOCOUNT ON; " + sql],
        capture_output=True, text=True, encoding="utf-8", errors="replace")
    if result.returncode != 0:
        raise RuntimeError(f"sqlcmd failed: {result.stderr}")
    rows = []
    for line in result.stdout.splitlines():
        if "\t" in line:
            rows.append(line.split("\t"))
    return rows


def slot_columns(prefix_min_max):
    cols = []
    for i in range(SLOTS):
        s = f"{i:02d}"
        cols.append(f"drop_item_id_{s}")
        cols.append(f"drop_percentage_{s}")
        cols.append(f"drop_min_count_{s}")
        cols.append(f"drop_max_count_{s}")
    return ", ".join(cols)


def parse_slots(cells, start):
    """Yield (itemId, value, minCount, maxCount) for each non-empty slot."""
    for i in range(SLOTS):
        base = start + i * 4
        item = int(float(cells[base]))
        value = float(cells[base + 1])
        if item == 0 or value <= 0:
            continue
        lo = max(1, int(float(cells[base + 2])))
        hi = max(lo, int(float(cells[base + 3])))
        yield item, value, lo, hi


def build_tables():
    """Monster/table id -> list of entries, aggregating every sub_id row and all ten slots."""
    rows = query(f"SELECT id, {slot_columns(True)} FROM MonsterDropTableResource ORDER BY id, sub_id;")
    tables = {}
    for cells in rows:
        table_id = int(cells[0])
        entries = tables.setdefault(table_id, [])
        for item, chance, lo, hi in parse_slots(cells, 1):
            entries.append({"ItemId": item, "Chance": round(chance, 8),
                            "MinCount": lo, "MaxCount": hi})
    return {tid: e for tid, e in tables.items() if e}


def build_groups():
    """Group id (negative) -> list of weighted items."""
    rows = query(f"SELECT id, {slot_columns(True)} FROM DropGroupResource ORDER BY id;")
    groups = {}
    for cells in rows:
        group_id = int(cells[0])
        entries = groups.setdefault(group_id, [])
        for item, weight, lo, hi in parse_slots(cells, 1):
            entries.append({"ItemId": item, "Weight": round(weight, 8),
                            "MinCount": lo, "MaxCount": hi})
    return {gid: e for gid, e in groups.items() if e}


def build_monsters():
    rows = query("SELECT id, drop_table_link_id FROM MonsterResource WHERE drop_table_link_id <> 0;")
    return {int(r[0]): int(r[1]) for r in rows}


def used_group_ids(tables, groups):
    """Every group id reachable from a table entry, following nested group refs."""
    used = set()
    stack = [e["ItemId"] for entries in tables.values() for e in entries if e["ItemId"] < 0]
    while stack:
        gid = stack.pop()
        if gid in used:
            continue
        used.add(gid)
        for e in groups.get(gid, []):
            if e["ItemId"] < 0:
                stack.append(e["ItemId"])
    return used


def main():
    tables = build_tables()
    groups = build_groups()
    monsters = build_monsters()

    used = used_group_ids(tables, groups)
    missing = sorted(g for g in used if g not in groups)

    linked = {m: t for m, t in monsters.items() if t in tables}
    direct = sum(1 for e in tables.values() for x in e if x["ItemId"] > 0)
    group_refs = sum(1 for e in tables.values() for x in e if x["ItemId"] < 0)

    print(f"tables: {len(tables)}, direct entries: {direct}, group-ref entries: {group_refs}")
    print(f"groups: {len(groups)}, used by tables (incl. nested): {len(used)}, missing: {len(missing)}")
    print(f"monsters linked to a real table: {len(linked)} of {len(monsters)}")
    if missing:
        print(f"  missing group ids (a ref to one is simply skipped at runtime): {missing[:10]}")

    catalog = {
        "Metadata": {
            "GeneratedAt": datetime.now(timezone.utc).isoformat(),
            "Source": "Rappelz 9.4 SQL Server Arcadia: MonsterDropTableResource + DropGroupResource "
                      "+ MonsterResource.drop_table_link_id",
            "Tables": len(tables),
            "DirectEntries": direct,
            "GroupRefEntries": group_refs,
            "Groups": len(groups),
            "MonstersLinked": len(linked),
            "ChanceScale": "table Chance and group Weight are probabilities in [0,1]; "
                           "a negative ItemId is a DropGroupResource reference resolved by weighted pick",
            "Epic73ItemFilter": "not applied: client db_item.rdb unavailable",
        },
        "MonsterDropCatalog": {
            "Tables": {str(k): v for k, v in sorted(tables.items())},
            "Groups": {str(k): v for k, v in sorted(groups.items())},
            "Monsters": {str(k): v for k, v in sorted(monsters.items())},
        },
    }

    path = os.path.abspath(OUTPUT)
    with open(path, "w", encoding="utf-8") as stream:
        json.dump(catalog, stream, separators=(",", ":"))
    print(f"wrote {path}")


if __name__ == "__main__":
    main()
