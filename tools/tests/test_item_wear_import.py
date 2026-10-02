import csv
import itertools
import pathlib
import re
import sys
import os
import io
import contextlib
import subprocess
import tempfile
import unittest
from unittest.mock import patch

sys.path.insert(0, str(pathlib.Path(__file__).resolve().parents[1]))
import import_epic7 as importer
import generate_item_wear_restrictions as generator


class ItemWearImportTests(unittest.TestCase):
    def test_all_flag_combinations_use_repository_enum_bits(self):
        limits = importer.ITEM_RACE_LIMITS + importer.ITEM_JOB_LIMITS
        for flags in itertools.product(("0", "1"), repeat=7):
            row = {column: flag for (column, _), flag in zip(limits, flags)}
            race = importer.whitelist_mask(row, importer.ITEM_RACE_LIMITS)
            job = importer.whitelist_mask(row, importer.ITEM_JOB_LIMITS)
            self.assertEqual(race, sum(bit for (column, bit), flag in zip(limits[:3], flags[:3]) if flag == "1"))
            self.assertEqual(job, sum(bit for (column, bit), flag in zip(limits[3:], flags[3:]) if flag == "1"))

    def test_invalid_source_flags_do_not_silently_allow_everyone(self):
        row = {column: "0" for column, _ in importer.ITEM_RACE_LIMITS}
        row["limit_deva"] = "2"
        with self.assertRaises(ValueError):
            importer.whitelist_mask(row, importer.ITEM_RACE_LIMITS)

    def test_import_plan_consumes_all_seven_limit_columns_and_depth(self):
        headers = [column for column, _ in importer.ITEM_RACE_LIMITS + importer.ITEM_JOB_LIMITS] + ["job_depth"]
        with tempfile.TemporaryDirectory() as directory:
            data = pathlib.Path(directory)
            (data / "ItemResource.csv").write_text(",".join(headers) + "\n", encoding="utf-8")
            columns = {"ItemResources": [("RaceRestriction", "integer", "int4", False),
                                          ("JobRestriction", "integer", "int4", False),
                                          ("JobDepth", "smallint", "int2", False)]}
            overrides = next(overrides for table, _, _, overrides in importer.TABLES if table == "ItemResources")
            with patch.object(importer, "DATA", data):
                _, mapped, _, missing, unused = importer.plan("ItemResources", "ItemResource", overrides, columns, {})
            self.assertFalse(missing); self.assertFalse(unused)
            self.assertIn('s."limit_deva"', mapped["RaceRestriction"])
            self.assertIn('s."limit_summoner"', mapped["JobRestriction"])
            self.assertIn('s."job_depth"', mapped["JobDepth"])

    def test_missing_flags_abort_the_import_plan(self):
        with tempfile.TemporaryDirectory() as directory:
            data = pathlib.Path(directory)
            (data / "ItemResource.csv").write_text("id,limit_deva\n", encoding="utf-8")
            columns = {"ItemResources": [("RaceRestriction", "integer", "int4", False)]}
            with patch.object(importer, "DATA", data), self.assertRaises(SystemExit):
                importer.plan("ItemResources", "ItemResource", {"RaceRestriction": importer.ITEM_RACE_LIMITS}, columns, {})

    def test_plan_mode_never_drops_staging_tables(self):
        with patch.object(importer, "introspect", return_value=({}, {}, {})), \
                patch.object(importer, "import_table", return_value=[]), patch.object(importer, "psql") as sql:
            importer.main(["import_epic7.py", "--plan", "ItemResources"])
            sql.assert_not_called()

    def test_embedded_migration_matches_every_csv_row(self):
        source = importer.DATA / "ItemResource.csv"
        if not source.exists():
            self.skipTest("Local Epic 7 CSV dump unavailable")
        actual = generator.OUTPUT.read_text(encoding="utf-8")
        self.assertEqual(actual, generator.generate(source))
        groups = re.findall(r'SET "RaceRestriction" = (\d+), "JobRestriction" = (\d+), "JobDepth" = (\d+)\s+WHERE "Id" IN \((.*?)\);', actual, re.S)
        imported = {}
        for race, job, depth, codes in groups:
            for code in re.findall(r"\d+", codes):
                self.assertNotIn(int(code), imported)
                imported[int(code)] = (int(race), int(job), int(depth))
        with source.open(encoding="utf-8", newline="") as stream:
            expected = {int(row["id"]): (importer.whitelist_mask(row, importer.ITEM_RACE_LIMITS),
                         importer.whitelist_mask(row, importer.ITEM_JOB_LIMITS), int(row["job_depth"]))
                        for row in csv.DictReader(stream)}
        self.assertEqual(imported, expected)
        self.assertEqual(len(imported), 29647)

    @unittest.skipUnless(os.environ.get("NAVISLAMIA_ITEM_IMPORT_TEST_PORT"), "Requires disposable navis_equipment_test PostgreSQL")
    def test_postgres_import_and_backfill_match_all_source_rows(self):
        port = int(os.environ["NAVISLAMIA_ITEM_IMPORT_TEST_PORT"])
        self.assertGreater(port, 0); self.assertLessEqual(port, 65535)

        def sql(query, fetch=False):
            result = subprocess.run(["psql", "-h", "127.0.0.1", "-p", str(port), "-U", "navis_equipment_test",
                                     "-d", "navis_equipment_test", "-v", "ON_ERROR_STOP=1", "-q", "-t", "-A", "-F", "\t"],
                                    input=query, text=True, capture_output=True, encoding="utf-8")
            self.assertEqual(result.returncode, 0, result.stderr)
            return [line.split("\t") for line in result.stdout.splitlines() if line]

        with patch.object(importer, "psql", side_effect=sql):
            columns, foreign, primary = importer.introspect()
            definition = next(row for row in importer.TABLES if row[0] == "ItemResources")
            with contextlib.redirect_stdout(io.StringIO()):
                pending = importer.import_table(*definition, columns, foreign, primary, False)
            try:
                # Read all imported masks, then verify the deployment backfill independently too.
                with (importer.DATA / "ItemResource.csv").open(encoding="utf-8", newline="") as stream:
                    expected = {int(row["id"]): (importer.whitelist_mask(row, importer.ITEM_RACE_LIMITS),
                                importer.whitelist_mask(row, importer.ITEM_JOB_LIMITS), int(row["job_depth"]))
                                for row in csv.DictReader(stream)}

                def actual():
                    return {int(code): (int(race), int(job), int(depth)) for code, race, job, depth in sql(
                        'SELECT "Id", "RaceRestriction", "JobRestriction", "JobDepth" FROM "ItemResources"')
                            if int(code) in expected}

                self.assertEqual(actual(), expected)
                sql('UPDATE "ItemResources" t SET "RaceRestriction"=0, "JobRestriction"=0, "JobDepth"=0 '
                    'FROM epic7_itemresources_rows s WHERE t."Id"=s.id::bigint;')
                sql(generator.OUTPUT.read_text(encoding="utf-8"))
                self.assertEqual(actual(), expected)
                self.assertEqual(sql('SELECT "RaceRestriction", "JobRestriction", "JobDepth", "Price" '
                                     'FROM "ItemResources" WHERE "Id"=99999999;'), [["4", "8192", "8", "4242"]])
            finally:
                importer.drop_staging(pending)


if __name__ == "__main__":
    unittest.main()
