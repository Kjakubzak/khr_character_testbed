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

    def test_shared_targets_and_vendor_metadata_are_preserved(self):
        root = self.asset()
        protected = copy.deepcopy((root["animations"], root["extensions"]["VRMC_vrm"]))
        normalize.normalize_json(root)
        self.assertEqual(protected, (root["animations"], root["extensions"]["VRMC_vrm"]))

    def test_visibility_role_becomes_roles_and_hierarchy_is_kept(self):
        root = self.asset()
        normalize.normalize_json(root)
        self.assertEqual(root["nodes"], [
            {"extensions": {normalize.NODE_HINT: {"roles": ["third_person"]}}, "children": [1]},
            {"extensions": {normalize.NODE_HINT: {"roles": ["always"]}}}])
        self.assertEqual(root["extensionsUsed"], [normalize.NODE_HINT])

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


class SpecMigrationTests(unittest.TestCase):
    """Rules for the 314c95c5 wire (glTF PR #2512 at 7ab5368a)."""

    def test_hint_role_spellings_labels_and_key_order(self):
        hint = {"role": "third_person_only", "label": "Head"}
        self.assertTrue(normalize.normalize_visibility_hint(hint, "hint"))
        self.assertEqual(list(hint.items()), [("roles", ["third_person"]), ("label", "Head")])
        self.assertFalse(normalize.normalize_visibility_hint(hint, "hint"))
        custom = {"roles": ["ACME_mirror_only", "first_person_only", "first_person"]}
        normalize.normalize_visibility_hint(custom, "hint")
        self.assertEqual(custom["roles"], ["ACME_mirror_only", "first_person"])

    def test_conflicting_role_and_roles_is_rejected(self):
        with self.assertRaisesRegex(ValueError, "differs"):
            normalize.normalize_visibility_hint({"role": "always", "roles": ["first_person"]}, "hint")

    def test_hints_never_required_and_empty_required_removed(self):
        root = {"extensionsUsed": [normalize.NODE_HINT, normalize.CAMERA_HINT, "KHR_node_lookat_target"],
                "extensionsRequired": [normalize.NODE_HINT, normalize.CAMERA_HINT, normalize.PRIMITIVE_HINT],
                "nodes": [{"extensions": {normalize.NODE_HINT: {"roles": ["always"]}}}]}
        self.assertTrue(normalize.normalize_json(root))
        self.assertNotIn("extensionsRequired", root)
        self.assertEqual(root["extensionsUsed"], [normalize.NODE_HINT, normalize.CAMERA_HINT, "KHR_node_lookat_target"])
        root["extensionsRequired"] = ["KHR_node_lookat_target", normalize.CAMERA_HINT]
        normalize.normalize_json(root)
        self.assertEqual(root["extensionsRequired"], ["KHR_node_lookat_target"])

    def test_annotations_become_primitive_hints_and_retired_names_go(self):
        def primitive(visibility):
            return {"attributes": {}, "extensions": {normalize.LEGACY_ANNOTATION: {"extensions": {
                normalize.LEGACY_RENDERVIEW: {"renderVisibility": visibility}}}}}
        root = {"extensionsUsed": [normalize.LEGACY_ANNOTATION, normalize.LEGACY_RENDERVIEW,
                                   normalize.LEGACY_VIRTUAL_TRANSFORMS, "VRMC_vrm"],
                "extensionsRequired": [normalize.LEGACY_VIRTUAL_TRANSFORMS],
                "meshes": [{"primitives": [primitive("thirdPersonOnly"), primitive("always"),
                                           primitive("firstPersonOnly"), primitive("auto")]}],
                "extensions": {normalize.LEGACY_VIRTUAL_TRANSFORMS: {"virtualTransforms": []}, "VRMC_vrm": {}}}
        self.assertTrue(normalize.normalize_json(root))
        primitives = root["meshes"][0]["primitives"]
        self.assertEqual(primitives[0], {"attributes": {}, "extensions": {normalize.PRIMITIVE_HINT: {"roles": ["third_person"]}}})
        self.assertEqual(primitives[1], {"attributes": {}})
        self.assertEqual(primitives[2]["extensions"], {normalize.PRIMITIVE_HINT: {"roles": ["first_person"]}})
        self.assertEqual(primitives[3], {"attributes": {}})
        self.assertEqual(root["extensionsUsed"], ["VRMC_vrm", normalize.PRIMITIVE_HINT])
        self.assertNotIn("extensionsRequired", root)
        self.assertEqual(root["extensions"], {"VRMC_vrm": {}})
        expected = copy.deepcopy(root)
        self.assertFalse(normalize.normalize_json(root))
        self.assertEqual(expected, root)

    def test_existing_primitive_hint_wins_over_annotation(self):
        root = {"extensionsUsed": [normalize.PRIMITIVE_HINT, normalize.LEGACY_ANNOTATION],
                "meshes": [{"primitives": [{"extensions": {
                    normalize.PRIMITIVE_HINT: {"roles": ["always"]},
                    normalize.LEGACY_ANNOTATION: {"extensions": {normalize.LEGACY_RENDERVIEW: {"renderVisibility": "thirdPersonOnly"}}}}}]}]}
        normalize.normalize_json(root)
        self.assertEqual(root["meshes"][0]["primitives"][0]["extensions"], {normalize.PRIMITIVE_HINT: {"roles": ["always"]}})

    def test_suppression_threshold_and_names(self):
        root = {"extensions": {normalize.EXPRESSION: {"expressions": [
            {"name": "a", "animation": 0, "extensions": {normalize.SUPPRESSION: {"suppressions": [
                {"target": 1, "name": "own label", "type": "blend", "threshold": 0.5},
                {"target": 1, "type": "block", "threshold": 0.5}]}}},
            {"name": "b", "animation": 1, "extensions": {normalize.LEGACY_MASK: {"masks": [
                {"target": "a", "name": "a", "amount": 0.5, "threshold": 0.1}]}}}]}}}
        normalize.normalize_json(root)
        expressions = root["extensions"][normalize.EXPRESSION]["expressions"]
        self.assertEqual(expressions[0]["extensions"][normalize.SUPPRESSION]["suppressions"],
                         [{"target": 1, "name": "own label", "type": "blend"}, {"target": 1, "type": "block", "threshold": 0.5}])
        self.assertEqual(expressions[1]["extensions"], {normalize.SUPPRESSION: {"suppressions": [{"target": 0, "amount": 0.5}]}})

    def test_string_and_float_indices_become_integers(self):
        root = {"nodes": [{"extensions": {normalize.CAMERA_HINT: {"role": "portrait", "targetNode": "0", "camera": 0.0}}}],
                "extensions": {normalize.EXPRESSION: {"expressions": [
                    {"name": "a", "animation": "0", "extensions": {normalize.JOINT: {"channels": ["0", 1.0]}}},
                    {"name": "b", "animation": 1, "extensions": {normalize.SUPPRESSION: {"suppressions": [{"target": "0"}]}}}]},
                    normalize.MAPPING: {"expressionSetMappings": {"demo_v1": {"A": [{"source": "1", "weight": 1}]}}},
                    normalize.SKELETON_MAPPING: {"skeletalRigMappings": {"vrm1_humanoid": {"hips": {"node": "0"}}}}}}
        self.assertTrue(normalize.normalize_json(root))
        extensions = root["extensions"]
        expression = extensions[normalize.EXPRESSION]["expressions"][0]
        self.assertEqual((expression["animation"], expression["extensions"][normalize.JOINT]["channels"]), (0, [0, 1]))
        self.assertEqual(extensions[normalize.EXPRESSION]["expressions"][1]["extensions"][normalize.SUPPRESSION]["suppressions"][0]["target"], 0)
        self.assertEqual(extensions[normalize.MAPPING]["expressionSetMappings"]["demo_v1"]["A"][0]["source"], 1)
        self.assertEqual(extensions[normalize.SKELETON_MAPPING]["skeletalRigMappings"]["vrm1_humanoid"]["hips"]["node"], 0)
        self.assertEqual(root["nodes"][0]["extensions"][normalize.CAMERA_HINT], {"role": "portrait", "targetNode": 0, "camera": 0})
        self.assertFalse(normalize.normalize_json(root))

    def test_non_integer_index_is_rejected(self):
        root = {"extensions": {normalize.EXPRESSION: {"expressions": [{"name": "a", "animation": "walk"}]}}}
        with self.assertRaisesRegex(ValueError, "not an integer"):
            normalize.normalize_json(root)

    def test_mapping_weight_above_one_needs_a_decision(self):
        root = {"extensions": {normalize.EXPRESSION: {"expressions": [{"name": "a", "animation": 0}]},
                               normalize.MAPPING: {"expressionSetMappings": {"demo_v1": {"A": [{"source": 0, "weight": 1.5}]}}}}}
        with self.assertRaisesRegex(ValueError, "outside"):
            normalize.normalize_json(root)

    def test_reference_pose_without_type_and_lookat_examples(self):
        root = {"animations": [{"extensions": {normalize.REFERENCE_POSE: {}}}],
                "nodes": [{"extensions": {normalize.LOOKAT_TARGET: {"hint": "head_target"}}}]}
        normalize.normalize_json(root)
        self.assertEqual(root["animations"][0]["extensions"][normalize.REFERENCE_POSE], {"poseType": "t_pose"})
        self.assertEqual(root["nodes"][0]["extensions"][normalize.LOOKAT_TARGET], {"hint": "ACME_head_target"})


