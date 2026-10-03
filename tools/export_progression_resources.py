"""Export the official title definitions, conditions and dungeon cells for the runtime catalogue."""
import csv
import json
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]


def main():
    def rows(name):
        with (ROOT / 'data/sqlserver/Arcadia' / f'{name}.csv').open(encoding='utf-8-sig') as stream:
            return list(csv.DictReader(stream))
    document = {
        'Titles': [{'Id': int(r['id']), 'NameId': int(r['name_id']), 'Rate': int(r['rate']),
                    'Types': [int(r[f'opt_type_{i}']) for i in range(8)],
                    'Var1': [float(r[f'opt_var1_{i}']) for i in range(8)],
                    'Var2': [float(r[f'opt_var2_{i}']) for i in range(8)],
                    'Periodic': r['is_periodic'] == '1', 'Begin': r['begin_of_period'], 'End': r['end_of_period']}
                   for r in rows('TitleResource')],
        'ConditionTypes': [{'Id': int(r['id']), 'Category': int(r['category']),
                            'Values': [int(r[f'value{i}']) for i in range(3)], 'Set': r['is_set'] == '1',
                            'SkipDbUpdate': r['skip_db_update'] == '1'}
                           for r in rows('TitleConditionTypeResource')],
        'Conditions': [{'TitleId': int(r['title_id']), 'Group': int(r['group_id']),
                        'TypeId': int(r['condition_type_id']), 'Count': int(r['count']),
                        'Achieve': r['is_archieve_condition'] == '1'} for r in rows('TitleConditionResource')],
        'DungeonCells': [{'Id': int(r['id']), 'X': int(r['seamless_x']), 'Y': int(r['seamless_y'])}
                         for r in rows('DungeonResource')]
    }
    output = ROOT / 'Game/Services/Progression/progression-resources.json'
    output.parent.mkdir(parents=True, exist_ok=True)
    output.write_text(json.dumps(document, ensure_ascii=False, separators=(',', ':')), encoding='utf-8')
    print({key: len(value) for key, value in document.items()})


if __name__ == '__main__':
    main()
