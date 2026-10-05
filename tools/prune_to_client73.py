"""Remove from Postgres Arcadia the resource rows the Epic 7.3 client does not know.

docs/packet-specs/filtre-ressources-73.md. The 7.3 set of a table is the client's own table when it has one
(db_item, db_skill, db_monster, db_npcresource, db_creature, db_quest: tools/client73_ids.py), and the Epic 7 dump
(data/epic7) for the tables the client has none for (StateResource, StatResource). A row outside that set is
removed unless something still points at it:

  * a kept row of another table (foreign keys and plain id columns: an item's skill, state and summon, a quest's
    rewards and targets, a summon's card, skills and stats, an NPC's equipment and stats, a job's or monster's
    stats, an item effect "skill" (5) or "add state" (6));
  * Telecaster (an item a character holds, a learned skill, a saved state, a summon, a quest in progress or done,
    a paid item, a starter item);
  * typed catalogue references and reachable Lua entry points (resource_reachability.py);
  * StringResources: exactly the ids declared by the client db_string.rdb (no outside exemption).

What stays outside the 7.3 set for one of these reasons is listed, so it can be cleaned at its source. The rows of
QuestLinkResources whose NPC or quest is removed go with them. Everything happens in one transaction.

Usage:
    python tools/prune_to_client73.py <extracted client dir>            # the plan, nothing written
    python tools/prune_to_client73.py <extracted client dir> --apply    # pg_dump Arcadia first: no undo
The database password is read from DevConsole/appsettings.json (never printed).
"""
import csv
import json
import os
import pathlib
import re
import subprocess
import sys
import tempfile

sys.path.insert(0, str(pathlib.Path(__file__).resolve().parent))
import client73_ids  # noqa: E402
import resource_reachability

ROOT = pathlib.Path(__file__).resolve().parent.parent
PSQL = r"C:\Program Files\PostgreSQL\18\bin\psql.exe"
EPIC7 = pathlib.Path(os.environ.get("NAVIS_EPIC7", ROOT / "data" / "epic7"))

# ItemEffectInstant: 5 casts the skill var1, 6 adds the state var1 (ItemUseService.ApplyEffects).
EFFECT_SKILL, EFFECT_ADD_STATE = 5, 6

# JSON keys whose integer values name a row of the family (lower-cased key contains one of the words).
JSON_FAMILIES = {
    "ItemResources": ("item", "card", "cage", "reward", "material", "code"),
    "SkillResources": ("skill", "activate"),
    "StateResources": ("stateid", "states"),
    "StatResources": ("statid",),
    "MonsterResources": ("monster", "target"),
    "NpcResources": ("npc",),
    "SummonResources": ("summon", "creature"),
    "QuestResources": ("quest",),
}


def settings():
    config = json.loads((ROOT / "DevConsole" / "appsettings.json").read_text(encoding="utf-8-sig"))["Database"]
    return config


def query(database, sql):
    config = settings()
    result = subprocess.run([PSQL, "-h", config.get("DataSource", "localhost"), "-p", str(config.get("Port", 5432)),
                             "-U", config["User"], "-d", database, "-At", "-F", "\t", "-v", "ON_ERROR_STOP=1",
                             "-c", sql], capture_output=True, text=True,
                            env={**os.environ, "PGPASSWORD": config["Password"]})
    if result.returncode:
        raise RuntimeError(result.stderr)
    return [line.split("\t") for line in result.stdout.splitlines() if line]


def ints(value):
    """Every integer of a scalar or a Postgres array literal; 0 and NULL name nothing."""
    if value in (None, ""):
        return []
    return [int(float(v)) for v in re.findall(r"-?\d+(?:\.\d+)?", value) if int(float(v)) != 0]


def epic7_ids(name, column):
    with open(EPIC7 / f"{name}.csv", encoding="utf-8") as stream:
        return {int(float(row[column])) for row in csv.DictReader(stream)}


