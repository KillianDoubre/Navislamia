"""Export the official NPC functions needed by stamp/event/gift/repair dialogues.

Usage: python tools/export_npc_runtime.py <official Resource/Script directory> [<Epic 7 Part 4 branches/Live directory>]
The generated resource is committed: the running server needs no external scripts.

The Hidden Village teleporters, the "to Hidden Village" teleporters of the towns, the Flea Market (maricat) NPCs and
the auctioneers run their official Lua too (docs/packet-specs/socle-pnj-pays-periodes.md): their menus depend on the
NPC id and on the Hidden Village pass. The teleporters come from the Epic 7 Lua (branches/Live, 2012), closer to the
7.3 client than the 2015 copy; NPC_Auction.lua exists in the 2015 tree only.
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

text = (source / 'NPC_CreatureSetup.lua').read_text(encoding='cp949', errors='replace')
starts = list(re.finditer(r'^function\s+(\w+)\s*\(', text, re.M))
for index, match in enumerate(starts):
    if match[1] not in ('Creature_name_change_Menu', 'Creature_name_change_gold'):
        continue
    block = text[match.start():starts[index + 1].start() if index + 1 < len(starts) else len(text)]
    ends = list(re.finditer(r'^\s*end\s*(?:--[^\n]*)?$', block, re.M))
    functions[match[1]] = '\n'.join(line.split('--', 1)[0].rstrip() for line in block[:ends[-1].end()].splitlines())
    selected.add(match[1])

props = json.loads((Path(__file__).resolve().parents[1] / 'DevConsole/field-props.73.json').read_text(encoding='utf-8'))
prop_roots = {re.match(r'\s*(\w+)', template['LuaScript'])[1]
              for template in props['FieldPropCatalog']['Templates'] if template.get('LuaScript')} & set(functions)
pending = set(prop_roots)
pending.add('on_channel_set')
while pending:
    name = pending.pop()
    if name not in selected:
        selected.add(name)
        pending.update(call for call in re.findall(r'\b(\w+)\s*\(', functions[name]) if call in functions)

def read_functions(path):
    text = path.read_text(encoding='cp949', errors='replace').replace('\x00', '')
    text = re.sub(r'--\[\[.*?\]\]', '', text, flags=re.S)
    starts = list(re.finditer(r'^function\s+(\w+)\s*\(', text, re.M))
    result = {}
    for index, match in enumerate(starts):
        block = text[match.start():starts[index + 1].start() if index + 1 < len(starts) else len(text)]
        ends = list(re.finditer(r'^\s*end\s*(?:--[^\n]*)?$', block, re.M))
        if ends:
            result[match[1]] = '\n'.join(line.split('--', 1)[0].rstrip() for line in block[:ends[-1].end()].splitlines())
    return result


epic7 = Path(sys.argv[2] if len(sys.argv) > 2 else r'A:\Rappelz Kiff\Epic 7 Part 4\branches\Live')
town = read_functions(epic7 / 'NPC_TeleportTown.lua')
auction = read_functions(source / 'NPC_Auction.lua')
contact_roots = ['NPC_TeleportTown_1_Secroute_contact', 'NPC_TeleportTown_2_Secroute_contact',
                 'NPC_TeleportSecroute_Town_contact', 'NPC_maricat_market_teleport_contact',
                 'NPC_maricat_market_maricat_contact', 'NPC_maricat_market_guard_contact']
contact_roots += sorted(name for name in auction if name.startswith('NPC_Auction_') and name.endswith('_contact'))
# The Epic 7 definitions win over the 2015 ones for these menus and everything they call.
pending = set(contact_roots)
done = set()
while pending:
    name = pending.pop()
    if name in done:
        continue
    done.add(name)
    body = town.get(name) or auction.get(name)
    if body is None:
        raise SystemExit(f'{name} is in neither {epic7} nor NPC_Auction.lua')
    functions[name] = body
    selected.add(name)
    pending.update(call for call in re.findall(r'\b(\w+)\s*\(', body) if call in town or call in auction)

resource = Path(__file__).resolve().parents[1] / 'Game/Scripting/Scripts/npc_dialogs.json'
resource.write_text(json.dumps({name: functions[name] for name in sorted(selected)}, ensure_ascii=True, indent=2) + '\n', encoding='utf-8')
print(f'Exported {len(selected)} official functions to {resource.name}')
