"""Regenerate DevConsole/skill-catalog.73.json from the Epic 7 skill tree and JP tables.

Same catalogue as tools/Export-SkillCatalog.ps1 (which reads the 9.4 SQL Server), from the CSV that
tools/rdu.py writes out of the Epic 7 Part 4 dump (data/epic7), the data closest to the 7.3 client:

  * SkillTreeResource.csv: at Epic 7 the tree is keyed by job_id directly; the 9.4 schema moved it behind
    JobResource.skill_tree_id, which is why the PowerShell exporter joins through JobResource;
  * SkillJPResource.csv: jp_01..jp_50, the JP cost of each skill level.

A skill without a JP row is left out, as the SQL inner join does, and a skill's cost list stops at the
highest level its rules allow (50 at most). SkillTreeId is not an Epic 7 column and is written as the job
id: the runtime does not read it.

Usage: python tools/export_skill_catalog.py [--source data/epic7]
"""
import csv
import json
import pathlib
import sys
from datetime import datetime, timezone

ROOT = pathlib.Path(__file__).resolve().parent.parent
OUTPUT = ROOT / "DevConsole" / "skill-catalog.73.json"
JOB_IDS = [
    100, 101, 102, 103, 110, 111, 112, 113, 114, 120, 121, 122, 123, 124,
    200, 201, 202, 203, 210, 211, 212, 213, 214, 220, 221, 222, 223, 224,
    300, 301, 302, 303, 310, 311, 312, 313, 314, 320, 321, 322, 323, 324,
]


def rows(source, table):
    with open(source / f"{table}.csv", encoding="utf-8") as stream:
        yield from csv.DictReader(stream)


def integer(value):
    return int(float(value)) if value else 0


def main(argv):
    source = pathlib.Path(argv[argv.index("--source") + 1]) if "--source" in argv else ROOT / "data" / "epic7"
    if not source.is_absolute():
        source = ROOT / source

    jp = {integer(r["skill_id"]): [integer(r[f"jp_{i:02d}"]) for i in range(1, 51)]
          for r in rows(source, "SkillJPResource")}
    wanted = set(JOB_IDS)
    tree = [r for r in rows(source, "SkillTreeResource")
            if integer(r["job_id"]) in wanted and integer(r["skill_id"]) in jp]
    tree.sort(key=lambda r: tuple(integer(r[c]) for c in
                                  ("job_id", "skill_id", "min_skill_lv", "max_skill_lv", "lv", "job_lv")))

    jobs = {}
    for r in tree:
        job_id, skill_id = integer(r["job_id"]), integer(r["skill_id"])
        job = jobs.setdefault(job_id, {"JobId": job_id, "SkillTreeId": job_id, "Skills": {}})
        skill = job["Skills"].setdefault(skill_id, {"SkillId": skill_id, "JpCosts": jp[skill_id], "Rules": []})
        prerequisites = [{"SkillId": integer(r[f"need_skill_id_{i}"]), "Level": integer(r[f"need_skill_lv_{i}"])}
                         for i in (1, 2, 3) if integer(r[f"need_skill_id_{i}"]) != 0]
        ratio = float(r["jp_ratio"]) if r["jp_ratio"] else 0.0
        skill["Rules"].append({
            "MinSkillLevel": integer(r["min_skill_lv"]),
            "MaxSkillLevel": integer(r["max_skill_lv"]),
            "RequiredLevel": integer(r["lv"]),
            "RequiredJobLevel": integer(r["job_lv"]),
            "JpRatio": int(ratio) if ratio.is_integer() else ratio,
            "Prerequisites": prerequisites,
        })

    catalog_jobs = []
    for job_id in sorted(jobs):
        job = jobs[job_id]
        skills = []
        for skill in job["Skills"].values():
            top = max(rule["MaxSkillLevel"] for rule in skill["Rules"])
            skill["JpCosts"] = skill["JpCosts"][:min(50, top)]
            skills.append(skill)
        job["Skills"] = skills
        catalog_jobs.append(job)

    output = {
        "Source": "Epic 7 Part 4 classic-job trees and JP tables (tools/rdu.py); consumed by the Epic 7.3 protocol",
        "GeneratedAtUtc": datetime.now(timezone.utc).isoformat(),
        "SkillCatalog": {"Jobs": catalog_jobs},
    }
    with open(OUTPUT, "w", encoding="utf-8") as stream:
        json.dump(output, stream, ensure_ascii=False, separators=(",", ":"))
    missing = sorted(wanted - set(jobs))
    print(f"Exported {sum(len(j['Skills']) for j in catalog_jobs)} job/skill definitions for "
          f"{len(catalog_jobs)} jobs to {OUTPUT}" + (f"; jobs without a tree: {missing}" if missing else ""))


if __name__ == "__main__":
    main(sys.argv)
