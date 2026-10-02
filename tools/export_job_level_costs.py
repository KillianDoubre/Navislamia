"""Export the job level JP costs of every job depth to DevConsole/job-level-costs.73.json.

LevelResource holds one row per level with `jp_0`..`jp_3`: the JP a job level costs at that job depth (base,
first job, second job, master class), 0 once the tier is capped. The Postgres `LevelResources.JLvs` column is
`integer[]`, and `jp_3` overflows an int at six master-class levels (max 24 293 159 660), so only `jp_0` ever
reached the game. This catalogue carries all four tiers as 64-bit integers, read at startup by
Program.ConfigureJobLevelCosts, like the other catalogues.

The Epic 7 Part 4 dump has no LevelResource table, so the source is the 9.4 export
(data/sqlserver/Arcadia/LevelResource.csv); the official GetNeedJpForJobLevelUp(level, depth) reads the same
column per depth.

Usage:
    python tools/export_job_level_costs.py [--source data/sqlserver/Arcadia/LevelResource.csv]
"""
import argparse
import csv
import json
import os
import sys

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--source', default=os.path.join(ROOT, 'data', 'sqlserver', 'Arcadia', 'LevelResource.csv'))
    args = parser.parse_args()

    rows = list(csv.DictReader(open(args.source, encoding='utf-8')))
    if not rows:
        sys.exit(f'{args.source}: no rows')

    max_level = max(int(row['level']) for row in rows)
    depths = []
    for depth in range(4):
        costs = [0] * (max_level + 1)
        for row in rows:
            costs[int(row['level'])] = int(float(row[f'jp_{depth}'] or 0))
        depths.append(costs)

    output = os.path.join(ROOT, 'DevConsole', 'job-level-costs.73.json')
    document = {
        'Source': '9.4 LevelResource jp_0..jp_3 (the Epic 7 dump has no LevelResource); index = current job level',
        'JobLevelCosts': {'Depths': depths},
    }
    with open(output, 'w', encoding='utf-8', newline='\n') as stream:
        json.dump(document, stream, separators=(',', ':'))
        stream.write('\n')
    caps = [next((level for level in range(1, max_level + 1) if costs[level] == 0), max_level) for costs in depths]
    print(f'{output}: {len(depths)} depths, {max_level} levels, job level caps {caps}')


if __name__ == '__main__':
    main()
