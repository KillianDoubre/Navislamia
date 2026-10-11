"""Put the towns' training scarecrows into DevConsole/monster-spawns.73.json.

The official server places them in server_init.lua, rare_mob(): one respawn_rare_mob( -1, 12000, x, y, monster, 1, 0 )
per scarecrow, in the sections titled "<town> - 전투 수련장 지역 N" (combat training ground, area N) of Rondo, Laksy's
Anchor, Katan, Horizon and Secroute. SCRIPT_AddRareMobRespawnInfo (ScriptNPC.cpp) registers a 1 x 1 box at (x, y) with
is_wandering = the last argument (0). The scarecrows are fight_type 6 (TRAP): StructMonster::IsAttackable and IsMovable
keep them still and harmless, which MonsterFightType ports.

The other rare_mob() entries (rare bosses, El Kasia's dungeon-core statues, Espoir's 185002) are left out: only the
training grounds were asked for, and the Epic 7 Lua that places the rare bosses is monster_respawn.lua, already read.

Usage: python tools/export_training_grounds.py <server_init.lua> --client-monsters <db_monster.rdb>
(server_init.lua is cp949; the 2015 official server's, the Epic 7 dump has none.)
"""
import argparse
import json
import re
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
CATALOG = ROOT / 'DevConsole/monster-spawns.73.json'
TRAINING_GROUND = '전투 수련장'
CALL = re.compile(r'respawn_rare_mob\(\s*(-?\d+)\s*,\s*(\d+)\s*,\s*(\d+)\s*,\s*(\d+)\s*,\s*(\d+)\s*,\s*(\d+)\s*,\s*(\d+)\s*\)')


def training_grounds(lua_text):
    """(town section, x, y, monster, count) of every respawn_rare_mob of rare_mob()'s training-ground sections."""
    body = re.search(r'function\s+rare_mob\s*\(\s*\)(.*?)\nend', lua_text, re.S)
    if not body:
        raise SystemExit('rare_mob() not found')
    section, result = None, []
    for line in body[1].splitlines():
        stripped = line.strip()
        if stripped.startswith('--'):
            title = stripped.lstrip('-').strip()
            # A row marker (앞줄 front row, 뒷줄 back row) or a dashed rule stays in its section; any other comment
            # opens the next one (Espoir's 185002 follows Secroute's training ground with no " - " in its title).
            if title and not title.startswith(('앞줄', '뒷줄')):
                section = title
            continue
        match = CALL.match(stripped)
        if match and section and TRAINING_GROUND in section:
            result.append((section, int(match[3]), int(match[4]), int(match[5]), int(match[6])))
    return result


def main():
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument('server_init', type=Path)
    parser.add_argument('--client-monsters', required=True, type=Path,
                        help="the 7.3 client's db_monster.rdb: a monster it does not know is left out")
    args = parser.parse_args()

    sys.path.insert(0, str(ROOT / 'tools'))
    import client73_ids
    known = client73_ids.monster_ids(args.client_monsters.parent)

    entries = training_grounds(args.server_init.read_bytes().decode('cp949'))
    spawns = [{'MonsterId': monster, 'X': x, 'Y': y, 'Count': count, 'Radius': 0}
              for _, x, y, monster, count in entries if monster in known]
    if len(spawns) != len(entries):
        raise SystemExit(f'{len(entries) - len(spawns)} training scarecrows unknown to the client')

    document = json.loads(CATALOG.read_text(encoding='utf-8'))
    document['MonsterSpawnCatalog']['Spawns'] = spawns
    document['Metadata']['TrainingGroundSpawns'] = (
        f'{len(spawns)} training scarecrows of server_init.lua rare_mob() '
        f'({len({section.split(" - ")[0] for section, *_ in entries})} towns)')
    CATALOG.write_text(json.dumps(document, ensure_ascii=False, separators=(',', ':')) + '\n', encoding='utf-8')
    print(f'{len(spawns)} training scarecrows written')


if __name__ == '__main__':
    main()
