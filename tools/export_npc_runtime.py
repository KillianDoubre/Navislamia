"""Export the official NPC functions needed by stamp/event/gift/repair dialogues.

Usage: python tools/export_npc_runtime.py <official Resource/Script directory>
The generated resource is committed: the running server needs no external scripts.
"""
import json
import re
import sys
from pathlib import Path

source = Path(sys.argv[1])
functions = {}
for filename in ('NPC_QuestClient.lua', 'NPC_Event.lua', 'NPC_ItemUP.lua', 'NPC_JobChange.lua'):
    text = (source / filename).read_text(encoding='cp949', errors='replace')
    text = re.sub(r'--\[\[.*?\]\]', '', text, flags=re.S)
    starts = list(re.finditer(r'^function\s+(\w+)\s*\(', text, re.M))
    for index, match in enumerate(starts):
        block = text[match.start():starts[index + 1].start() if index + 1 < len(starts) else len(text)]
        ends = list(re.finditer(r'^\s*end\s*(?:--[^\n]*)?$', block, re.M))
        if not ends:
            continue
        block = block[:ends[-1].end()]
        # Comments contain legacy Korean text; they are irrelevant to execution.
        block = '\n'.join(line.split('--', 1)[0].rstrip() for line in block.splitlines())
        functions[match[1]] = block

roots = {name for name in functions if name.startswith(('question_stamp', 'valentine_', 'event_', 'second_present', 'dormancyuser_'))}
roots.update(('max_item_durability', 'tp_skill', 'tp_skill_2', 'random_item_change_menu'))
selected = roots.copy()
while True:
    previous = selected.copy()
    for name in previous:
        selected.update(call for call in re.findall(r'\b(\w+)\s*\(', functions[name]) if call in functions)
    # Dynamic contact menus must evaluate their conditions as well as the selected action.
    selected.update(name for name, body in functions.items() if name.endswith('_contact')
                    and any(call in previous for call in re.findall(r'\b(\w+)\s*\(', body)))
    if previous == selected:
        break

# The field props' scripts (script_text, run by UseProp: docs/packet-specs/socle-props.md), from the files that define
# them. Read after the NPC files and without overriding them; no contact menu is pulled in for these roots, so a prop
# script never takes an NPC's dialogue over.
for filename in ('NPC_Dungeon.lua', 'ETC_dungeon_prop.lua', 'NPC_Adventure_Guide.lua', 'NPC_TeleportTown.lua',
                 'ETC_huntaholicprop.lua'):
    text = (source / filename).read_text(encoding='cp949', errors='replace')
    text = re.sub(r'--\[\[.*?\]\]', '', text, flags=re.S)
    starts = list(re.finditer(r'^function\s+(\w+)\s*\(', text, re.M))
    for index, match in enumerate(starts):
        block = text[match.start():starts[index + 1].start() if index + 1 < len(starts) else len(text)]
        ends = list(re.finditer(r'^\s*end\s*(?:--[^\n]*)?$', block, re.M))
        if ends:
            functions.setdefault(match[1], '\n'.join(line.split('--', 1)[0].rstrip()
                                                     for line in block[:ends[-1].end()].splitlines()))

props = json.loads((Path(__file__).resolve().parents[1] / 'DevConsole/field-props.73.json').read_text(encoding='utf-8'))
prop_roots = {re.match(r'\s*(\w+)', template['LuaScript'])[1]
              for template in props['FieldPropCatalog']['Templates'] if template.get('LuaScript')} & set(functions)
pending = set(prop_roots)
while pending:
    name = pending.pop()
    if name not in selected:
        selected.add(name)
        pending.update(call for call in re.findall(r'\b(\w+)\s*\(', functions[name]) if call in functions)

resource = Path(__file__).resolve().parents[1] / 'Game/Scripting/Scripts/npc_dialogs.json'
resource.write_text(json.dumps({name: functions[name] for name in sorted(selected)}, ensure_ascii=True, indent=2) + '\n', encoding='utf-8')
print(f'Exported {len(selected)} official functions to {resource.name}')
