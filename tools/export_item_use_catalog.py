"""Export Epic 7.x consumable use fields for the game server.

Usage: python tools/export_item_use_catalog.py
"""

import csv
import json
from pathlib import Path


ROOT = Path(__file__).resolve().parents[1]
SOURCE = ROOT / "data/epic7/ItemResource.csv"
TARGET = ROOT / "DevConsole/item-use.73.json"
SKILLS = ROOT / "data/epic7/SkillResource.csv"

with SKILLS.open(encoding="utf-8-sig", newline="") as stream:
    recovery_skills = {
        int(row["id"]): (int(row["effect_type"]), int(float(row["var3"] or 0)))
        for row in csv.DictReader(stream)
        if row["effect_type"] in ("509", "510")
    }


def fields(row):
    def integer(name):
        return int(float(row[name] or 0))

    def types(prefix):
        return [integer(f"{prefix}_type_{slot}") for slot in range(4)]

    def values(prefix, column):
        return [float(row[f"{prefix}_{column}_{slot}"] or 0) for slot in range(4)]

    base_types = types("base")
    opt_types = types("opt")
    state_id = integer("state_id")
    recovery = []
    for prefix in ("base", "opt"):
        for slot in range(4):
            if integer(f"{prefix}_type_{slot}") != 5:
                continue
            skill_id = integer(f"{prefix}_var1_{slot}")
            if skill_id in recovery_skills:
                effect_type, amount = recovery_skills[skill_id]
                recovery.append({"SkillId": skill_id, "EffectType": effect_type, "Amount": amount})
    return {
        "Id": integer("id"),
        "UseMinLevel": integer("use_min_level"),
        "UseMaxLevel": integer("use_max_level"),
        "BaseType": integer("type"),
        "RenamesPet": 120 in base_types or 120 in opt_types,
        "CoolTime": integer("cool_time"),
        "CoolTimeGroup": integer("cool_time_group"),
        "BaseTypes": base_types,
        "BaseVar1": values("base", "var1"),
        "BaseVar2": values("base", "var2"),
        "OptTypes": opt_types,
        "OptVar1": values("opt", "var1"),
        "OptVar2": values("opt", "var2"),
        "StateId": state_id or None,
        "StateLevel": integer("state_level"),
        "StateTime": integer("state_time"),
        "RecoverySkills": recovery,
    }


with SOURCE.open(encoding="utf-8-sig", newline="") as stream:
    catalog = [fields(row) for row in csv.DictReader(stream) if row["type"] == "3"]

with TARGET.open("w", encoding="utf-8", newline="\n") as stream:
    stream.write('[\n')
    for index, item in enumerate(catalog):
        stream.write(json.dumps(item, separators=(",", ":")))
        stream.write(",\n" if index < len(catalog) - 1 else "\n")
    stream.write("]\n")

print(f"Exported {len(catalog)} Epic 7.x consumables to {TARGET}")
