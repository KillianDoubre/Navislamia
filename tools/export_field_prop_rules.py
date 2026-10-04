"""Adds the Epic 7 FieldPropResource rules to DevConsole/field-props.73.json.

Usage: python tools/export_field_prop_rules.py <data/epic7/FieldPropResource.csv>

tools/Export-FieldProps writes the positions and the templates from the 9.4 SQL export; this adds what the
official StructFieldProp/FieldPropManager use and the 9.4 rows lack or differ on (docs/packet-specs/socle-props.md):
use_count, regen_time and life_time (seconds, x100 like FieldPropLoader), casting_time, both activation conditions,
the two drop slots (ratio over 100 000 000) and the Epic 7 script when the 9.4 one has no action. Run it again after
Export-FieldProps.
"""
import csv
import json
import re
import sys
from pathlib import Path

catalog_path = Path(__file__).resolve().parents[1] / 'DevConsole/field-props.73.json'
rows = {int(r['id']): r for r in csv.DictReader(open(sys.argv[1], encoding='utf-8'))}
document = json.loads(catalog_path.read_text(encoding='utf-8'))
templates = document['FieldPropCatalog']['Templates']


def number(row, key):
    value = (row.get(key) or '').strip()
    return float(value) if value else 0.0


def action_less(script):
    return not re.match(r'\s*(common_warp_gate|RunTeleport|enter_dungeon|exit_dungeon|warp_gate|go_to_huntaholic|'
                        r'enter_secret_dungeon|exit_secret_dungeon|enter_instance_dungeon|exit_instance_dungeon|'
                        r'enter_other_instance_dungeon|warp_instance_floor)', script or '')


missing = []
for template in templates:
    row = rows.get(template['Id'])
    if row is None:
        missing.append(template['Id'])
        continue
    template['CastingTime'] = int(number(row, 'casting_time') * 100)
    template['UseCount'] = int(number(row, 'use_count'))
    template['RegenTime'] = int(number(row, 'regen_time') * 100)
    template['LifeTime'] = int(number(row, 'life_time') * 100)
    template['MinLevel'] = int(number(row, 'limit_min_level'))
    template['MaxLevel'] = int(number(row, 'limit_max_level'))
    template['LimitJobId'] = int(number(row, 'limit_job'))
    template['Activations'] = [
        {'Condition': int(number(row, c)), 'Value1': int(number(row, v1)), 'Value2': int(number(row, v2))}
        for c, v1, v2 in (('activation_condition', 'activation_value1', 'activation_value2'),
                          ('activation2_condition', 'activation2_value1', 'activation2_value2'))
        if int(number(row, c))]
    template['Drops'] = [
        {'ItemId': int(number(row, f'drop_item_id_{s}')),
         'Ratio': round(number(row, f'drop_item_ratio_{s}') * 100_000_000),
         'CountMin': int(number(row, f'drop_item_count_min_{s}')),
         'CountMax': int(number(row, f'drop_item_count_max_{s}')),
         'LevelMin': int(number(row, f'drop_item_level_min_{s}')),
         'LevelMax': int(number(row, f'drop_item_level_max_{s}'))}
        for s in ('00', '01') if int(number(row, f'drop_item_id_{s}'))]
    epic_script = (row.get('script_text') or '').strip()
    template['LuaScript'] = epic_script if action_less(template.get('Script')) and re.match(r'[A-Za-z_]\w*\s*\(', epic_script) else ''

if missing:
    sys.exit(f'{len(missing)} templates have no Epic 7 row, first {missing[0]}')
document['Metadata']['Rules'] = 'Epic 7 FieldPropResource (tools/export_field_prop_rules.py)'
# Compact, like Export-FieldProps writes it.
catalog_path.write_text(json.dumps(document, ensure_ascii=False, separators=(',', ':')), encoding='utf-8')
print(f'{len(templates)} templates: {sum(1 for t in templates if t["UseCount"])} limited, '
      f'{sum(1 for t in templates if t["Drops"])} with drops, {sum(1 for t in templates if t["LuaScript"])} Lua scripts')
