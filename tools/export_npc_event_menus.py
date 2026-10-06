"""The NPC menu entries that belong to a seasonal event, and the event each belongs to.

Usage: python tools/export_npc_event_menus.py [<official Resource/Script directory>] [<Epic 7 Part 4 branches/Live>]

The official Lua knows no date: an event's entry in a permanent NPC's menu (`dlg_menu( "@90604959",
'Trick_or_treat_2011()' )`, commented "Halloween candy // runs during the Halloween event only, in NPC_Event.lua") was
added to the script for the event and taken out after it. The catalogue (DevConsole/npc-dialogs.73.json) kept it for
good. An entry is an event's when its function is defined in NPC_Event.lua, or when its name says which event it is
(the 9.4 import brought Valentine_2016_Kale_Give, defined in neither copy). NpcEventMenus hides such an entry unless
GameRules:Events opens its event, and an NPC whose contact is an event's is not placed while it is closed
(docs/packet-specs/socle-pnj-evenements.md).

Writes Game/Services/npc-event-menus.json (embedded): {"Menus": {"function": "Event"}, "Npcs": {"npc id": "Event"}}.
"""
import json
import re
import sys
from pathlib import Path

root = Path(__file__).resolve().parents[1]
script = Path(sys.argv[1] if len(sys.argv) > 1 else r'A:\Rappelz Kiff\Rappelz\Rappelz\program\server\GameServer\Resource\Script')
live = Path(sys.argv[2] if len(sys.argv) > 2 else r'A:\Rappelz Kiff\Epic 7 Part 4\branches\Live')

event_functions = set()
for path in (script / 'NPC_Event.lua', live / 'NPC_Event.lua'):
    text = path.read_text(encoding='cp949', errors='replace')
    event_functions |= set(re.findall(r'(?m)^\s*function\s+(\w+)\s*\(', text))

# The event a function belongs to, by what its name says; anything else keeps its own name as its event.
NAMED = [
    (r'(?i)hallow|trick_or_treat|pumpkin|wonderland', 'Halloween'),
    (r'(?i)valentine', 'Valentine'),
    (r'(?i)xmas|christmas|santa|rangifer', 'Christmas'),
    (r'(?i)easter', 'Easter'),
    (r'(?i)thanks_?giving', 'Thanksgiving'),
    (r'(?i)new_?year|lunar', 'NewYear'),
]


def event_of(function):
    for pattern, name in NAMED:
        if re.search(pattern, function):
            return name
    return None


catalog = json.loads((root / 'DevConsole/npc-dialogs.73.json').read_text(encoding='utf-8-sig'))['NpcDialogCatalog']

def is_event(function):
    return (function in event_functions or re.search(r'(?i)(^|_)event(_|$)', function) is not None
            or event_of(function) is not None)


# Only the doors into an event: an event entry in the menu of a dialogue that is not an event's itself. What lies behind
# a door is reached through it alone, and the event NPCs that hold the rest are not placed (NpcSpawnRules).
result = {}
for owner, dialog in catalog['Dialogs'].items():
    if is_event(owner):
        continue
    for entry in dialog.get('Menu', []):
        match = re.match(r'\s*(\w+)', entry.get('Trigger', ''))
        if match and is_event(match[1]):
            result[match[1]] = event_of(match[1]) or match[1]

# The NPCs whose contact is an event's: the Halloween pumpkin spirits (npc_wonderland_teleport_contact, model
# spirit_blackpumpkin) and the Easter dragon stand all year by their local_flag, like every event NPC of the data.
npcs = {}
for npc_id, contact in catalog['Npcs'].items():
    match = re.match(r'\s*(\w+)', contact)
    if match and is_event(match[1]):
        npcs[npc_id] = event_of(match[1]) or match[1]

output = root / 'Game/Services/npc-event-menus.json'
output.write_text(json.dumps({'Menus': dict(sorted(result.items())),
                              'Npcs': dict(sorted(npcs.items(), key=lambda item: int(item[0])))}, indent=2) + '\n',
                  encoding='utf-8')
events = {}
for function, event in result.items():
    events.setdefault(event, []).append(function)
print(f'{len(result)} event menu entries over {len(events)} events, {len(npcs)} event NPCs -> {output.relative_to(root)}')
for event in ('Halloween', 'Valentine', 'Christmas', 'Easter'):
    print(f'  {event}: {len(events.get(event, []))} entries, {sum(1 for e in npcs.values() if e == event)} NPCs')
