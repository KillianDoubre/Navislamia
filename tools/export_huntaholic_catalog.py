"""Export the HuntaHolic catalogue (Bear Road) to DevConsole/huntaholic-catalog.73.json.

What the official HuntaholicLoader keeps in GameContent::HUNTAHOLIC_BASE, from the 9.4 export
(data/sqlserver/Arcadia): the Epic 7 Part 4 dump has no Huntaholic*.rdu, and the 7.3 client's own
db_huntaholicresource.rdb carries the same base row (id 10000, period 1500 s, objective 3000).

- HuntaholicResource: id, hunting period (seconds), objective and max score, the lobby and dungeon
  positions and their channels.
- ChannelResource: the two channel boxes, kept in world units; HuntaholicManager turns them into region
  indices (GetRegionX(left) .. GetRegionX(right) + 1) at load, which the server does too.
- HuntaholicInstanceResource: the level tiers ([min, max)), point advantage, rewards. A tier's id is the
  lobby layer of the players of its levels.
- HuntaholicMonsterRespawnResource: one respawn entry per box (period in seconds; the loader multiplies by
  100 to get ar_time ticks — the server does it, the file keeps seconds).
- HuntaholicHealingpropResource: the healing props of each tier, as the loader's FIELD_PROP_RESPAWN_INFO (z offset,
  rotation, scale, height lock).
- FieldPropResource (data/epic7, else the 9.4 export): what those props are — the skill a double-click casts,
  use_count, regen_time (seconds), casting range and level limits.

Usage: python tools/export_huntaholic_catalog.py
"""
import csv
import json
import os

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
SQL = os.path.join(ROOT, 'data', 'sqlserver', 'Arcadia')
EPIC7 = os.path.join(ROOT, 'data', 'epic7')
OUT = os.path.join(ROOT, 'DevConsole', 'huntaholic-catalog.73.json')


def rows(name):
    with open(os.path.join(SQL, name + '.csv'), encoding='utf-8') as stream:
        return list(csv.DictReader(stream))


def num(value, kind=int):
    return kind(float(value)) if kind is int else kind(value)


def main():
    channels = {r['id']: r for r in rows('ChannelResource')}

    def box(channel_id):
        c = channels[channel_id]
        left, right = sorted((num(c['left']), num(c['right'])))
        top, bottom = sorted((num(c['top']), num(c['bottom'])))
        return {'Left': left, 'Top': top, 'Right': right, 'Bottom': bottom}

    tiers = {}
    for r in rows('HuntaholicInstanceResource'):
        tiers[(r['huntaholic_id'], r['instance_type_id'])] = {
            'Id': num(r['instance_type_id']), 'MinLevel': num(r['min_level']), 'MaxLevel': num(r['max_level']),
            'PointAdvantage': num(r['point_advantage'], float), 'RewardExp': num(r['reward_exp']),
            'RewardJp': num(r['reward_jp']), 'SuccessItemId': num(r['success_item_id']),
            'SuccessItemCount': num(r['success_item_count']), 'FailItemId': num(r['fail_item_id']),
            'FailItemCount': num(r['fail_item_count']), 'Respawns': [], 'HealingProps': []}

    for r in rows('HuntaholicMonsterRespawnResource'):
        tier = tiers[(r['huntaholic_id'], r['instance_type_id'])]
        left, right = sorted((num(r['left']), num(r['right'])))
        top, bottom = sorted((num(r['top']), num(r['bottom'])))
        tier['Respawns'].append({'Id': num(r['respawn_id']), 'Left': left, 'Top': top, 'Right': right,
                                 'Bottom': bottom, 'MonsterId': num(r['monster_id']), 'Count': num(r['count']),
                                 'PeriodSeconds': num(r['period']), 'IsWandering': r['is_wandering'] == '1'})

    for r in rows('HuntaholicHealingpropResource'):
        tier = tiers[(r['hunterholic_id'], r['instance_type'])]
        tier['HealingProps'].append({'Id': num(r['id']), 'PropId': num(r['prop_id']), 'X': num(r['prop_x']),
                                     'Y': num(r['prop_y']), 'ZOffset': num(r['offset_z'], float),
                                     'RotateX': num(r['around_x'], float), 'RotateY': num(r['around_y'], float),
                                     'RotateZ': num(r['around_z'], float), 'ScaleX': num(r['scale_x'], float),
                                     'ScaleY': num(r['scale_y'], float), 'ScaleZ': num(r['scale_z'], float),
                                     'LockHeight': r['is_height_locked'] == '1',
                                     'LockHeightValue': num(r['lock_height'], float)})

    # The Epic 7 FieldPropResource wins over the 9.4 one, as in tools/import_epic7.py.
    prop_path = os.path.join(EPIC7, 'FieldPropResource.csv')
    if not os.path.exists(prop_path):
        prop_path = os.path.join(SQL, 'FieldPropResource.csv')
    with open(prop_path, encoding='utf-8') as stream:
        props = {r['id']: r for r in csv.DictReader(stream)}

    def template(prop_id):
        p = props[str(prop_id)]
        return {'Id': prop_id, 'ActivateSkillId': num(p['activate_id']), 'UseCount': num(p['use_count']),
                'RegenSeconds': num(p['regen_time']), 'CastingRange': num(p['casting_range'], float),
                'MinLevel': num(p['limit_min_level']), 'MaxLevel': num(p['limit_max_level'])}

    bases = []
    for r in rows('HuntaholicResource'):
        bases.append({
            'Id': num(r['id']), 'NameId': num(r['name_id']), 'HuntingPeriodSeconds': num(r['hunting_period']),
            'ObjectivePoint': num(r['objective_point']), 'MaxPoint': num(r['max_point']),
            'LobbyX': num(r['lobby_x']), 'LobbyY': num(r['lobby_y']), 'LobbyArea': box(r['lobby_channel_id']),
            'DungeonX': num(r['dungeon_x']), 'DungeonY': num(r['dungeon_y']),
            'DungeonArea': box(r['dungeon_channel_id']),
            # The loader keeps the tiers in table order, and GetProperLobbyLayer returns the first that fits.
            'Tiers': [t for (h, _), t in tiers.items() if h == r['id']]})
        used = sorted({p['PropId'] for t in bases[-1]['Tiers'] for p in t['HealingProps']})
        bases[-1]['HealingPropTemplates'] = [template(prop_id) for prop_id in used]

    with open(OUT, 'w', encoding='utf-8') as stream:
        json.dump({'HuntaholicCatalog': {'Huntaholics': bases}}, stream, indent=1)
        stream.write('\n')
    tiers_count = sum(len(b['Tiers']) for b in bases)
    respawns = sum(len(t['Respawns']) for b in bases for t in b['Tiers'])
    print(f'{os.path.basename(OUT)}: {len(bases)} huntaholic(s), {tiers_count} tiers, {respawns} respawn entries')


if __name__ == '__main__':
    main()
