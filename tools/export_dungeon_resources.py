"""Build the runtime dungeon catalogue from the exported Arcadia tables.

Besides the access tables it carries what the instance scenarios need (docs/packet-specs/socle-donjons-instances-secrets.md):
each respawn row's respawn_group, the props an instance poses on its layer at creation
(InstanceDungeonHealingPropResource — the exit gates of Vulcanus), the FieldPropResource rows of those props and of the
floor gates the scenario poses (126024-126027), and Vulcanus' room rewards, read from vulcanus_clear_reward in the Epic 7
ETC_dungeon_prop.lua (--lua).
"""
import argparse
import csv
import json
import re
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
DEFAULT_LUA = r'A:\Rappelz Kiff\Epic 7 Part 4\trunk\ETC_dungeon_prop.lua'

# The gates vulcanus_check_respawn_group_clear poses when a room is cleared (floors 1, 2, 3 and the boss room).
VULCANUS_GATES = [126024, 126025, 126026, 126027]


def rows(table):
    with (ROOT / f'data/sqlserver/Arcadia/{table}.csv').open(encoding='utf-8-sig', newline='') as source:
        return list(csv.DictReader(source))


def field_props():
    # The Epic 7 FieldPropResource wins over the 9.4 one, as in tools/import_epic7.py.
    path = ROOT / 'data/epic7/FieldPropResource.csv'
    if not path.exists():
        path = ROOT / 'data/sqlserver/Arcadia/FieldPropResource.csv'
    with path.open(encoding='utf-8-sig', newline='') as source:
        return {row['id']: row for row in csv.DictReader(source)}


def vulcanus_rewards(lua_path):
    """vulcanus_clear_reward: `difficulty == D` then `floor == F` then exp/jp/gold assignments."""
    source = Path(lua_path).read_bytes().decode('cp949', errors='replace').replace('\0', '')
    start = source.index('function vulcanus_clear_reward')
    body = source[start:source.index('\nfunction ', start + 1)]
    rewards, difficulty, floor = [], None, None
    for line in body.splitlines():
        code = line.split('--', 1)[0]
        if match := re.search(r'difficulty\s*==\s*(\d+)', code):
            difficulty = int(match[1])
        if match := re.search(r'floor\s*==\s*(\d+)', code):
            floor = int(match[1])
            rewards.append({'Difficulty': difficulty, 'Floor': floor, 'Exp': 0, 'Jp': 0, 'Gold': 0})
        # The `local exp = 0` declarations come before the first floor and set nothing.
        if rewards and (match := re.search(r'^\s*(exp|jp|gold)\s*=\s*(\d+)', code)):
            rewards[-1][match[1].capitalize()] = int(match[2])
    if not rewards:
        raise SystemExit(f'no vulcanus_clear_reward table in {lua_path}')
    return rewards


def select(table, mapping):
    return [{key: int(row[column]) for key, column in mapping.items()} for row in rows(table)]


def main():
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument('--lua', default=DEFAULT_LUA, help='the Epic 7 ETC_dungeon_prop.lua')
    args = parser.parse_args()
    data = {
        'Dungeons': select('DungeonResource', dict(Id='id', LocalFlag='local_flag', Kind='dungeon_type',
            Level='dungeon_level', X='raid_start_pos_x', Y='raid_start_pos_y', SiegeX='siege_start_pos_x',
            SiegeY='siege_start_pos_y', DefenceX='defence_pos_x', DefenceY='defence_pos_y',
            RaidOpen='raid_opening_time', RaidClose='raid_closing_time', SiegeOpen='siege_opening_time',
            SiegeClose='siege_closing_time', CellX='seamless_x', CellY='seamless_y',
            Boss1='boss01_id', Boss2='boss02_id', Connector='connector_id',
            ConnectorX='connector_pos_x', ConnectorY='connector_pos_y', Core='core_id',
            CoreX='core_pos_x', CoreY='core_pos_y', GuildParties='num_party_guild',
            MercenaryParties='num_party_mercenary', RaidParties='num_party_raid')),
        'Instances': select('InstanceDungeonResource', dict(Id='id', LocalFlag='local_flag',
            X='dungeon_x', Y='dungeon_y')),
        'Types': select('InstanceDungeonTypeResource', dict(DungeonId='instance_dungeon_id',
            Type='instance_type_id', MinLevel='min_level', MaxLevel='max_level', ItemId='need_item_id',
            ItemCount='need_item_count')),
        'Respawns': select('InstanceDungeonMonsterRespawnResource', dict(DungeonId='instance_dungeon_id',
            Type='instance_type_id', Left='left', Top='top', Right='right', Bottom='bottom',
            MonsterId='monster_id', Count='count', Period='period', Controlled='is_regen_control',
            Group='respawn_group')),
        'Secrets': [int(r['id']) for r in rows('SecretDungeonResource')],
        'NpcDungeons': {},
        # exit_dungeon, ETC_dungeon_prop.lua; these are outside positions, not the raid start positions.
        'Exits': {str(k): v for k, v in {
            130000: [155817, 103724], 130600: [152309, 102886], 130300: [103210, 100366],
            130500: [99757, 103236], 130400: [132995, 87096], 130700: [130842, 79586],
            130800: [132680, 128030], 130900: [137441, 128115], 121000: [91942, 117103],
            122000: [85752, 118062], 123000: [91998, 124400], 120700: [146015, 135591],
            120292: [132680, 128030], 70192: [103234, 100310]}.items()}
    }
    import re
    for npc in rows('NPCResource'):
        match = re.fullmatch(r'NPC_dungeon_siege_manager_contact\(\s*(\d+)\s*\)', npc['contact_script'].split('\0')[0].strip())
        if match:
            data['NpcDungeons'][npc['id']] = int(match[1])
    data['InstanceProps'] = [{
        'DungeonId': int(r['instance_dungeon_id']), 'Type': int(r['instance_type_id']), 'PropId': int(r['prop_id']),
        'X': int(float(r['x'])), 'Y': int(float(r['y'])), 'ZOffset': float(r['offset_z']),
        'RotateX': float(r['around_x']), 'RotateY': float(r['around_y']), 'RotateZ': float(r['around_z']),
        'ScaleX': float(r['scale_x']), 'ScaleY': float(r['scale_y']), 'ScaleZ': float(r['scale_z'])}
        for r in rows('InstanceDungeonHealingPropResource')]
    props = field_props()
    used = sorted({p['PropId'] for p in data['InstanceProps']} | set(VULCANUS_GATES))
    data['PropTemplates'] = [{
        'Id': prop_id, 'ActivateSkillId': int(props[str(prop_id)]['activate_id']),
        'Script': props[str(prop_id)]['script_text'].strip(),
        'MinLevel': int(props[str(prop_id)]['limit_min_level']),
        'MaxLevel': int(props[str(prop_id)]['limit_max_level'])} for prop_id in used if str(prop_id) in props]
    data['VulcanusRewards'] = vulcanus_rewards(args.lua)
    target = ROOT / 'Game/Services/Dungeons/dungeon-resources.json'
    target.parent.mkdir(parents=True, exist_ok=True)
    target.write_text(json.dumps(data, ensure_ascii=False, separators=(',', ':')) + '\n', encoding='utf-8')
    print(f'{len(data["Dungeons"])} dungeon variants, {len(data["Instances"])} instances, '
          f'{len(data["Types"])} difficulties, {len(data["Respawns"])} respawns')


if __name__ == '__main__':
    main()
