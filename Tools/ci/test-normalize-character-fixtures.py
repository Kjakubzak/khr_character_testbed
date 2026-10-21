#!/usr/bin/env python3
"""Exercise migration decisions which can otherwise silently change authored drivers."""
import copy
import importlib.util
from pathlib import Path
import unittest

spec = importlib.util.spec_from_file_location("normalize", Path(__file__).with_name("normalize-character-fixtures.py"))
normalize = importlib.util.module_from_spec(spec)
spec.loader.exec_module(normalize)


class MappingMigrationTests(unittest.TestCase):
    def asset(self):
        return {"extensions": {normalize.CHARACTER: {}, normalize.EXPRESSION: {"expressions": [
            {"name": "smile", "animation": 0}, {"name": "jaw", "animation": 1}]}, normalize.MAPPING: {
            "expressionSetMappings": {"demoVocabulary": {"OldOutput": [{"source": 0, "weight": 0.1}]},
                                      "custom alias": {"X": [{"source": 1, "weight": 0.25}]}},
            "expressionSetInputMappings": {"demoVocabulary": {"ExternalSmile": [
                {"target": "smile", "weight": 0.75, "name": "independent", "extras": {"keep": True}},
                {"target": 1, "weight": 0.5}]}}},
            "VRMC_vrm": {"meta": {"authors": ["fixture author"], "commercialUsage": "personalNonProfit"}}},
            "nodes": [{"extensions": {"KHR_node_visibility_hint": {"role": "third_person"}}, "children": [1]},
                      {"extensions": {"KHR_node_visibility_hint": {"role": "always"}}}],
            "animations": [{"channels": [{"target": {"node": 1, "path": "rotation"}}]},
                           {"channels": [{"target": {"node": 1, "path": "rotation"}}]}]}

    def test_authored_input_operation_wins_without_matrix_inversion(self):
        root = self.asset()
        self.assertTrue(normalize.normalize_json(root))
        mapping = root["extensions"][normalize.MAPPING]
        self.assertNotIn("expressionSetInputMappings", mapping)
        self.assertEqual(mapping["expressionSetMappings"]["demo_v1"], {"ExternalSmile": [
            {"source": 0, "weight": 0.75, "name": "independent", "extras": {"keep": True}},
            {"source": 1, "weight": 0.5}]})
        self.assertEqual(mapping["expressionSetMappings"]["custom alias"], {"X": [{"source": 1, "weight": 0.25}]})

    def test_visibility_shared_targets_and_vendor_metadata_are_preserved(self):
        root = self.asset()
        protected = copy.deepcopy((root["nodes"], root["animations"], root["extensions"]["VRMC_vrm"]))
        normalize.normalize_json(root)
        self.assertEqual(protected, (root["nodes"], root["animations"], root["extensions"]["VRMC_vrm"]))

    def test_completed_migration_is_idempotent(self):
        root = self.asset()
        normalize.normalize_json(root)
        expected = copy.deepcopy(root)
        self.assertFalse(normalize.normalize_json(root))
        self.assertEqual(expected, root)

    def test_ambiguous_legacy_name_is_not_guessed(self):
        root = self.asset()
        root["extensions"][normalize.EXPRESSION]["expressions"][1]["name"] = "smile"
        with self.assertRaisesRegex(ValueError, "ambiguous"):
            normalize.normalize_json(root)

    def test_duplicate_expression_labels_with_numeric_references_are_valid(self):
        root = self.asset()
        root["extensions"][normalize.EXPRESSION]["expressions"][1]["name"] = "smile"
        root["extensions"][normalize.MAPPING]["expressionSetInputMappings"]["demoVocabulary"]["ExternalSmile"][0]["target"] = 0
        normalize.normalize_json(root)
        self.assertEqual(0, root["extensions"][normalize.MAPPING]["expressionSetMappings"]["demo_v1"]["ExternalSmile"][0]["source"])


if __name__ == "__main__":
    unittest.main()
