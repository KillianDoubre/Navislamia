"""Export EventAreaResource.rdu and its Lua dependency closure, without database access.

Usage: python tools/export_event_areas.py TABLE.rdu SCRIPT_DIRECTORY
The available oldest table is Epic 8 Part 2; prefer Epic 7 Part 4 Lua, then supply
the matching Epic 8 directory as a third argument for functions absent in Epic 7.
"""
import csv
import json
import re
import sys
from pathlib import Path
from rdu import RduTable

root = Path(__file__).resolve().parents[1]
table = RduTable(sys.argv[1])
functions = {}
# Earlier directories have priority.
for directory in reversed(sys.argv[2:]):
    for file in sorted(Path(directory).glob('*.lua')):
        text = file.read_text(encoding='cp949', errors='replace')
        text = re.sub(r'--\[\[.*?\]\]', '', text, flags=re.S)
        starts = list(re.finditer(r'^function\s+(\w+)\s*\(', text, re.M))
        for i, match in enumerate(starts):
            block = text[match.start():starts[i + 1].start() if i + 1 < len(starts) else len(text)]
            ends = list(re.finditer(r'^\s*end\s*(?:--[^\n]*)?$', block, re.M))
            if ends:
                functions[match[1]] = '\n'.join(line.split('--', 1)[0].rstrip() for line in block[:ends[-1].end()].splitlines())
rows = [dict(zip((c.name for c in table.columns), row)) for row in table.rows]
handlers = [row[c].strip() for row in rows for c in ('script_enter_text', 'script_leave_text') if row[c].strip() not in ('', '0')]
selected = {call for body in handlers for call in re.findall(r'\b(\w+)\s*\(', body) if call in functions}
while True:
    more = {call for name in selected for call in re.findall(r'\b(\w+)\s*\(', functions[name]) if call in functions}
    if more <= selected:
        break
    selected |= more
missing = sorted({call for body in handlers for call in re.findall(r'\b(\w+)\s*\(', body) if call not in functions})
if missing:
    print(f'Entry functions absent from supplied official scripts: {missing}')
target = root / 'Game/Scripting/Scripts/event_area_functions.json'
target.write_text(json.dumps({name: functions[name] for name in sorted(selected)}, ensure_ascii=True, indent=2) + '\n', encoding='utf-8')
csvpath = root / 'data/epic7/EventAreaResource.csv'
with csvpath.open('w', newline='', encoding='utf-8') as output:
    writer = csv.writer(output); writer.writerow([c.name for c in table.columns]); writer.writerows(table.rows)
columns = ['Id', 'BeginTime', 'EndTime', 'MinLevel', 'MaxLevel', 'RaceJobLimit', 'Conditions', 'Values', 'CountLimit', 'EnterHandler', 'LeaveHandler']
def quote(s): return "'" + s.replace("'", "''") + "'"
lines = []
for row in table.rows:
    values = [str(v) for v in row[:6]]
    values += ["ARRAY[" + ','.join(str(row[6 + i * 3]) for i in range(6)) + ']']
    values += ["ARRAY[" + ','.join(str(row[7 + i * 3 + j]) for i in range(6) for j in range(2)) + ']']
    values += [str(row[24]), quote(row[25].strip()), quote(row[26].strip())]
    lines.append('(' + ','.join(values) + ',CURRENT_TIMESTAMP)')
sql = 'INSERT INTO "EventAreaResources" (' + ','.join('"' + c + '"' for c in columns + ['CreatedOn']) + ') VALUES\n'
sql += ',\n'.join(lines) + '\nON CONFLICT ("Id") DO NOTHING;\n'
(root / 'Game/DataAccess/Migrations/Arcadia/EventAreaResources.sql').write_text(sql, encoding='utf-8')
print(f'{len(rows)} areas, {len(selected)} Lua functions. Source: {table.path.name}')
