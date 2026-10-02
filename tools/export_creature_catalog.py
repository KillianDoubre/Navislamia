"""Export the creature catalogue (summons and taming) to DevConsole/creature-catalog.73.json.

What the official server keeps in GameContent and the game reads at runtime, from the Epic 7 tables where they
exist (data/epic7, tools/rdu.py) and from the 9.4 export for what the Epic 7 dump lacks:

- Summons: every SummonResource row (stat_id, card, type, form, run speed, attack range, size, scale, name id)
  with its StatResource base stats inlined, so a summon's stats never query the database
  (StructSummon::GetBaseStat reads GameContent::GetStatInfo(stat_id)).
- Names: SummonDefaultNameResource split into prefixes (kind 0) and postfixes (kind 1), resolved to their
  English text — GameContent::GetSummonName() draws one of each.
- Monsters: the English name of every monster with a taming_id, for the TAMING_START|name| party line
  (BroadcastTamingMessage writes pMonster->GetName(), the server-side string).

Usage:
    python tools/export_creature_catalog.py
"""
import csv
import json
import os

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
EPIC7 = os.path.join(ROOT, 'data', 'epic7')
SQL = os.path.join(ROOT, 'data', 'sqlserver', 'Arcadia')


def rows(path):
    with open(path, encoding='utf-8') as stream:
        return list(csv.DictReader(stream))


def number(value, kind=int):
    try:
        return kind(float(value)) if kind is int else kind(value)
    except (TypeError, ValueError):
        return kind(0)


def main():
    strings = {row['code']: row['value'].rstrip('\0') for row in rows(os.path.join(SQL, 'StringResource_EN.csv'))}
    stats = {row['id']: row for row in rows(os.path.join(EPIC7, 'StatResource.csv'))}

    summons = []
    for row in rows(os.path.join(EPIC7, 'SummonResource.csv')):
        stat = stats.get(row['stat_id'])
        summons.append({
            'Id': number(row['id']),
            'NameId': number(row['name_id']),
            'Name': strings.get(row['name_id'], ''),
            'Type': number(row['type']),
            'Rate': number(row['rate']),
            'Form': number(row['form']),
            'EvolveTarget': number(row['evolve_target']),
            'CardId': number(row['card_id']),
            'StatId': number(row['stat_id']),
            'RunSpeed': number(row['run_speed']),
            'AttackRange': number(row['attack_range'], float),
            'Size': number(row['size'], float),
            'Scale': number(row['scale'], float),
            'Stats': None if stat is None else [number(stat[key], float)
                                                for key in ('str', 'vit', 'dex', 'agi', 'int', 'men', 'luk')],
        })

    prefixes, postfixes = [], []
    for row in rows(os.path.join(SQL, 'SummonDefaultNameResource.csv')):
        text = strings.get(row['text_id'], '')
        if text:
            (postfixes if row['kind'] == '1' else prefixes).append(text)

    monsters = {}
    for row in rows(os.path.join(EPIC7, 'MonsterResource.csv')):
        if row['taming_id'] not in ('', '0'):
            monsters[row['id']] = strings.get(row['name_id'], '')

    output = os.path.join(ROOT, 'DevConsole', 'creature-catalog.73.json')
    document = {
        'Source': 'Epic 7 SummonResource/StatResource/MonsterResource, 9.4 SummonDefaultNameResource and English strings',
        'CreatureCatalog': {
            'Summons': summons,
            'NamePrefixes': prefixes,
            'NamePostfixes': postfixes,
            'TamableMonsterNames': monsters,
        },
    }
    with open(output, 'w', encoding='utf-8', newline='\n') as stream:
        json.dump(document, stream, ensure_ascii=False, separators=(',', ':'))
        stream.write('\n')
    missing = sum(1 for summon in summons if summon['Stats'] is None)
    print(f'{output}: {len(summons)} summons ({missing} without stats), {len(prefixes)}+{len(postfixes)} name parts, '
          f'{len(monsters)} tamable monsters')


if __name__ == '__main__':
    main()
