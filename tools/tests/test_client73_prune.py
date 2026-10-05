import json
import struct
import sys
import tempfile
import unittest
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(ROOT / "tools"))
import client73_ids
import resource_reachability as rr


class Client73PruneTests(unittest.TestCase):
    def read_strings(self, payload):
        with tempfile.TemporaryDirectory() as directory:
            path = Path(directory) / "db_string.rdb"
            path.write_bytes(payload)
            return client73_ids.string_ids(path)

    def record(self, code):
        return struct.pack("<II", 2, 2) + b"n\0t\0" + struct.pack("<6i", code, 0, 0, 0, 0, 0)

    def test_strings_obey_declared_count_and_deduplicate(self):
        payload = bytes(128) + struct.pack("<I", 2) + self.record(42) * 2 + self.record(99)
        self.assertEqual({42}, self.read_strings(payload))

    def test_strings_reject_truncation_and_invalid_lengths(self):
        for payload in (b"", bytes(128) + struct.pack("<I", 1),
                        bytes(128) + struct.pack("<I", 1) + self.record(42)[:-1],
                        bytes(128) + struct.pack("<III", 1, 0, 10000000)):
            with self.subTest(payload=payload), self.assertRaises(ValueError):
                self.read_strings(payload)

    def test_strings_reject_missing_terminator(self):
        payload = bytes(128) + struct.pack("<I", 1) + self.record(42).replace(b"n\0", b"nn")
        with self.assertRaises(ValueError):
            self.read_strings(payload)

    def test_call_graph_follows_dialogs_cycles_and_export_alias(self):
        functions = {"root": "child()", "child": "root()", "dead": "insert_item(99,1)",
                     "NPC_all_2012_EnchantEvent_b_2_n": "child()"}
        dialogs = {"root": {"Menu": [{"Trigger": "NPC_all_2012_EnchantEvent_b_2()"}]}}
        called = rr.reachable(functions, dialogs, ["root()"])
        self.assertIn("child", called)
        self.assertNotIn("dead", called)

    def test_typed_arguments_do_not_protect_other_families_or_coordinates(self):
        refs = rr.typed_lua("insert_item(42,99); add_npc(5,400,145017,1,48000); add_state(13005,1) -- insert_item(7,1)")
        self.assertEqual({42}, refs["ItemResources"])
        self.assertEqual({5}, refs["NpcResources"])
        self.assertEqual({13005}, refs["StateResources"])
        self.assertEqual(set(), refs["StatResources"])

    def test_monster_selector_is_not_spawn_and_spawn_uses_second_argument(self):
        with tempfile.TemporaryDirectory() as directory:
            path = Path(directory) / "trigger.lua"
            path.write_text("\tif monster_id == 10 then\n respawn_near_monster(monster_handle,20,2)\n"
                            "\telseif monster_id == 30 then\n add_state(40,1)\n", encoding="utf-8")
            refs = rr.monster_references(path, {10})
            self.assertEqual({20}, refs["MonsterResources"])
            self.assertEqual(set(), refs["StateResources"])

    def test_each_audited_literal_matches_reachable_typed_decision(self):
        rows = json.loads((ROOT / "docs/packet-specs/audit-litteraux-73.json").read_text(encoding="utf-8"))
        self.assertEqual(134, len(rows))
        self.assertEqual(35, sum(row["keep"] for row in rows))
        catalog = json.loads((ROOT / "DevConsole/npc-dialogs.73.json").read_text(encoding="utf-8-sig"))
        refs, _ = rr.references(ROOT, set(map(int, catalog["NpcDialogCatalog"]["Npcs"])))
        for row in rows:
            with self.subTest(table=row["table"], id=row["id"]):
                self.assertTrue(row["evidence"])
                self.assertEqual(row["keep"], row["id"] in refs[row["table"]])


if __name__ == "__main__":
    unittest.main()
