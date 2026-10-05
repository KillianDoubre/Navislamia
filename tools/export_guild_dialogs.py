"""Export the official guild officer and dungeon siege manager dialogues (docs/packet-specs/socle-dialogues-guilde-siege.md).

Usage: python tools/export_guild_dialogs.py "<Epic 7 Part 4>/trunk"
The Epic 7 Part 4 scripts are the closest to the 7.3 client (NPC_CreateGuild.lua, NPC_QuestClient.lua). Every function
reachable from the officers' contacts (NPC_CreateGuild_*_contact) and from NPC_dungeon_siege_manager_contact is kept —
called directly or advertised as a menu trigger — plus the two window callbacks the server calls itself
(on_create_guild, on_create_alliance). The generated resource is committed: the running server needs no external script.
"""
import json
import re
import sys
from pathlib import Path


def read_functions(path):
    text = path.read_text(encoding='cp949', errors='replace')
    text = re.sub(r'--\[\[.*?\]\]', '', text, flags=re.S)
    starts = list(re.finditer(r'^function\s+(\w+)\s*\(', text, re.M))
    functions = {}
    for index, match in enumerate(starts):
        block = text[match.start():starts[index + 1].start() if index + 1 < len(starts) else len(text)]
        ends = list(re.finditer(r'^\s*end\s*(?:--[^\n]*)?$', block, re.M))
        if not ends:
            continue
        # Comments carry legacy Korean text; no string literal of these files contains "--".
        lines = (line.split('--', 1)[0].rstrip() for line in block[:ends[-1].end()].splitlines())
        functions[match[1]] = '\n'.join(line for line in lines if line)
    return functions


source = Path(sys.argv[1])
functions = {}
for filename in ('NPC_CreateGuild.lua', 'NPC_QuestClient.lua'):
    for name, body in read_functions(source / filename).items():
        functions.setdefault(name, body)

roots = {name for name in functions if re.fullmatch(r'NPC_CreateGuild_\w+_contact', name)}
roots.update(('NPC_dungeon_siege_manager_contact', 'on_create_guild', 'on_create_alliance'))
selected, pending = set(), set(roots)
while pending:
    name = pending.pop()
    if name in selected:
        continue
    selected.add(name)
    pending.update(call for call in re.findall(r'\b(\w+)\s*\(', functions[name]) if call in functions)

resource = Path(__file__).resolve().parents[1] / 'Game/Scripting/Scripts/guild_dialogs.json'
resource.write_text(json.dumps({name: functions[name] for name in sorted(selected)}, ensure_ascii=True, indent=2) + '\n',
                    encoding='utf-8')
print(f'Exported {len(selected)} official functions to {resource.name}')
