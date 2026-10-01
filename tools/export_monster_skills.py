"""Regenerate DevConsole/monster-skills.73.json from a CSV export of the resource tables.

The source is the Epic 7 tables by default (data/epic7, written by tools/rdu.py from the Epic 7 Part 4 dump).

MonsterSkillResource is keyed by MonsterResource.monster_skill_link_id. The official loader
(onMonsterSkillInfo, CaptainHerlockServer.exe 0x1401c63a0, reading "SELECT * FROM dbo.MonsterSkillResource
ORDER BY id, sub_id") appends, for every row of an id and in sub_id order, each non-zero skillN_id as one
{id, level, probability} entry; the official AI (StructMonster::AI_processAttack, 0x140166350) then rolls
the entries in that order.

Only entries with a probability above zero are written: the AI never rolls the others, which only the
trigger scripts (Lua, trigger_N_function) reach. The triggers are not exported: this repository runs no Lua.

Usage:
    python tools/export_monster_skills.py [--source data/epic7]
"""

import csv
import json
import pathlib
import sys
from datetime import datetime, timezone

ROOT = pathlib.Path(__file__).resolve().parent.parent
OUTPUT = ROOT / "DevConsole" / "monster-skills.73.json"
SLOTS = 6


def main(argv):
    source = ROOT / "data" / "epic7"
    if "--source" in argv:
        source = pathlib.Path(argv[argv.index("--source") + 1])

    with open(source / "MonsterSkillResource.csv", encoding="utf-8") as stream:
        table = list(csv.DictReader(stream))
    table.sort(key=lambda row: (int(row["id"]), int(row["sub_id"])))

    links = {}
    skipped = 0
    for row in table:
        entries = links.setdefault(int(row["id"]), [])
        for slot in range(1, SLOTS + 1):
            skill = int(row[f"skill{slot}_id"] or 0)
            if skill == 0:
                continue
            probability = float(row[f"skill{slot}_probability"] or 0)
            if probability <= 0:
                skipped += 1
                continue
            entries.append({"SkillId": skill, "Level": int(row[f"skill{slot}_lv"] or 0),
                            "Probability": probability})

    links = {link: entries for link, entries in links.items() if entries}
    document = {
        "Metadata": {
            "GeneratedAt": datetime.now(timezone.utc).isoformat(),
            "Source": f"MonsterSkillResource ({source.name}), ordered by id, sub_id",
            "Links": len(links),
            "Entries": sum(len(entries) for entries in links.values()),
            "ZeroProbabilityEntriesSkipped": skipped,
        },
        "MonsterSkillCatalog": {"Links": {str(link): entries for link, entries in sorted(links.items())}},
    }
    OUTPUT.write_text(json.dumps(document, separators=(",", ":")), encoding="utf-8")
    print(f"{OUTPUT.name}: {document['Metadata']}")


if __name__ == "__main__":
    main(sys.argv[1:])
