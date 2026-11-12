#!/usr/bin/env python3
"""Unit tests for validate-character-json.py. Needs a glTF spec checkout in $GLTF_SPEC (see that script)."""
import importlib.util
import os
from pathlib import Path
import unittest

spec = importlib.util.spec_from_file_location("validator", Path(__file__).with_name("validate-character-json.py"))
validator = importlib.util.module_from_spec(spec)
spec.loader.exec_module(validator)
SPEC_ROOT = os.environ.get("GLTF_SPEC")


@unittest.skipUnless(SPEC_ROOT, "set GLTF_SPEC to a glTF checkout with specification/ and extensions/")
class VisibilityHintRoleTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.schemas = validator.Schemas(SPEC_ROOT)

    def validate(self, hint):
        root = {"asset": {"version": "2.0"}, "extensionsUsed": ["KHR_node_visibility_hint"],
                "nodes": [{"name": "Head", "extensions": {"KHR_node_visibility_hint": hint}}]}
        errors, warnings = validator.validate(self.schemas, root)
        return sorted({code for code, _ in errors}), warnings

    def test_roles_alone_is_valid(self):
        self.assertEqual(self.validate({"roles": ["third_person"]}), ([], []))

    def test_legacy_role_beside_roles_is_only_a_warning(self):
        codes, warnings = self.validate({"roles": ["third_person"], "role": "third_person"})
        self.assertEqual(codes, [])
        self.assertTrue(any("legacy 'role'" in warning for warning in warnings), warnings)

    def test_legacy_role_without_roles_is_an_error(self):
        codes, _ = self.validate({"role": "third_person"})
        self.assertEqual(codes, ["HINT_WITHOUT_ROLES", "SCHEMA"])

    def test_always_with_other_roles_is_a_warning(self):
        codes, warnings = self.validate({"roles": ["always", "first_person"]})
        self.assertEqual(codes, [])
        self.assertTrue(any("'always'" in warning for warning in warnings), warnings)


if __name__ == "__main__":
    unittest.main()
