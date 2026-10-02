import csv
import json
import re
import sys
import unittest
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(ROOT / 'tools'))
from export_monster_trigger_profiles import build


class MonsterTriggerProfileTests(unittest.TestCase):
    def test_zero_probability_slots_are_selected_first_and_no_skill_is_invented(self):
        rows = [{'id': '1', 'monster_skill_link_id': '10'}]
        links = {'10': [{'SkillId': 100, 'Probability': 1}, {'SkillId': 200, 'Probability': 0}]}
        skills = {'100': {'is_harmful': '1'}, '200': {'is_harmful': '0'}}
        result = build(rows, links, {'10': [{}, {}, {}]}, skills, set())
        self.assertEqual(result[1]['actions'], [{'index': 1, 'self': True}, {'index': 0, 'self': False}, None])

    def test_identical_link_reuses_an_official_branch_and_empty_link_has_no_action(self):
        rows = [{'id': '1', 'monster_skill_link_id': '10'}, {'id': '2', 'monster_skill_link_id': '10'},
                {'id': '3', 'monster_skill_link_id': '11'}]
        result = build(rows, {}, {'10': [{}], '11': [{}]}, {}, {1})
        self.assertEqual(result, {2: {'alias': 1}, 3: {'actions': [None]}})

    def test_all_148_imported_profiles_are_reconstructed_from_existing_links(self):
        def rows(name):
            with (ROOT / 'data/epic7' / (name + '.csv')).open(encoding='utf-8-sig') as stream:
                return list(csv.DictReader(stream))
        source = (ROOT / 'Game/Scripting/Scripts/monster_triggers.lua').read_text(encoding='utf-8')
        covered = set(map(int, re.findall(r'monster_id\s*==\s*(\d+)', source)))
        data = json.loads((ROOT / 'DevConsole/monster-skills.73.json').read_text(encoding='utf-8-sig'))['MonsterSkillCatalog']
        resources = rows('MonsterResource')
        profiles = build(resources, data['Links'], data['Triggers'],
                         {r['id']: r for r in rows('SkillResource')}, covered)
        self.assertEqual(len(profiles), 148)
        self.assertEqual(sum('alias' in p for p in profiles.values()), 105)
        self.assertEqual(sum('actions' in p and not any(p['actions']) for p in profiles.values()), 19)
        by_id = {int(r['id']): r for r in resources}
        for code, profile in profiles.items():
            link = by_id[code]['monster_skill_link_id']
            if 'alias' in profile:
                self.assertIn(profile['alias'], covered)
                self.assertEqual(link, by_id[profile['alias']]['monster_skill_link_id'])
            else:
                self.assertEqual(len(profile['actions']), len(data['Triggers'][link]))
                for action in filter(None, profile['actions']):
                    self.assertLess(action['index'], len(data['Links'][link]))
        embedded = (ROOT / 'Game/Scripting/Scripts/monster_trigger_profiles.lua').read_text(encoding='utf-8')
        self.assertEqual(set(map(int, re.findall(r'^    \[(\d+)\]', embedded, re.M))), set(profiles))

    def test_all_52_external_control_quests_have_an_imported_start_link(self):
        def rows(name):
            with (ROOT / 'data/epic7' / (name + '.csv')).open(encoding='utf-8-sig') as stream:
                return list(csv.DictReader(stream))
        quests = {int(r['id']) for r in rows('QuestResource') if r['type'] == '701'}
        links = {int(r['quest_id']) for r in rows('QuestLinkResource') if r['flag_start'] == '1'}
        self.assertEqual(len(quests), 52)
        self.assertEqual(quests - links, set())


if __name__ == '__main__':
    unittest.main()
