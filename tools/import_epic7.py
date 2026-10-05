"""Import the Epic 7 resource tables (data/epic7/*.csv, written by tools/rdu.py) into Postgres Arcadia.

The Epic 7 Part 4 dump is the closest data to the 7.3 client, so its rows win over the 9.4 import:

  * an existing row (same key) has every mapped column overwritten with its Epic 7 value;
  * an Epic 7 row the table lacks is inserted, a NOT NULL column the source has no value for getting
    its type's zero;
  * pruning to client 7.3 runs after importing and resolving foreign keys, including partial imports.

Columns are mapped by introspection: a Postgres column (EF property) takes the source column whose name
is the same once case and underscores are ignored, unless OVERRIDES says otherwise — the overrides are
the pairs the name rule cannot derive, taken from MigrateDatabase's ArcadiaResourcesMappingProfile and
the Import-*Columns.ps1 scripts. An array column takes its numbered source columns in order.

Foreign keys are written in a second pass, once every table holds its rows: a nullable foreign key
reads 0 as "none" (NULL) and is only set when the referenced row exists, so a key into a table this
import does not fill (EffectResources, ModelEffectResources) stays NULL instead of failing.

Usage:
    python tools/import_epic7.py --client-dir <extracted client> --plan  # both plans, no writes
    python tools/import_epic7.py --client-dir <extracted client>         # import then prune
    python tools/import_epic7.py --client-dir <extracted client> SkillResources StateResources
NAVIS_CLIENT73 can supply --client-dir; NAVIS_EPIC7 selects the CSV directory.
Back up Arcadia before a live import. Pruning is mandatory; no unfiltered success path.
The database password is read from DevConsole/appsettings.json (never printed).
"""
import argparse
import csv
import json
import os
import pathlib
import re
import subprocess
import sys
import tempfile

ROOT = pathlib.Path(__file__).resolve().parent.parent
DATA = pathlib.Path(os.environ.get("NAVIS_EPIC7", ROOT / "data" / "epic7"))

sys.path.insert(0, str(ROOT / "tools"))
import prune_to_client73  # noqa: E402
AUDIT = {"CreatedOn", "ModifiedOn", "DeletedOn"}

# Whitelists use the repository's separate enum masks, not the original combined ItemBase.nLimit.
ITEM_RACE_LIMITS = (("limit_deva", 1), ("limit_asura", 2), ("limit_gaia", 4))
ITEM_JOB_LIMITS = (("limit_fighter", 1024), ("limit_hunter", 2048),
                   ("limit_magician", 4096), ("limit_summoner", 8192))


def whitelist_mask(row, limits):
    """Convert the source's 0/1 strings to an allow-list; reject malformed flags."""
    mask = 0
    for column, bit in limits:
        flag = row[column].strip()
        if flag not in ("0", "1"):
            raise ValueError(f"{column}: expected 0 or 1, got {flag!r}")
        if flag == "1":
            mask |= bit
    return mask


def whitelist_sql(limits):
    return "(" + " + ".join(f'''CASE WHEN btrim(s."{column}") = '1' THEN {bit} ELSE 0 END'''
                             for column, bit in limits) + ")"


def r(prefix, first, last, suffix=""):
    return [f"{prefix}{i}{suffix}" for i in range(first, last + 1)]


VF = {f"UseWith{k}": f"vf_{v}" for k, v in {
    "OneHandSword": "one_hand_sword", "TwoHandSword": "two_hand_sword", "DoubleSword": "double_sword",
    "Dagger": "dagger", "DoubleDagger": "double_dagger", "Spear": "spear", "Axe": "axe",
    "OneHandAxe": "one_hand_axe", "DoubleAxe": "double_axe", "OneHandMace": "one_hand_mace",
    "TwoHandMace": "two_hand_mace", "Lightbow": "lightbow", "Heavybow": "heavybow", "Crossbow": "crossbow",
    "OneHandStaff": "one_hand_staff", "TwoHandStaff": "two_hand_staff", "ShieldOnly": "shield_only",
    "WeaponNotRequired": "is_not_need_weapon"}.items()}