def json_references():
    found = {table: set() for table in JSON_FAMILIES}

    def walk(node, key=""):
        if isinstance(node, dict):
            for k, v in node.items():
                walk(v, k.lower())
        elif isinstance(node, list):
            for v in node:
                walk(v, key)
        elif isinstance(node, (int, float)) and not isinstance(node, bool) and node == int(node) and node != 0:
            for table, words in JSON_FAMILIES.items():
                if any(word in key for word in words):
                    found[table].add(int(node))

    files = [p for p in (ROOT / "DevConsole").glob("*.73.json")]
    files += [p for p in (ROOT / "Game").rglob("*.json") if "bin" not in p.parts and "obj" not in p.parts]
    for path in files:
        walk(json.loads(path.read_text(encoding="utf-8-sig")))
    return found


def literal_references(known_npcs, known_monsters, known_quests):
    handlers = [v for row in query("Arcadia", 'SELECT "EnterHandler", "LeaveHandler" FROM "EventAreaResources"') for v in row]
    quest_scripts = [v for row in query("Arcadia", 'SELECT "Id", "ScriptStartText", "ScriptEndText", "ScriptDropText" FROM "QuestResources"')
                     if int(row[0]) in known_quests for v in row[1:]]
    found = resource_reachability.references(ROOT, known_npcs, handlers, quest_scripts)[0]
    for family, ids in resource_reachability.monster_references(
            ROOT / "Game/Scripting/Scripts/monster_triggers.lua", known_monsters).items():
        found[family].update(ids)
    return found