class GlbRewriteTests(unittest.TestCase):
    def glb(self, root, binary=b"\x01\x02\x03\x04\x05\x06\x07\x08"):
        encoded = normalize.json.dumps(root, indent=1).encode()
        encoded += b" " * (-len(encoded) % 4)
        body = normalize.struct.pack("<II", len(encoded), normalize.JSON_CHUNK) + encoded
        body += normalize.struct.pack("<II", len(binary), 0x004E4942) + binary
        return b"glTF" + normalize.struct.pack("<II", 2, 12 + len(body)) + body

    def test_only_the_json_chunk_changes_and_reruns_do_not_write(self):
        import tempfile
        binary = bytes(range(16))
        root = {"asset": {"version": "2.0"}, "extensionsUsed": [normalize.NODE_HINT],
                "extensionsRequired": [normalize.NODE_HINT], "buffers": [{"byteLength": 16}],
                "nodes": [{"extensions": {normalize.NODE_HINT: {"role": "first_person"}}}]}
        with tempfile.TemporaryDirectory() as directory:
            path = Path(directory) / "fixture.glb"
            path.write_bytes(self.glb(root, binary))
            self.assertTrue(normalize.update(path))
            data = path.read_bytes()
            self.assertEqual(normalize.struct.unpack_from("<I", data, 8)[0], len(data))
            json_length = normalize.struct.unpack_from("<I", data, 12)[0]
            self.assertEqual(json_length % 4, 0)
            self.assertEqual(data[-16:], binary)
            self.assertEqual(normalize.struct.unpack_from("<II", data, 20 + json_length), (16, 0x004E4942))
            written = path.stat().st_mtime_ns
            self.assertFalse(normalize.update(path))
            self.assertEqual(path.read_bytes(), data)
            self.assertEqual(path.stat().st_mtime_ns, written)


if __name__ == "__main__":
    unittest.main()