# Import order matters for nothing but readability: foreign keys are resolved after every table.
TABLES = [
    ("EventAreaResources", "EventAreaResource", ["Id"], {
        "Conditions": r("activate_condition", 1, 6),
        "Values": [f"activate_value{i}_{j}" for i in range(1, 7) for j in range(1, 3)],
        "CountLimit": "count_limit", "EnterHandler": "script_enter_text", "LeaveHandler": "script_leave_text"}),
    ("StringResources", "StringResource", ["Id"], {"Id": "code"}),
    ("StatResources", "StatResource", ["Id"], {
        "Strength": "str", "Vitality": "vit", "Dexterity": "dex", "Agility": "agi",
        "Intelligence": "int", "Wisdom": "men", "Luck": "luk"}),
    ("StateResources", "StateResource", ["Id"], {
        "Id": "state_id", "DuplicateGroup": r("duplicate_group_", 1, 3),
        "UseOnCharacter": "uf_avatar", "UseOnSummon": "uf_summon", "UseOnMonster": "uf_monster",
        "BaseEffect": "base_effect_id", "AmplifyPerSkill": "amplify_per_skl",
        "Values": r("value_", 0, 19)}),
    ("SkillResources", "SkillResource", ["Id"], {
        **VF,
        "UseOnSelf": "uf_self", "UseOnParty": "uf_party", "UseOnGuild": "uf_guild",
        "UseOnNeutral": "uf_neutral", "UseOnPurple": "uf_purple", "UseOnEnemy": "uf_enemy",
        "UseOnCharacter": "tf_avatar", "UseOnSummon": "tf_summon", "UseOnMonster": "tf_monster",
        "StateLevelPerSkill": "state_level_per_skl", "RequiredStateId": "need_state_id",
        "RequiredStateLevel": "need_state_level", "RequiredStateExhaust": "need_state_exhaust",
        "RequiredLevel": "need_level", "RequiredHp": "need_hp", "RequiredMp": "need_mp",
        "RequiredHavoc": "need_havoc", "RequiredHavocBurst": "need_havoc_burst",
        "RequiredTarget": "is_need_target", "ElementalType": "elemental", "HatePerSkill": "hate_per_skl",
        "DescriptionId": "desc_id", "Values": r("var", 1, 20)}),
    ("ItemResources", "ItemResource", ["Id"], {
        "ItemBaseType": "type", "ItemType": "class", "Status": "status_flag", "SocketCount": "socket",
        "SetPart": "set_part_flag",
        "RaceRestriction": ITEM_RACE_LIMITS, "JobRestriction": ITEM_JOB_LIMITS,
        "BaseTypes": r("base_type_", 0, 3), "BaseVar1": r("base_var1_", 0, 3), "BaseVar2": r("base_var2_", 0, 3),
        "OptTypes": r("opt_type_", 0, 3), "OptVar1": r("opt_var1_", 0, 3), "OptVar2": r("opt_var2_", 0, 3),
        "EnhanceIds": ["enhance_0_id", "enhance_1_id"]}),
    ("SummonResources", "SummonResource", ["Id"], {
        "EvolveType": "form", "EvolveTargetId": "evolve_target", "ModelName": "model",
        "CameraPosition": ["camera_x", "camera_y", "camera_z"],
        "TargetPosition": ["target_x", "target_y", "target_z"],
        "SkillIds": r("skill", 1, 5, "_id")}),
    ("MonsterResources", "MonsterResource", ["Id"], {
        "FirstAttack": "f_fisrt_attack", "GroupFirstAttack": "f_group_first_attack",
        "ResponseCasting": "f_response_casting", "ResponseRace": "f_response_race",
        "ResponseBattle": "f_response_battle", "StandardWalkSpeed": "standard_walk_spped",
        "StandardRunSpeed": "standard_run_spped"}),
    ("NpcResources", "NPCResource", ["Id"], {"NameId": "name_text_id", "SexualId": "sexsual_id"}),
    ("QuestResources", "QuestResource", ["Id"], {"LimitJobDepth": "job_depth"}),
    ("QuestLinkResources", "QuestLinkResource", ["NpcId", "QuestId"], {}),
    ("RandomPoolResources", "RandomPoolResource", ["GroupId", "QuestTargetId"], {}),
    ("WorldLocations", "WorldLocation", None, {}),
]


