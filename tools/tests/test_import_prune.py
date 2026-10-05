import sys
import unittest
from contextlib import ExitStack
from pathlib import Path
from unittest.mock import patch

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
import import_epic7 as importer


class ImportPruneTests(unittest.TestCase):
    def mocks(self, stack, events):
        for name in ("validate_client", "introspect", "import_table", "resolve_foreign_keys", "drop_staging", "psql"):
            def callback(*args, operation=name):
                events.append(operation)
                return ({}, {}, {}) if operation == "introspect" else []
            stack.enter_context(patch.object(importer, name, side_effect=callback))
        return stack.enter_context(patch.object(importer.prune_to_client73, "main",
                                                side_effect=lambda args: events.append(("prune", args)) or 0))

    def test_partial_import_prunes_after_resolving_and_cleanup(self):
        events = []
        with ExitStack() as stack:
            prune = self.mocks(stack, events)
            self.assertEqual(0, importer.main(["import", "--client-dir", "client", "SkillResources"]))
            prune.assert_called_once_with(["client", "--apply"])
        self.assertEqual("validate_client", events[0])
        self.assertEqual(1, events.count("import_table"))
        self.assertEqual(["resolve_foreign_keys", "drop_staging", ("prune", ["client", "--apply"])], events[-3:])

    def test_invalid_client_prevents_all_import_work(self):
        events = []
        with ExitStack() as stack:
            self.mocks(stack, events)
            stack.enter_context(patch.object(importer, "validate_client", side_effect=ValueError("bad client")))
            with self.assertRaises(ValueError):
                importer.main(["import", "--client-dir", "client"])
        self.assertEqual([], events)

    def test_filter_failure_is_an_import_failure(self):
        events = []
        with ExitStack() as stack:
            prune = self.mocks(stack, events)
            prune.side_effect = RuntimeError("filter failed")
            with self.assertRaises(RuntimeError):
                importer.main(["import", "--client-dir", "client", "StringResources"])

    def test_plan_neither_cleans_staging_nor_resolves_foreign_keys(self):
        events = []
        with ExitStack() as stack:
            prune = self.mocks(stack, events)
            self.assertEqual(0, importer.main(["import", "--client-dir", "client", "--plan"]))
            prune.assert_called_once_with(["client"])
        for operation in ("psql", "drop_staging", "resolve_foreign_keys"):
            self.assertNotIn(operation, events)


if __name__ == "__main__":
    unittest.main()