def plan(client_dir):
    client = client73_ids.client_ids(client_dir)
    db = {}

    items = query("Arcadia", 'select "Id", "SkillId", "StateId", "SummonId", "BaseTypes", "BaseVar1", "OptTypes", '
                             '"OptVar1" from "ItemResources"')
    skills = query("Arcadia", 'select "Id", "StateId", "RequiredStateId", "SummonId", "UpgradeIntoSkillId" '
                              'from "SkillResources"')
    summons = query("Arcadia", 'select "Id", "CardId", "StatId", "SkillIds", "EvolveTargetId" from "SummonResources"')
    monsters = query("Arcadia", 'select "Id", "StatId" from "MonsterResources"')
    npc_items = [c for c in ("Weapon", "Shield", "Clothes", "Helm", "Gloves", "Boots", "Belt", "Mantle", "Necklace",
                             "Earring", "Ring1", "Ring2")]
    npcs = query("Arcadia", 'select "Id", "StatId", ' + ", ".join(f'"{c}ItemId"' for c in npc_items)
                 + ' from "NpcResources"')
    quest_columns = (["DefaultRewardId"] + [f"OptionalRewardId{i}" for i in range(1, 7)]
                     + [f"Value{i}" for i in range(1, 13)] + ["ForeQuest1", "ForeQuest2", "ForeQuest3"])
    quests = query("Arcadia", 'select "Id", ' + ", ".join(f'"{c}"' for c in quest_columns) + ' from "QuestResources"')
    db["ItemResources"] = {int(r[0]) for r in items}
    db["SkillResources"] = {int(r[0]) for r in skills}
    db["SummonResources"] = {int(r[0]) for r in summons}
    db["MonsterResources"] = {int(r[0]) for r in monsters}
    db["NpcResources"] = {int(r[0]) for r in npcs}
    db["QuestResources"] = {int(r[0]) for r in quests}
    db["StateResources"] = {int(r[0]) for r in query("Arcadia", 'select "Id" from "StateResources"')}
    db["StatResources"] = {int(r[0]) for r in query("Arcadia", 'select "Id" from "StatResources"')}

    seven = dict(client)
    seven["StateResources"] = epic7_ids("StateResource", "state_id")
    seven["StatResources"] = epic7_ids("StatResource", "id")

    # Outside references: Telecaster, catalogues, scripts and code.
    outside = {table: set() for table in db}
    reasons = {table: {} for table in db}

    def protect(table, ids, why):
        for i in ids:
            if i in db[table] and i not in seven[table]:
                outside[table].add(i)
                reasons[table].setdefault(i, why)

    tele = {
        "ItemResources": ('select distinct "ItemResourceId" from "Items" union select distinct "ItemCode" from "PaidItems" '
                          'union select distinct "ItemId" from "StarterItems"', "held in Telecaster"),
        "SkillResources": ('select distinct "SkillId" from "CharacterSkills" union select distinct "SkillId" from '
                           '"SummonSkills" union select distinct "SkillId" from "CharacterStates"', "learned or saved"),
        "StateResources": ('select distinct "StateId" from "CharacterStates"', "saved on a character"),
        "SummonResources": ('select distinct "SummonResourceId" from "Summons"', "a character's summon"),
        "QuestResources": ('select distinct "Code" from "CharacterQuests" union select distinct "Code" from '
                           '"CharacterQuestCompletions"', "a character's quest"),
    }
    for table, (sql, why) in tele.items():
        protect(table, {i for r in query("Telecaster", sql) for i in ints(r[0])}, why)
    for table, ids in json_references().items():
        protect(table, ids, "named by a catalogue")
    literals = literal_references(seven["NpcResources"], seven["MonsterResources"] | json_references()["MonsterResources"], seven["QuestResources"])
    for table, ids in literals.items():
        protect(table, ids, "reachable typed code or Lua reference")

    kept = {table: (db[table] & seven[table]) | outside[table] for table in db}

    # The closure: a kept row keeps what it points at.
    while True:
        before = sum(len(v) for v in kept.values())
        for r in items:
            if int(r[0]) not in kept["ItemResources"]:
                continue
            protect("SkillResources", ints(r[1]), "a kept item's skill")
            protect("StateResources", ints(r[2]), "a kept item's state")
            protect("SummonResources", ints(r[3]), "a kept item's summon")
            for types, vars_ in ((r[4], r[5]), (r[6], r[7])):
                for effect, var in zip([int(float(v)) for v in re.findall(r"-?\d+(?:\.\d+)?", types or "")],
                                         re.findall(r"-?\d+(?:\.\d+)?", vars_ or "")):
                    if effect == EFFECT_SKILL:
                        protect("SkillResources", ints(var), "a kept item's use effect")
                    elif effect == EFFECT_ADD_STATE:
                        protect("StateResources", ints(var), "a kept item's use effect")
        for r in skills:
            if int(r[0]) in kept["SkillResources"]:
                protect("StateResources", ints(r[1]) + ints(r[2]), "a kept skill's state")
                protect("SummonResources", ints(r[3]), "a kept skill's summon")
                protect("SkillResources", ints(r[4]), "a kept skill's upgrade")
        for r in summons:
            if int(r[0]) in kept["SummonResources"]:
                protect("ItemResources", ints(r[1]), "a kept summon's card")
                protect("StatResources", ints(r[2]), "a kept summon's stats")
                protect("SkillResources", ints(r[3]), "a kept summon's skills")
                protect("SummonResources", ints(r[4]), "a kept summon's evolution")
        for r in monsters:
            if int(r[0]) in kept["MonsterResources"]:
                protect("StatResources", ints(r[1]), "a kept monster's stats")
        for r in npcs:
            if int(r[0]) in kept["NpcResources"]:
                protect("StatResources", ints(r[1]), "a kept NPC's stats")
                protect("ItemResources", [i for v in r[2:] for i in ints(v)], "a kept NPC's equipment")
        for r in quests:
            if int(r[0]) in kept["QuestResources"]:
                values = [i for v in r[1:] for i in ints(v)]
                protect("ItemResources", values, "a kept quest's reward or target")
                protect("MonsterResources", values, "a kept quest's target")
                protect("QuestResources", values, "a kept quest's prerequisite")
        protect("StatResources", [i for r in query("Arcadia", 'select "StatId" from "JobResources"') for i in ints(r[0])],
                "a job's stats")
        protect("ItemResources", [i for r in query("Arcadia", 'select "RequiredItemId" from "EnhanceResources"')
                                  for i in ints(r[0])], "an enhancement's material")
        kept = {table: (db[table] & seven[table]) | outside[table] for table in db}
        if sum(len(v) for v in kept.values()) == before:
            break

    removed = {table: db[table] - kept[table] for table in db}
    # Strings are protocol/client vocabulary: retain exactly codes in the client's own db_string.
    db["StringResources"] = {int(r[0]) for r in query("Arcadia", 'select "Id" from "StringResources"')}
    seven["StringResources"] = client73_ids.string_ids(pathlib.Path(client_dir) / "db_string.rdb")
    kept["StringResources"] = db["StringResources"] & seven["StringResources"]
    removed["StringResources"] = db["StringResources"] - kept["StringResources"]
    reasons["StringResources"] = {}
    return db, seven, kept, removed, reasons


