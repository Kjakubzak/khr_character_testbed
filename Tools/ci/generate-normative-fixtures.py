#!/usr/bin/env python3
"""Generate small CC0 integration fixtures without requiring a Unity Editor.

The .gltf/.bin sources are also packaged by SampleCharacterFactory in Unity, so
the golden/export gate and the checked-in integration assets use the same wire.
"""
import argparse
import json
from pathlib import Path
import struct

REPO = Path(__file__).resolve().parents[2]
NODE_HINT = "KHR_node_visibility_hint"
PRIMITIVE_HINT = "KHR_mesh_primitive_visibility_hint"


class Fixture:
    def __init__(self, name):
        self.name = name
        self.binary = bytearray()
        self.root = {"asset": {"version": "2.0", "generator": "testbed normative fixtures"},
                     "scene": 0, "scenes": [{"nodes": [0]}], "bufferViews": [], "accessors": []}

    def accessor(self, values, kind, component=5126, bounds=False, target=None):
        while len(self.binary) % 4:
            self.binary.append(0)
        width = {"SCALAR": 1, "VEC3": 3, "VEC4": 4}[kind]
        encoded = struct.pack("<" + ({5126: "f", 5123: "H", 5121: "B"}[component]) * len(values), *values)
        view = {"buffer": 0, "byteOffset": len(self.binary), "byteLength": len(encoded)}
        if target:
            view["target"] = target
        self.binary.extend(encoded)
        self.root["bufferViews"].append(view)
        accessor = {"bufferView": len(self.root["bufferViews"]) - 1, "componentType": component,
                    "count": len(values) // width, "type": kind}
        if bounds:
            accessor["min"] = [min(values[j::width]) for j in range(width)]
            accessor["max"] = [max(values[j::width]) for j in range(width)]
        self.root["accessors"].append(accessor)
        return len(self.root["accessors"]) - 1

    def geometry(self, morph=False):
        positions = self.accessor([-0.5, -0.5, 0, 0.5, -0.5, 0, -0.5, 0.5, 0,
                                   0.7, -0.5, 0, 1.7, -0.5, 0, 0.7, 0.5, 0], "VEC3", bounds=True, target=34962)
        normals = self.accessor([0, 0, 1] * 6, "VEC3", target=34962)
        first = self.accessor([0, 1, 2], "SCALAR", 5123, target=34963)
        second = self.accessor([3, 4, 5], "SCALAR", 5123, target=34963)
        primitives = [{"attributes": {"POSITION": positions, "NORMAL": normals}, "indices": first, "material": 0},
                      {"attributes": {"POSITION": positions, "NORMAL": normals}, "indices": second, "material": 0}]
        if morph:
            delta = self.accessor([0.1, 0, 0] * 6, "VEC3", bounds=True, target=34962)
            for primitive in primitives:
                primitive["targets"] = [{"POSITION": delta}]
        else:
            primitives[0]["extensions"] = {PRIMITIVE_HINT: {"role": "always"}}
            primitives[1]["extensions"] = {PRIMITIVE_HINT: {"role": "first_person"}}
        self.root["materials"] = [{"name": "FixtureRed", "doubleSided": True,
                                   "pbrMetallicRoughness": {"baseColorFactor": [1, 0, 0, 1], "metallicFactor": 0, "roughnessFactor": 1}}]
        self.root["meshes"] = [{"name": "SharedFixtureMesh", "primitives": primitives}]
        if morph:
            self.root["meshes"][0].update({"weights": [0.3], "extras": {"targetNames": ["shift"]}})

    def bytes(self):
        self.root["buffers"] = [{"byteLength": len(self.binary)}]
        encoded = json.dumps(self.root, separators=(",", ":"), ensure_ascii=False).encode()
        encoded += b" " * (-len(encoded) % 4)
        binary = bytes(self.binary) + b"\0" * (-len(self.binary) % 4)
        body = struct.pack("<II", len(encoded), 0x4E4F534A) + encoded
        body += struct.pack("<II", len(binary), 0x004E4942) + binary
        return b"glTF" + struct.pack("<II", 2, 12 + len(body)) + body

    def outputs(self):
        glb = self.bytes()
        source = dict(self.root)
        source["buffers"] = [{"byteLength": len(self.binary), "uri": self.name + ".bin"}]
        return {REPO / "Assets/SampleAssets/Synthetic" / (self.name + ".glb"): glb,
                REPO / "Tools/fixtures" / (self.name + ".gltf"): (json.dumps(source, indent=2) + "\n").encode(),
                REPO / "Tools/fixtures" / (self.name + ".bin"): bytes(self.binary)}


def hint(role):
    return {NODE_HINT: {"role": role}}


def ancestors():
    f = Fixture("VH-Ancestors")
    f.geometry()
    f.root["extensionsUsed"] = [NODE_HINT, PRIMITIVE_HINT, "KHR_node_visibility", "KHR_animation_pointer"]
    f.root["nodes"] = [
        {"name": "FixtureRoot", "children": [1, 5, 7, 9, 11]},
        {"name": "ThirdParent", "children": [2, 4, 14], "extensions": hint("third_person")},
        {"name": "AlwaysChild", "mesh": 0, "children": [3], "extensions": hint("always")},
        {"name": "FirstChild", "mesh": 0, "translation": [3, 0, 0], "extensions": hint("first_person")},
        {"name": "NestedCamera", "camera": 0, "translation": [0, 0, 3]},
        {"name": "UnknownParent", "children": [6], "extensions": hint("ACME_unknown")},
        {"name": "UnknownChild", "mesh": 0, "translation": [-3, 0, 0], "extensions": hint("always")},
        {"name": "CustomParent", "children": [8], "extensions": hint("ACME_mirror_only")},
        {"name": "CustomChild", "mesh": 0, "translation": [0, 3, 0], "extensions": hint("always")},
        {"name": "CoreParent", "children": [10, 13], "extensions": {"KHR_node_visibility": {"visible": False}}},
        {"name": "CoreChild", "mesh": 0, "extensions": {**hint("always"), "KHR_node_visibility": {"visible": True}}},
        {"name": "UnannotatedBranch", "children": [12]},
        {"name": "FreeChild", "mesh": 0, "translation": [0, -3, 0]},
        {"name": "CoreCamera", "camera": 0, "translation": [0, 0, 3]},
        {"name": "UnknownUnderThird", "children": [15], "extensions": hint("ACME_unknown")},
        {"name": "FallbackUnderThird", "mesh": 0, "extensions": hint("always")},
    ]
    f.root["cameras"] = [{"name": "NestedCameraProjection", "type": "perspective",
                           "perspective": {"yfov": 0.7, "znear": 0.1, "zfar": 100}}]
    times = f.accessor([0, 1], "SCALAR", bounds=True)
    translations = f.accessor([0, 0, 0, 0.5, 0, 0], "VEC3")
    visibility = f.accessor([0, 1], "SCALAR", 5121)
    f.root["animations"] = [
        {"name": "AncestorMotion", "channels": [{"sampler": 0, "target": {"node": 1, "path": "translation"}}],
         "samplers": [{"input": times, "output": translations, "interpolation": "LINEAR"}]},
        {"name": "CoreVisibilityMotion", "channels": [{"sampler": 0, "target": {"path": "pointer", "extensions": {
            "KHR_animation_pointer": {"pointer": "/nodes/9/extensions/KHR_node_visibility/visible"}}}}],
         "samplers": [{"input": times, "output": visibility, "interpolation": "STEP"}]},
    ]
    return f


def shared_targets():
    f = Fixture("SC-SharedTargets")
    f.geometry(morph=True)
    f.root["nodes"] = [{"name": "FixtureRoot", "children": [1]},
                       {"name": "SharedTarget", "mesh": 0, "translation": [1, 0, 0], "scale": [1, 1, 1]}]
    f.root["extensionsUsed"] = ["KHR_character", "KHR_character_expression", "KHR_character_expression_joint",
                                "KHR_character_expression_morphtarget", "KHR_character_expression_mapping"]
    times = f.accessor([0, 1], "SCALAR", bounds=True)
    animations = []
    for index in range(2):
        angle = (index + 1) * 0.7853981633974483
        from math import sin, cos
        outputs = [f.accessor([1.2, 0, 0, 1.5 + index * 0.1, 0, 0], "VEC3"),
                   f.accessor([0, 0, 0, 1, 0, 0, sin(angle), cos(angle)], "VEC4"),
                   f.accessor([1, 1, 1, 2 + index, 2 + index, 2 + index], "VEC3"),
                   f.accessor([0.4, 0.6 + index * 0.1], "SCALAR")]
        animations.append({"name": "Response" + str(index),
                           "channels": [{"sampler": j, "target": {"node": 1, "path": path}}
                                        for j, path in enumerate(["translation", "rotation", "scale", "weights"])],
                           "samplers": [{"input": times, "output": output, "interpolation": "LINEAR"} for output in outputs]})
    f.root["animations"] = animations
    f.root["extensions"] = {"KHR_character": {}, "KHR_character_expression": {"expressions": [
        {"name": "response" + str(index), "animation": index, "extensions": {
            "KHR_character_expression_joint": {"channels": [0, 1, 2]},
            "KHR_character_expression_morphtarget": {"channels": [3]}}} for index in range(2)]},
        "KHR_character_expression_mapping": {"expressionSetMappings": {"demo_v1": {
            "Together": [{"source": 0, "name": "independent label", "weight": 1}, {"source": 1, "weight": 1}],
            "Half": [{"source": 0, "weight": 0.5}]}}}}
    return f


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--check", action="store_true")
    args = parser.parse_args()
    mismatch = []
    for fixture in [ancestors(), shared_targets()]:
        for path, data in fixture.outputs().items():
            if args.check:
                if not path.exists() or path.read_bytes() != data:
                    mismatch.append(str(path))
            else:
                path.parent.mkdir(parents=True, exist_ok=True)
                path.write_bytes(data)
                print(path.relative_to(REPO))
    if mismatch:
        raise SystemExit("Generated fixtures differ: " + ", ".join(mismatch))


if __name__ == "__main__":
    main()