def norm(name):
    return re.sub(r"[^a-z0-9]", "", name.lower())


def password():
    settings = json.loads((ROOT / "DevConsole" / "appsettings.json").read_text(encoding="utf-8-sig"))
    return settings["Database"]["Password"]


def psql(sql, fetch=False):
    env = dict(os.environ, PGPASSWORD=password())
    with tempfile.NamedTemporaryFile("w", suffix=".sql", delete=False, encoding="utf-8") as f:
        f.write(sql)
        path = f.name
    try:
        args = ["psql", "-h", "localhost", "-U", "postgres", "-d", "Arcadia", "-v", "ON_ERROR_STOP=1",
                "-q", "-f", path]
        if fetch:
            args[1:1] = ["-t", "-A", "-F", "\t"]
        out = subprocess.run(args, capture_output=True, text=True, env=env, encoding="utf-8")
        if out.returncode != 0:
            raise SystemExit(f"psql failed:\n{out.stderr}")
        return [line.split("\t") for line in out.stdout.splitlines() if line]
    finally:
        os.unlink(path)


def introspect():
    columns, foreign, primary = {}, {}, {}
    for table, column, dtype, udt, nullable in psql(
            "select table_name, column_name, data_type, udt_name, is_nullable from information_schema.columns "
            "where table_schema = 'public' order by table_name, ordinal_position", fetch=True):
        columns.setdefault(table, []).append((column, dtype, udt, nullable == "YES"))
    for table, column, ref in psql(
            "select tc.table_name, kcu.column_name, ccu.table_name from information_schema.table_constraints tc "
            "join information_schema.key_column_usage kcu on tc.constraint_name = kcu.constraint_name "
            "join information_schema.constraint_column_usage ccu on ccu.constraint_name = tc.constraint_name "
            "where tc.constraint_type = 'FOREIGN KEY'", fetch=True):
        foreign[(table, column)] = ref
    for table, column in psql(
            "select tc.table_name, kcu.column_name from information_schema.table_constraints tc "
            "join information_schema.key_column_usage kcu on tc.constraint_name = kcu.constraint_name "
            "where tc.constraint_type = 'PRIMARY KEY'", fetch=True):
        primary.setdefault(table, []).append(column)
    return columns, foreign, primary


BASE = {"int2": "smallint", "int4": "integer", "int8": "bigint", "numeric": "numeric", "bool": "boolean",
        "float4": "real", "float8": "double precision", "text": "text", "varchar": "text", "bpchar": "text",
        "timestamptz": "timestamptz", "timestamp": "timestamp"}


def cast(expr, udt):
    """A text source value as the column's type; an empty field is NULL."""
    t = BASE.get(udt.lstrip("_"), "text")
    v = f"NULLIF({expr}, '')"
    if t == "boolean":
        return f"({expr} IN ('1', 'Y', 'y', 'T', 't', 'true'))"
    if t == "smallint":
        # A 16-bit flag word may be stored unsigned in the source (state_time_type 33150): keep its bit
        # pattern, as Import-StateResourceColumns.ps1 does.
        return f"(((({v})::numeric::integer + 32768) % 65536) - 32768)::smallint"
    if t in ("integer", "bigint"):
        return f"({v})::numeric::{t}"
    if t in ("numeric", "real", "double precision"):
        return f"({v})::{t}"
    if t == "timestamptz":
        return f"(({v})::timestamp AT TIME ZONE 'UTC')"
    if t == "timestamp":
        return f"({v})::timestamp"
    return expr


def zero(udt):
    t = BASE.get(udt.lstrip("_"), "text")
    if udt.startswith("_"):
        return "'{}'"
    return {"boolean": "false", "text": "''", "timestamptz": "now()", "timestamp": "now()"}.get(t, "0")


