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
- Progression: CreatureLevelBonus (per-level stat growth, keyed by summon id: GameContent::GetSummonLevelBonus),
  SummonLevelResource.normal_exp (the cumulative table GameContent::GetNeedSummonExp reads for every form) and
  the Epic 7 CreatureEnhance rows (card enhance → stat amplifier, durability, wear slots, JP bonus). The first
  two exist only in the 9.4 export: the Epic 7 dump has no such .rdu.

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
    level_bonus = {row['id']: row for row in rows(os.path.join(SQL, 'CreatureLevelBonus.csv'))}
    seven = ('str', 'vit', 'dex', 'agi', 'int', 'men', 'luk')

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
            'Stats': None if stat is None else [number(stat[key], float) for key in seven],
            'LevelBonus': None if row['id'] not in level_bonus
            else [number(level_bonus[row['id']][key], float) for key in seven],
            'RidingSpeed': number(row['riding_speed']),
            'IsRidingOnly': number(row['is_riding_only']) != 0,
            'RidingKind': number(row['is_riding_only']),
        })

    summon_exp = [number(row['normal_exp'])
                  for row in sorted(rows(os.path.join(SQL, 'SummonLevelResource.csv')), key=lambda r: int(r['level']))]
    enhance = [{'Level': number(row['enhance_level']), 'StatAmplify': number(row['stat_amplify'], float),
                'CardDurability': number(row['card_durability']), 'SlotAmount': number(row['slot_amount']),
                'JpAddition': number(row['jp_addition'])}
               for row in sorted(rows(os.path.join(EPIC7, 'CreatureEnhance.csv')), key=lambda r: int(r['enhance_level']))]

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
        'Source': 'Epic 7 SummonResource/StatResource/MonsterResource/CreatureEnhance, 9.4 SummonDefaultNameResource/CreatureLevelBonus/SummonLevelResource and English strings',
        'CreatureCatalog': {
            'Summons': summons,
            'NamePrefixes': prefixes,
            'NamePostfixes': postfixes,
            'TamableMonsterNames': monsters,
            'SummonExp': summon_exp,
            'Enhance': enhance,
        },
    }
    with open(output, 'w', encoding='utf-8', newline='\n') as stream:
        json.dump(document, stream, ensure_ascii=False, separators=(',', ':'))
        stream.write('\n')
    missing = sum(1 for summon in summons if summon['Stats'] is None)
    bonus = sum(1 for summon in summons if summon['LevelBonus'] is not None)
    print(f'{output}: {len(summons)} summons ({missing} without stats, {bonus} with a level bonus), '
          f'{len(prefixes)}+{len(postfixes)} name parts, {len(monsters)} tamable monsters, '
          f'{len(summon_exp)} exp levels, {len(enhance)} enhance levels')


if __name__ == '__main__':
    main()