def apply(removed):
    statements = ["begin;"]

    def ids(table):
        return "array[" + ",".join(str(i) for i in sorted(removed[table])) + "]::bigint[]"

    # String ids outside db_string have no vocabulary in this client. Clear nullable
    # references first. Refuse to destroy a NOT NULL effect definition in future imports.
    for table, column, required in query("Arcadia", "SELECT c.relname, a.attname, a.attnotnull FROM pg_constraint f "
            "JOIN pg_class c ON c.oid=f.conrelid JOIN pg_attribute a ON a.attrelid=c.oid AND a.attnum=ANY(f.conkey) "
            "WHERE f.confrelid='\"StringResources\"'::regclass"):
        if required == "t":
            if query("Arcadia", f'SELECT 1 FROM "{table}" WHERE "{column}" = any({ids("StringResources")}) LIMIT 1'):
                raise ValueError(f"Unknown client string on required {table}.{column}; audit before pruning")
        else:
            statements.append(f'UPDATE "{table}" SET "{column}" = NULL WHERE "{column}" = any({ids("StringResources")});')
    # Children first; a kept row never points at a removed one (the closure), so no foreign key fires.
    statements.append(f'delete from "QuestLinkResources" where "QuestId" = any({ids("QuestResources")}) '
                      f'or "NpcId" = any({ids("NpcResources")});')
    for table in ("QuestResources", "NpcResources", "MonsterResources", "ItemResources", "SkillResources",
                  "SummonResources", "StateResources", "StatResources", "StringResources"):
        if removed[table]:
            statements.append(f'delete from "{table}" where "Id" = any({ids(table)});')
    statements.append("commit;")
    with tempfile.NamedTemporaryFile(suffix=".sql", delete=False) as temporary:
        script = pathlib.Path(temporary.name)
    script.write_text("\n".join(statements) + "\n", encoding="utf-8")
    config = settings()
    result = subprocess.run([PSQL, "-h", config.get("DataSource", "localhost"), "-p", str(config.get("Port", 5432)),
                             "-U", config["User"], "-d", "Arcadia", "-v", "ON_ERROR_STOP=1", "-f", str(script)],
                            capture_output=True, text=True, env={**os.environ, "PGPASSWORD": config["Password"]})
    script.unlink()
    if result.returncode:
        raise RuntimeError(result.stderr)
    print(result.stdout)


def main(argv):
    if not argv:
        print(__doc__)
        return 2
    db, seven, kept, removed, reasons = plan(argv[0])
    total = 0
    for table in db:
        kept_outside = kept[table] - seven[table]
        print(f"{table:18} {len(db[table]):6} rows, 7.3 set {len(seven[table] & db[table]):6}, "
              f"removed {len(removed[table]):5}, kept outside the 7.3 set {len(kept_outside):4}")
        by_reason = {}
        for i in kept_outside:
            by_reason[reasons[table].get(i, "?")] = by_reason.get(reasons[table].get(i, "?"), 0) + 1
        for why, count in sorted(by_reason.items(), key=lambda kv: -kv[1]):
            print(f"{'':20}{count:5} {why}")
        total += len(removed[table])
    print(f"{total} rows to remove")
    if "--apply" in argv:
        apply(removed)
        print("applied")
    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