def plan(table, source, overrides, columns, foreign):
    with (DATA / f"{source}.csv").open(encoding="utf-8", newline="") as stream:
        header = next(csv.reader(stream))
    by_norm = {norm(h): h for h in header}
    mapped, fk, missing, used = {}, {}, [], set()
    for column, dtype, udt, nullable in columns[table]:
        if column in AUDIT:
            continue
        spec = overrides.get(column)
        if spec is None and norm(column) in by_norm:
            spec = by_norm[norm(column)]
        if spec is None:
            missing.append((column, udt, nullable))
            continue
        if isinstance(spec, tuple):
            names = [name for name, _ in spec]
            if not all(name in header for name in names):
                raise SystemExit(f"{table}.{column}: source columns missing: {[name for name in names if name not in header]}")
            used.update(names)
            mapped[column] = whitelist_sql(spec)
            continue
        if isinstance(spec, list):
            if not all(s in header for s in spec):
                raise SystemExit(f"{table}.{column}: source columns missing: {[s for s in spec if s not in header]}")
            used.update(spec)
            element = udt.lstrip("_")
            # The entities read these arrays as non-nullable element collections (Npgsql refuses an array
            # holding a NULL), and every one of them uses 0 for "nothing": a NULL source element is 0.
            items = ", ".join(f"COALESCE({cast(f's.{chr(34)}{s}{chr(34)}', element)}, 0)" for s in spec)
            mapped[column] = f"ARRAY[{items}]::{BASE[element]}[]"
            continue
        if spec not in header:
            raise SystemExit(f"{table}.{column}: source column {spec} missing")
        used.add(spec)
        value = cast(f's."{spec}"', udt)
        if (table, column) in foreign:
            fk[column] = (value, foreign[(table, column)])
        elif not nullable and BASE.get(udt, "text") == "text":
            mapped[column] = f"COALESCE({value}, '')"
        else:
            mapped[column] = value
    return header, mapped, fk, missing, [h for h in header if h not in used]


def import_table(table, source, key, overrides, columns, foreign, primary, dry):
    header, mapped, fk, missing, unused = plan(table, source, overrides, columns, foreign)
    key = key or primary.get(table) or ["Id"]
    print(f"\n## {table} <- {source}.csv: {len(mapped)} columns, {len(fk)} foreign keys, key {key}")
    if missing:
        print("   not in the source (kept on update, zero on insert):",
              ", ".join(f"{c}{'' if n else '!'}" for c, _, n in missing))
    if unused:
        print("   source columns not used:", ", ".join(unused))
    if dry:
        return []
    staging = f"epic7_{table.lower()}"
    create = ", ".join(f'"{h}" text' for h in header)
    csv_path = str(DATA / f"{source}.csv").replace("\\", "/")
    key_cols = [c for c in key]
    key_src = {c: mapped.get(c) or fk[c][0] for c in key_cols}
    match = " AND ".join(f't."{c}" = {key_src[c]}' for c in key_cols)
    distinct = ", ".join(key_src[c] for c in key_cols)
    # Only the leading key is an id where 0 means "no row" (StringResource code 0); a trailing key such
    # as WorldLocation's weather_id/time_id legitimately holds 0.
    valid = " AND ".join(f"{key_src[c]} IS NOT NULL" for c in key_cols) + f" AND {key_src[key_cols[0]]} <> 0"
    updatable = [c for c in mapped if c not in key_cols]
    set_list = ", ".join(f'"{c}" = {mapped[c]}' for c in updatable)
    insert_cols = list(mapped) + [c for c, _, n in missing if not n] + (
        ["CreatedOn"] if any(c == "CreatedOn" for c, *_ in columns[table]) else [])
    insert_vals = [mapped[c] for c in mapped] + [zero(u) for c, u, n in missing if not n] + (
        ["now()"] if "CreatedOn" in insert_cols else [])
    audit_update = ', "ModifiedOn" = now()' if any(c == "ModifiedOn" for c, *_ in columns[table]) else ""
    sql = f"""
CREATE TABLE {staging} ({create});
\\copy {staging} FROM '{csv_path}' WITH (FORMAT csv, HEADER)
CREATE TABLE {staging}_rows AS SELECT DISTINCT ON ({distinct}) s.* FROM {staging} s WHERE {valid};
{"UPDATE " + f'"{table}"' + " t SET " + set_list + audit_update + f" FROM {staging}_rows s WHERE {match};" if set_list else ""}
INSERT INTO "{table}" ({", ".join(f'"{c}"' for c in insert_cols)})
SELECT {", ".join(insert_vals)} FROM {staging}_rows s
WHERE NOT EXISTS (SELECT 1 FROM "{table}" t WHERE {match});
SELECT (SELECT count(*) FROM {staging}_rows),
       (SELECT count(*) FROM "{table}"),
       (SELECT count(*) FROM "{table}" t WHERE NOT EXISTS (SELECT 1 FROM {staging}_rows s WHERE {match}));
"""
    counts = psql(sql, fetch=True)[-1]
    print(f"   {counts[0]} Epic 7 rows; table now {counts[1]} rows, of which {counts[2]} are 9.4-only")
    return [(table, staging, match, fk)]


