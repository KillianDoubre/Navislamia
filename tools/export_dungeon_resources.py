"""Build the runtime dungeon catalogue from the exported Arcadia tables."""
import csv
import json
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]


def rows(table):
    with (ROOT / f'data/sqlserver/Arcadia/{table}.csv').open(encoding='utf-8-sig', newline='') as source:
        return list(csv.DictReader(source))


def select(table, mapping):
    return [{key: int(row[column]) for key, column in mapping.items()} for row in rows(table)]


def main():
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
            MonsterId='monster_id', Count='count', Period='period', Controlled='is_regen_control')),
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
    target = ROOT / 'Game/Services/Dungeons/dungeon-resources.json'
    target.parent.mkdir(parents=True, exist_ok=True)
    target.write_text(json.dumps(data, ensure_ascii=False, separators=(',', ':')) + '\n', encoding='utf-8')
    print(f'{len(data["Dungeons"])} dungeon variants, {len(data["Instances"])} instances, '
          f'{len(data["Types"])} difficulties, {len(data["Respawns"])} respawns')


if __name__ == '__main__':
    main()