def resolve_foreign_keys(pending):
    for table, staging, match, fk in pending:
        for column, (value, ref) in fk.items():
            sql = f"""
UPDATE "{table}" t SET "{column}" = r."Id"
FROM {staging}_rows s LEFT JOIN "{ref}" r ON r."Id" = NULLIF({value}, 0)
WHERE {match} AND t."{column}" IS DISTINCT FROM r."Id";
"""
            psql(sql)
        if fk:
            print(f"   {table}: foreign keys {', '.join(fk)} resolved against the rows that exist")


def drop_staging(pending):
    for _, staging, _, _ in pending:
        psql(f"DROP TABLE IF EXISTS {staging}; DROP TABLE IF EXISTS {staging}_rows;")


def validate_client(client_dir):
    # Validate every file needed by the mandatory filter before the first import write.
    prune_to_client73.client73_ids.client_ids(client_dir)
    prune_to_client73.client73_ids.string_ids(pathlib.Path(client_dir) / "db_string.rdb")
    prune_to_client73.EPIC7 = DATA
    prune_to_client73.epic7_ids("StateResource", "state_id")
    prune_to_client73.epic7_ids("StatResource", "id")


def main(argv):
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--plan", action="store_true")
    parser.add_argument("--client-dir", default=os.environ.get("NAVIS_CLIENT73"))
    parser.add_argument("tables", nargs="*")
    args = parser.parse_args(argv[1:])
    if not args.client_dir:
        parser.error("--client-dir or NAVIS_CLIENT73 is required for mandatory 7.3 pruning")
    unknown = set(args.tables) - {t for t, *_ in TABLES}
    if unknown:
        parser.error("Unknown tables: " + ", ".join(sorted(unknown)))
    validate_client(args.client_dir)
    dry, only = args.plan, args.tables
    columns, foreign, primary = introspect()
    if not dry:
        for staging in [f"epic7_{t.lower()}" for t, *_ in TABLES]:
            psql(f"DROP TABLE IF EXISTS {staging}; DROP TABLE IF EXISTS {staging}_rows;")
    pending = []
    for table, source, key, overrides in TABLES:
        if only and table not in only:
            continue
        pending += import_table(table, source, key, overrides, columns, foreign, primary, dry)
    if not dry:
        print()
        resolve_foreign_keys(pending)
        drop_staging(pending)
    print("\nMandatory client 7.3 filter" + (" (plan against current database)" if dry else ""))
    # Typed references follow official Resource/Script/NPC_ItemUP.lua:109,152-177,
    # documented with all audited sources in filtre-ressources-73.md §5.
    # Errors propagate: an import cannot report success when its final filter fails.
    return prune_to_client73.main([args.client_dir] + ([] if dry else ["--apply"]))


if __name__ == "__main__":
    sys.exit(main(sys.argv))
