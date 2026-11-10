#!/usr/bin/env python3
"""Generate the shared KHR_character conformance fixtures under Tools/fixtures/conformance/.

Usage: python3 Tools/ci/generate-conformance-fixtures.py [--check]

Each fixture is one self-contained .gltf (buffers and images embedded as data URIs) plus an
expected.json that states the inputs and the outputs the normative draft at glTF-private 314c95c5
(KhronosGroup/glTF PR #2512 at 7ab5368a) requires (MUST) or recommends (SHOULD). The expected values
are written out by hand below from the spec text; Tools/ci/conformance_reference.py recomputes them
from the .gltf files independently. Fixtures in invalid/ must be rejected by a validator for the
reason listed in invalid/expected.json. Everything here is procedurally generated and CC0-1.0.

--check writes nothing and fails when a committed file differs from the generated one.
"""

import argparse
import base64
import json
import math
import pathlib
import struct
import sys
import zlib

REPO = pathlib.Path(__file__).resolve().parents[2]
OUT = REPO / "Tools" / "fixtures" / "conformance"
SPEC = {
    "repository": "Kjakubzak/glTF-private",
    "branch": "avatar_ext_normative",
    "commit": "314c95c571185776f8624ceb94773ce3b976ad91",
    "public": "KhronosGroup/glTF#2512 at 7ab5368a87d1d303dff33ec5f9aad42752e84c78",
}
GENERATOR = "khr_character_testbed conformance fixtures (Tools/ci/generate-conformance-fixtures.py)"

NODE_HINT = "KHR_node_visibility_hint"
PRIMITIVE_HINT = "KHR_mesh_primitive_visibility_hint"
CAMERA_HINT = "KHR_node_camera_hint"
LOOKAT = "KHR_node_lookat_target"
CHARACTER = "KHR_character"
EXPRESSION = "KHR_character_expression"
JOINT = "KHR_character_expression_joint"
MORPH = "KHR_character_expression_morphtarget"
SUPPRESSION = "KHR_character_expression_suppression"
TEXTURE = "KHR_character_expression_texture_transform"
MAPPING = "KHR_character_expression_mapping"
POSE = "KHR_character_reference_pose"
POINTER = "KHR_animation_pointer"
TEXTURE_TRANSFORM = "KHR_texture_transform"
CORE_VISIBILITY = "KHR_node_visibility"

SIN5, COS5 = math.sin(math.radians(5)), math.cos(math.radians(5))
S45 = math.sqrt(0.5)
S225, C225 = math.sin(math.radians(22.5)), math.cos(math.radians(22.5))
FP_TP = ["first_person", "third_person"]


class Asset:
    """A small glTF document whose single buffer is embedded as a data URI."""

    def __init__(self, used):
        self.binary = bytearray()
        self.root = {"asset": {"version": "2.0", "generator": GENERATOR, "copyright": "CC0-1.0"},
                     "extensionsUsed": list(used), "scene": 0, "scenes": [{"nodes": [0]}], "nodes": [],
                     "bufferViews": [], "accessors": []}

    def view(self, data, target=None):
        while len(self.binary) % 4:
            self.binary.append(0)
        view = {"buffer": 0, "byteOffset": len(self.binary), "byteLength": len(data)}
        if target:
            view["target"] = target
        self.binary.extend(data)
        self.root["bufferViews"].append(view)
        return len(self.root["bufferViews"]) - 1

    def accessor(self, values, kind, component=5126, bounds=False, target=None):
        width = {"SCALAR": 1, "VEC2": 2, "VEC3": 3, "VEC4": 4}[kind]
        code = {5126: "f", 5123: "H", 5121: "B"}[component]
        view = self.view(struct.pack("<" + code * len(values), *values), target)
        accessor = {"bufferView": view, "componentType": component, "count": len(values) // width, "type": kind}
        if bounds:
            accessor["min"] = [min(values[j::width]) for j in range(width)]
            accessor["max"] = [max(values[j::width]) for j in range(width)]
        self.root["accessors"].append(accessor)
        return len(self.root["accessors"]) - 1

    def node(self, name, **properties):
        self.root["nodes"].append({"name": name, **properties})
        return len(self.root["nodes"]) - 1

    def mesh(self, name, primitive_count=1, morph=False):
        positions = self.accessor([0, 0, 0, 0.2, 0, 0, 0, 0.2, 0], "VEC3", bounds=True, target=34962)
        primitives = []
        for _ in range(primitive_count):
            primitive = {"attributes": {"POSITION": positions}, "mode": 4}
            if morph:
                delta = self.accessor([0, 0, 0.1] * 3, "VEC3", bounds=True, target=34962)
                primitive["targets"] = [{"POSITION": delta}]
            primitives.append(primitive)
        self.root.setdefault("meshes", []).append({"name": name, "primitives": primitives})
        return len(self.root["meshes"]) - 1

    def animation(self, name, channels, extensions=None):
        """channels: list of (target, times, values, accessor type, interpolation, component)."""
        animation = {"name": name, "channels": [], "samplers": []}
        for target, times, values, kind, interpolation, component in channels:
            sampler = {"input": self.accessor(times, "SCALAR", bounds=True),
                       "output": self.accessor(values, kind, component), "interpolation": interpolation}
            animation["samplers"].append(sampler)
            animation["channels"].append({"sampler": len(animation["samplers"]) - 1, "target": target})
        if extensions:
            animation["extensions"] = extensions
        self.root.setdefault("animations", []).append(animation)
        return len(self.root["animations"]) - 1

    def finish(self):
        if not self.binary:  # core glTF: these arrays need at least one item, a buffer at least one byte
            for key in ("bufferViews", "accessors"):
                del self.root[key]
            return self.root
        while len(self.binary) % 4:
            self.binary.append(0)
        uri = "data:application/octet-stream;base64," + base64.b64encode(bytes(self.binary)).decode("ascii")
        self.root["buffers"] = [{"byteLength": len(self.binary), "uri": uri}]
        return self.root


def node_target(node, path):
    return {"node": node, "path": path}


def pointer_target(pointer):
    return {"path": "pointer", "extensions": {POINTER: {"pointer": pointer}}}


def hint(*roles, label=None):
    value = {"roles": list(roles)}
    if label:
        value["label"] = label
    return {NODE_HINT: value}


def case(name, level, section, given, expected, **extra):
    return {"name": name, "level": level, "spec_section": section, "input": given, "expected": expected, **extra}


def host(logic, applies):
    return {"logic": logic, "applies": applies}


def expected_file(fixture, kind, summary, cases, notes=None):
    document = {"fixture": fixture, "asset": fixture + ".gltf", "license": "CC0-1.0", "spec": SPEC,
                "kind": kind, "summary": summary, "cases": cases}
    if notes:
        document["notes"] = notes
    return document


# ── Visibility ────────────────────────────────────────────────────────────────────────────────

VH_SECTION = "KHR_node_visibility_hint: Roles, Combining hints"


def vh_multi_role():
    a = Asset([NODE_HINT])
    mesh = a.mesh("Quad")
    a.node("Root", children=[1, 2])
    a.node("SelfStatus", mesh=mesh, extensions=hint("first_person", "ACME_mirror_only", label="Self status"))
    a.node("Head", mesh=mesh, translation=[0, 1, 0], extensions=hint("third_person"))
    nodes = lambda status, head: {"nodes": {"SelfStatus": status, "Head": head}}
    cases = [
        case("no logic (inspector): every standard role applies", "MUST", VH_SECTION,
             {"host": host([], [])}, nodes(True, True)),
        case("first/third-person logic, own eye view", "MUST", VH_SECTION,
             {"host": host(FP_TP, ["first_person"])}, nodes(True, False)),
        case("first/third-person logic, spectator view; ACME_mirror_only unrecognized", "MUST", VH_SECTION,
             {"host": host(FP_TP, ["third_person"])}, nodes(False, True)),
        case("first/third-person and ACME_mirror_only logic, mirror view", "MUST", VH_SECTION,
             {"host": host(FP_TP + ["ACME_mirror_only"], ["third_person", "ACME_mirror_only"])}, nodes(True, True)),
        case("first/third-person and ACME_mirror_only logic, spectator view", "MUST", VH_SECTION,
             {"host": host(FP_TP + ["ACME_mirror_only"], ["third_person"])}, nodes(False, True)),
        case("logic only for first_person: third_person has no logic, so it applies in every view", "MUST",
             VH_SECTION, {"host": host(["first_person"], ["first_person"])}, nodes(True, True)),
    ]
    return a.finish(), expected_file("VH-MultiRole", "visibility",
        "Roles in one hint are alternatives (OR); custom roles count only when the host recognizes them.", cases)


def vh_unknown_custom():
    a = Asset([NODE_HINT])
    mesh = a.mesh("Quad")
    a.node("Root", children=[1, 3])
    a.node("UnknownParent", children=[2], extensions=hint("ACME_unknown"))
    a.node("UnknownChild", mesh=mesh)
    a.node("UnknownOrFirst", mesh=mesh, translation=[1, 0, 0], extensions=hint("ACME_unknown", "first_person"))
    nodes = lambda child, alternative: {"nodes": {"UnknownChild": child, "UnknownOrFirst": alternative}}
    cases = [
        case("no logic", "MUST", VH_SECTION, {"host": host([], [])}, nodes(True, True)),
        case("first/third-person logic, first-person view", "MUST", VH_SECTION,
             {"host": host(FP_TP, ["first_person"])}, nodes(True, True)),
        case("first/third-person logic, third-person view: only first_person is recognized", "MUST", VH_SECTION,
             {"host": host(FP_TP, ["third_person"])}, nodes(True, False)),
        case("host recognizing a different custom role", "MUST", VH_SECTION,
             {"host": host(FP_TP + ["ACME_mirror_only"], ["third_person", "ACME_mirror_only"])}, nodes(True, False)),
    ]
    return a.finish(), expected_file("VH-UnknownCustom", "visibility",
        "A hint with no recognized role imposes no restriction; unrecognized custom roles are ignored.", cases)


def vh_always_plus():
    a = Asset([NODE_HINT])
    mesh = a.mesh("Quad")
    a.node("Root", children=[1, 2])
    a.node("AlwaysOrFirst", mesh=mesh, extensions=hint("always", "first_person"))
    a.node("ThirdParent", children=[3], extensions=hint("third_person"))
    a.node("AlwaysChild", mesh=mesh, translation=[1, 0, 0], extensions=hint("always"))
    nodes = lambda plus, child: {"nodes": {"AlwaysOrFirst": plus, "AlwaysChild": child}}
    cases = [
        case("no logic", "MUST", VH_SECTION, {"host": host([], [])}, nodes(True, True)),
        case("first-person view: always cannot cancel the third_person ancestor", "MUST", VH_SECTION,
             {"host": host(FP_TP, ["first_person"])}, nodes(True, False)),
        case("third-person view", "MUST", VH_SECTION, {"host": host(FP_TP, ["third_person"])}, nodes(True, True)),
    ]
    return a.finish(), expected_file("VH-AlwaysPlus", "visibility",
        "A hint listing 'always' imposes no restriction; validators MAY warn about 'always' with other roles but "
        "MUST NOT reject the asset. An 'always' child cannot restore content its ancestor hides.", cases,
        notes=["A validator MAY report AlwaysOrFirst's roles; the asset is valid."])


def vh_compose():
    a = Asset([NODE_HINT, PRIMITIVE_HINT, CORE_VISIBILITY, POINTER])
    mesh = a.mesh("ThreeParts", primitive_count=3)
    primitives = a.root["meshes"][mesh]["primitives"]
    primitives[1]["extensions"] = {PRIMITIVE_HINT: {"roles": ["first_person"], "label": "FirstOnly"}}
    primitives[2]["extensions"] = {PRIMITIVE_HINT: {"roles": ["always"]}}
    a.node("Root", children=[1, 2, 4])
    a.node("ThirdBody", mesh=mesh, extensions=hint("third_person"))
    a.node("CoreParent", children=[3], extensions={CORE_VISIBILITY: {"visible": False}})
    a.node("CoreChild", mesh=mesh, translation=[1, 0, 0], extensions={CORE_VISIBILITY: {"visible": True}})
    a.node("FreeBody", mesh=mesh, translation=[-1, 0, 0])
    a.animation("CoreToggle", [(pointer_target("/nodes/2/extensions/KHR_node_visibility/visible"),
                                [0, 1], [0, 1], "SCALAR", "STEP", 5121)])

    def draw(third, core, free):
        return {"primitives": {**{f"ThirdBody#{i}": third[i] for i in range(3)},
                               **{f"CoreChild#{i}": core[i] for i in range(3)},
                               **{f"FreeBody#{i}": free[i] for i in range(3)}}}

    authored = {"animation": None}
    toggled = {"animation": "CoreToggle", "time": 1}
    cases = [
        case("no logic, authored state", "MUST", "KHR_mesh_primitive_visibility_hint: Primitive-instance composition",
             {"host": host([], []), "state": authored}, draw([1, 1, 1], [0, 0, 0], [1, 1, 1])),
        case("first-person view: the node hint hides every primitive; a primitive 'always' cannot restore", "MUST",
             "KHR_mesh_primitive_visibility_hint: Primitive-instance composition",
             {"host": host(FP_TP, ["first_person"]), "state": authored}, draw([0, 0, 0], [0, 0, 0], [1, 1, 1])),
        case("third-person view: primitive AND node", "MUST",
             "KHR_mesh_primitive_visibility_hint: Primitive-instance composition",
             {"host": host(FP_TP, ["third_person"]), "state": authored}, draw([1, 0, 1], [0, 0, 0], [1, 0, 1])),
        case("both roles apply", "MUST", "KHR_mesh_primitive_visibility_hint: Primitive-instance composition",
             {"host": host(FP_TP, FP_TP), "state": authored}, draw([1, 1, 1], [0, 0, 0], [1, 1, 1])),
        case("KHR_node_visibility on the parent animated to true (STEP pointer at t=1)", "MUST",
             "KHR_node_visibility_hint: Combining hints (coreVisible)",
             {"host": host(FP_TP, ["third_person"]), "state": toggled}, draw([1, 0, 1], [1, 0, 1], [1, 0, 1])),
    ]
    for item in cases:
        item["expected"]["primitives"] = {key: bool(value) for key, value in item["expected"]["primitives"].items()}
    return a.finish(), expected_file("VH-Compose", "visibility",
        "renderPrimitiveInstance = primitive hint AND ancestor-inclusive node hints AND KHR_node_visibility; "
        "a child's visible:true does not restore a hidden parent.", cases)


# ── Suppression ───────────────────────────────────────────────────────────────────────────────

def expression_asset(names, extra_used=()):
    """One helper node per expression, each moved by its own translation channel (a joint channel)."""
    a = Asset([CHARACTER, EXPRESSION, JOINT, *extra_used])
    a.node("Root", children=list(range(1, len(names) + 1)))
    entries = []
    for index, name in enumerate(names):
        node = a.node(name)
        animation = a.animation(name, [(node_target(node, "translation"), [0, 1], [0, 0, 0, 0, 1, 0],
                                        "VEC3", "LINEAR", 5126)])
        entries.append({"name": name, "animation": animation, "extensions": {JOINT: {"channels": [0]}}})
    a.root["extensions"] = {CHARACTER: {}, EXPRESSION: {"expressions": entries}}
    return a, entries


SUP_SECTION = "KHR_character_expression_suppression: Suppression and aggregation"


def sup_overlap():
    a, entries = expression_asset(["A", "B", "T", "C"], [SUPPRESSION])
    entries[0]["extensions"][SUPPRESSION] = {"suppressions": [{"target": 2, "type": "blend", "amount": 0.4}]}
    entries[1]["extensions"][SUPPRESSION] = {"suppressions": [{"target": 2, "type": "blend", "amount": 0.5}]}
    entries[3]["extensions"][SUPPRESSION] = {"suppressions": [{"target": 2, "type": "block", "amount": 1,
                                                               "threshold": 0.5}]}
    drivers = lambda **values: {"drivers": values}
    cases = [
        case("two blends sum then clamp: 1 * (1 - (0.4 + 0.5)) = 0.1 (the product rule gave 0.3)", "MUST",
             SUP_SECTION, drivers(A=1, B=1, T=1), {"effective": {"A": 1, "B": 1, "T": 0.1, "C": 0}}),
        case("partial source driver", "MUST", SUP_SECTION, drivers(A=0.5, B=1, T=1), {"effective": {"T": 0.3}}),
        case("target driver below 1", "MUST", SUP_SECTION, drivers(A=1, B=1, T=0.8), {"effective": {"T": 0.08}}),
        case("block above threshold adds its amount; the sum clamps to 1", "MUST", SUP_SECTION,
             drivers(A=1, B=1, T=1, C=0.6), {"effective": {"T": 0, "C": 0.6}}),
        case("block threshold is strict (0.5 is not above 0.5)", "MUST", SUP_SECTION,
             drivers(A=1, B=1, T=1, C=0.5), {"effective": {"T": 0.1}}),
        case("drivers clamp to [0, 1] before suppression; a missing driver is 0", "MUST",
             "KHR_character_expression_suppression: Immutable input and output vectors",
             drivers(A=2, T=1.5), {"effective": {"A": 1, "B": 0, "T": 0.6, "C": 0}}),
    ]
    return a.finish(), expected_file("SUP-Overlap", "suppression",
        "Attenuations from every suppression on a target are summed, clamped to [0, 1], and applied once.",
        cases, notes=["Tolerance for every effective value: 1e-6."])


def sup_custom():
    a, entries = expression_asset(["Curve", "Soft", "T"], [SUPPRESSION])
    entries[0]["extensions"][SUPPRESSION] = {"suppressions": [{"target": 2, "type": "ACME_curve", "amount": 1}]}
    # threshold on a blend: exporters MUST NOT write it; importers MUST ignore it (validators SHOULD warn).
    entries[1]["extensions"][SUPPRESSION] = {"suppressions": [{"target": 2, "type": "blend", "amount": 0.5,
                                                               "threshold": 0.9}]}
    cases = [
        case("custom type without a companion contributes 0", "MUST",
             "KHR_character_expression_suppression: Custom suppression types",
             {"drivers": {"Curve": 1, "T": 1}}, {"effective": {"T": 1}}),
        case("threshold on a blend is ignored: 1 * (1 - 0.5 * 0.5) = 0.75", "MUST", SUP_SECTION,
             {"drivers": {"Soft": 0.5, "T": 1}}, {"effective": {"T": 0.75}}),
        case("both together", "MUST", SUP_SECTION, {"drivers": {"Curve": 1, "Soft": 1, "T": 1}},
             {"effective": {"T": 0.5}}),
    ]
    return a.finish(), expected_file("SUP-Custom", "suppression",
        "An unsupported custom suppression type contributes 0; threshold on a blend is ignored.", cases,
        notes=["Validators SHOULD warn about the threshold on Soft's blend suppression; the asset is valid."])


# ── Expressions ───────────────────────────────────────────────────────────────────────────────

def rotation_keys(axis, degrees):
    half = math.radians(degrees) / 2
    q = [0.0, 0.0, 0.0, math.cos(half)]
    q["xyz".index(axis)] = math.sin(half)
    return [0, 0, 0, 1] + q


def expr_rot_small():
    a = Asset([CHARACTER, EXPRESSION, JOINT])
    a.node("Root", children=[1])
    a.node("Eye")
    specs = [("yawA", "y"), ("pitch", "x"), ("yawB", "y")]
    entries = []
    for name, axis in specs:
        animation = a.animation(name, [(node_target(1, "rotation"), [0, 1], rotation_keys(axis, 10), "VEC4",
                                        "LINEAR", 5126)])
        entries.append({"name": name, "animation": animation, "extensions": {JOINT: {"channels": [0]}}})
    a.root["extensions"] = {CHARACTER: {}, EXPRESSION: {"expressions": entries}}
    section = "KHR_character_expression_joint: Response handling and runtime composition; Authoring guidance"
    rot = lambda vector, tolerance: {"properties": [{"target": "/nodes/1/rotation", "rotation_vector_deg": vector,
                                                     "tolerance_deg": tolerance}]}
    cases = [
        case("single response: the absolute sample at t = e*T", "MUST",
             "KHR_character_expression: Sampling", {"drivers": {"yawA": 1}}, rot([0, 10, 0], 1e-3)),
        case("half driver samples t = 0.5 (slerp)", "MUST", "KHR_character_expression: Sampling",
             {"drivers": {"yawA": 0.5}}, rot([0, 5, 0], 1e-3)),
        case("10 degrees about Y plus 10 degrees about X is approximately both", "SHOULD", section,
             {"drivers": {"yawA": 1, "pitch": 1}}, rot([10, 10, 0], 1.5)),
        case("10 degrees plus 10 degrees about the same axis is approximately 20 degrees", "SHOULD", section,
             {"drivers": {"yawA": 1, "yawB": 1}}, rot([0, 20, 0], 0.5)),
        case("no driver: no response for the property", "MUST", "KHR_character_expression: Expression response "
             "evaluator", {"drivers": {}}, {"properties": [{"target": "/nodes/1/rotation", "value": None}]}),
    ]
    return a.finish(), expected_file("EXPR-RotSmall", "expression",
        "Rotation composition is host-defined but SHOULD be approximately additive for small rotations; any "
        "method within the tolerance passes.", cases,
        notes=["rotation_vector_deg is the axis-angle vector (axis times angle in degrees) of the composed node "
               "rotation; each component must be within tolerance_deg. Both multiplication orders and an "
               "axis-angle sum pass the SHOULD cases."])


def expr_scale():
    a = Asset([CHARACTER, EXPRESSION, JOINT])
    a.node("Root", children=[1])
    a.node("Target", translation=[1, 0, 0], scale=[1, 1, 1])
    grow2 = a.animation("grow2", [(node_target(1, "scale"), [0, 1], [1, 1, 1, 2, 2, 2], "VEC3", "LINEAR", 5126)])
    grow3 = a.animation("grow3", [(node_target(1, "scale"), [0, 2], [1, 1, 1, 3, 3, 3], "VEC3", "LINEAR", 5126)])
    shift = a.animation("shift", [(node_target(1, "translation"), [0, 1], [0.5, 0, 0, 1.5, 0, 0], "VEC3",
                                   "LINEAR", 5126)])
    a.root["extensions"] = {CHARACTER: {}, EXPRESSION: {"expressions": [
        {"name": "grow2", "animation": grow2, "extensions": {JOINT: {"channels": [0]}}},
        {"name": "grow3", "animation": grow3, "extensions": {JOINT: {"channels": [0]}}},
        {"name": "shift", "animation": shift, "extensions": {JOINT: {"channels": [0]}}}]}}
    scale_section = "KHR_character_expression_joint: Response handling and runtime composition"
    prop = lambda target, value: {"target": target, "value": value, "tolerance": 1e-5}
    cases = [
        case("scale responses 1->2 and 1->3 compose to b + sum(s - n) = 4 (multiplying gives 6)", "SHOULD",
             scale_section, {"drivers": {"grow2": 1, "grow3": 1}}, {"properties": [prop("/nodes/1/scale", [4, 4, 4])]}),
        case("t = e*T: grow3 has T = 2, so a 0.5 driver samples t = 1", "SHOULD", scale_section,
             {"drivers": {"grow3": 0.5}}, {"properties": [prop("/nodes/1/scale", [2, 2, 2])]}),
        case("translation uses its own time-zero sample n, not the authored b: 1 + (1.5 - 0.5) = 2", "MUST",
             "KHR_character_expression: Additive aggregation", {"drivers": {"shift": 1}},
             {"properties": [prop("/nodes/1/translation", [2, 0, 0]), {"target": "/nodes/1/scale", "value": None}]}),
        case("half driver on the translation", "MUST", "KHR_character_expression: Additive aggregation",
             {"drivers": {"shift": 0.5}}, {"properties": [prop("/nodes/1/translation", [1.5, 0, 0])]}),
    ]
    return a.finish(), expected_file("EXPR-Scale", "expression",
        "Node scale SHOULD compose per component as b + sum(s - n); translation MUST.", cases)


def png_2x2():
    def chunk(kind, data):
        return struct.pack(">I", len(data)) + kind + data + struct.pack(">I", zlib.crc32(kind + data) & 0xFFFFFFFF)
    rows = b"\0" + bytes([255, 0, 0, 255, 0, 255, 0, 255]) + b"\0" + bytes([0, 0, 255, 255, 255, 255, 0, 255])
    return (b"\x89PNG\r\n\x1a\n" + chunk(b"IHDR", struct.pack(">IIBBBBB", 2, 2, 8, 6, 0, 0, 0))
            + chunk(b"IDAT", zlib.compress(rows, 9)) + chunk(b"IEND", b""))


def tt_atlas():
    a = Asset([CHARACTER, EXPRESSION, TEXTURE, POINTER, TEXTURE_TRANSFORM])
    image_view = a.view(png_2x2())
    a.root["images"] = [{"bufferView": image_view, "mimeType": "image/png", "name": "Atlas2x2"}]
    a.root["samplers"] = [{"magFilter": 9728, "minFilter": 9728}]
    a.root["textures"] = [{"source": 0, "sampler": 0}]
    a.root["materials"] = [{"name": "AtlasMaterial", "pbrMetallicRoughness": {"baseColorTexture": {
        "index": 0, "extensions": {TEXTURE_TRANSFORM: {"offset": [0, 0], "scale": [0.5, 0.5]}}}}}]
    mesh = a.mesh("Quad")
    a.root["meshes"][mesh]["primitives"][0]["material"] = 0
    a.node("Root", children=[1])
    a.node("Face", mesh=mesh)
    base = "/materials/0/pbrMetallicRoughness/baseColorTexture/extensions/KHR_texture_transform/"
    channels = {
        "cellRight": (base + "offset", [0, 0.5, 1], [0, 0, 0.5, 0, 0.5, 0], "VEC2", "STEP"),
        "cellDown": (base + "offset", [0, 0.5, 1], [0, 0, 0, 0.5, 0, 0.5], "VEC2", "STEP"),
        "spin": (base + "rotation", [0, 1], [0, 7], "SCALAR", "LINEAR"),
        "zoom": (base + "scale", [0, 1], [0.5, 0.5, 1, 1], "VEC2", "LINEAR"),
        "zoomMore": (base + "scale", [0, 1], [0.5, 0.5, 0.75, 0.75], "VEC2", "LINEAR"),
    }
    entries = []
    for name, (pointer, times, values, kind, interpolation) in channels.items():
        animation = a.animation(name, [(pointer_target(pointer), times, values, kind, interpolation, 5126)])
        entries.append({"name": name, "animation": animation, "extensions": {TEXTURE: {"channels": [0]}}})
    a.root["extensions"] = {CHARACTER: {}, EXPRESSION: {"expressions": entries}}
    section = "KHR_character_expression_texture_transform: Expression composition"
    prop = lambda leaf, value: {"target": base + leaf, "value": value, "tolerance": 1e-5}
    cases = [
        case("one STEP atlas cell", "MUST", section, {"drivers": {"cellRight": 1}},
             {"properties": [prop("offset", [0.5, 0]), {"target": base + "rotation", "value": None}]}),
        case("STEP below the switching key holds the first key (a response of [0, 0])", "MUST",
             "KHR_character_expression: Sampling", {"drivers": {"cellRight": 0.25}}, {"properties": [prop("offset", [0, 0])]}),
        case("STEP at the switching key", "MUST", "KHR_character_expression: Sampling",
             {"drivers": {"cellRight": 0.5}}, {"properties": [prop("offset", [0.5, 0])]}),
        case("two cells add: the diagonal cell", "MUST", section, {"drivers": {"cellRight": 1, "cellDown": 1}},
             {"properties": [prop("offset", [0.5, 0.5])]}),
        case("raw radians, no wrapping: 7 rad", "MUST", section, {"drivers": {"spin": 1}},
             {"properties": [prop("rotation", 7)]}),
        case("half driver: 3.5 rad", "MUST", section, {"drivers": {"spin": 0.5}}, {"properties": [prop("rotation", 3.5)]}),
        case("texture scale composes as b + sum(s - n): 0.5 + 0.5 + 0.25", "SHOULD", section,
             {"drivers": {"zoom": 1, "zoomMore": 1}}, {"properties": [prop("scale", [1.25, 1.25])]}),
        case("no driver: no response", "MUST", "KHR_character_expression: Expression response evaluator",
             {"drivers": {}}, {"properties": [{"target": base + "offset", "value": None}]}),
    ]
    return a.finish(), expected_file("TT-Atlas", "expression",
        "Texture-transform offset and rotation are additive (rotation on raw radians, never wrapped); scale "
        "SHOULD be additive.", cases)


# ── Hints and poses ───────────────────────────────────────────────────────────────────────────

def cam_default():
    a = Asset([CAMERA_HINT])
    a.root["cameras"] = [{"name": "Lens", "type": "perspective",
                          "perspective": {"yfov": 0.785398, "znear": 0.01, "zfar": 100}}]
    camera = lambda role, **extra: {CAMERA_HINT: {"role": role, **extra}}
    a.node("Root", children=[1, 3, 4, 5, 6, 7, 8, 9])
    a.node("Rig")
    a.node("DefaultB", translation=[0, 1.5, 1], extensions=camera("default_view", camera=0, targetNode=4))
    a.node("Portrait", translation=[0, 1.6, 0.6], extensions=camera("portrait", targetNode=4, label="Face"))
    a.node("Subject", translation=[0, 1.6, 0])
    a.node("DefaultA", translation=[1, 1, 3], extensions=camera("default_view"))
    a.node("Stand", translation=[0, 0, 2], children=[2])
    a.node("Inspect", translation=[0, 3, 0], extensions=camera("ACME_inspect", label="Inspection"))
    a.node("Spare")
    a.node("DefaultC", translation=[-1, 1, 3], extensions=camera("default_view"))
    section = "KHR_node_camera_hint: Default view selection; Custom roles"
    cases = [
        case("several default_view hints: the lowest node index wins", "SHOULD", section,
             {"query": "default_view", "host": {"recognized_custom_roles": []}}, {"node": "DefaultB", "node_index": 2}),
        case("unrecognized custom roles stay out of default affordances", "SHOULD", section,
             {"query": "default_affordances", "host": {"recognized_custom_roles": []}},
             {"nodes": ["DefaultB", "Portrait", "DefaultA", "DefaultC"]}),
        case("a host that recognizes the custom role may surface it", "SHOULD", section,
             {"query": "default_affordances", "host": {"recognized_custom_roles": ["ACME_inspect"]}},
             {"nodes": ["DefaultB", "Portrait", "DefaultA", "Inspect", "DefaultC"]}),
        case("initial pose position is the hint node's global translation; targetNode gives only the subject "
             "point", "MUST", "KHR_node_camera_hint: Initial camera pose; Subject point", {"query": "pose"},
             {"hints": {"DefaultB": {"position": [0, 1.5, 3], "forward": [0, 0, -1], "subject_point": [0, 1.6, 0]},
                        "Portrait": {"position": [0, 1.6, 0.6], "forward": [0, 0, -1], "subject_point": [0, 1.6, 0]},
                        "DefaultA": {"position": [1, 1, 3], "forward": [0, 0, -1], "subject_point": None}},
              "tolerance": 1e-5}),
    ]
    return a.finish(), expected_file("CAM-Default", "camera_hint",
        "default_view selection by lowest node index; unrecognized custom roles not surfaced; targetNode does "
        "not orient the camera.", cases,
        notes=["A targetNode equal to the hint's own node is invalid (validators MUST reject it); that case is "
               "invalid/camera-target-self.gltf."])


def look_animated():
    a = Asset([LOOKAT])
    a.node("Root", translation=[1, 0, 0], children=[1])
    a.node("Parent", rotation=[0, S45, 0, S45], scale=[2, 2, 2], children=[2])
    a.node("Marker", translation=[1, 0, 0], rotation=[S225, 0, 0, C225],
           scale=[3, 3, 3], children=[3], extensions={LOOKAT: {"hint": "ACME_eye_target"}})
    a.node("HintlessMarker", translation=[0, 0, 1], extensions={LOOKAT: {}})
    a.animation("ParentMotion", [(node_target(1, "translation"), [0, 1], [0, 0, 0, 0, 1, 0], "VEC3", "LINEAR", 5126)])
    r = 3 * 2 * S45  # 4.2426...
    points = lambda t: {"Marker": [1, t, -2], "HintlessMarker": [1 + r, t - r, -2]}
    section = "KHR_node_lookat_target: Target-point semantics"
    cases = [
        case("authored state", "MUST", section, {"instances": [{"state": {"animation": None}}]},
             {"instances": [points(0)], "tolerance": 1e-5}),
        case("two instances in different evaluation states are independent", "MUST", section,
             {"instances": [{"state": {"animation": "ParentMotion", "time": 0}},
                            {"state": {"animation": "ParentMotion", "time": 1}},
                            {"state": {"animation": "ParentMotion", "time": 0.5}}]},
             {"instances": [points(0), points(1), points(0.5)], "tolerance": 1e-5}),
    ]
    return a.finish(), expected_file("LOOK-Animated", "lookat_target",
        "The target point is the translation of the evaluated global transform; the marker's own rotation and "
        "scale do not move it, its ancestors' do.", cases,
        notes=["Points are in the asset's coordinate space; a host's placement of each instance is outside it."])


def ref_poses():
    a = Asset([CHARACTER, POSE])
    a.node("Root", children=[1, 4])
    a.node("Hips", translation=[0, 1, 0], children=[2])
    a.node("Arm", translation=[0.2, 0, 0], children=[3])
    a.node("Hand", translation=[0.3, 0, 0])
    a.node("Prop", matrix=[1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1, 0, 0, 0, 0.5, 1])
    a.animation("APose", [(node_target(2, "rotation"), [0], [0, 0, -S225, C225], "VEC4", "STEP", 5126)],
                {POSE: {"poseType": "a_pose"}})
    a.animation("Custom", [(node_target(1, "translation"), [2], [0, 0.9, 0], "VEC3", "LINEAR", 5126)],
                {POSE: {"poseType": "ACME_custom"}})
    a.animation("Wave", [(node_target(2, "rotation"), [0, 1], [0, 0, 0, 1, 0, 0, S45, S45], "VEC4", "LINEAR", 5126)])
    a.root["extensions"] = {CHARACTER: {}}
    identity = [0, 0, 0, 1]
    trs = lambda t, r=identity, s=(1, 1, 1): {"translation": list(t), "rotation": list(r), "scale": list(s)}
    prop_matrix = {"matrix": [1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1, 0, 0, 0, 0.5, 1]}
    h = 0.3 * S45
    section = "KHR_character_reference_pose: Reference-pose validity"
    cases = [
        case("a_pose (STEP, one key): targeted rotation from the sampler, everything else authored", "MUST",
             section, {"animation": "APose"},
             {"poseType": "a_pose", "local": {"Hips": trs([0, 1, 0]), "Arm": trs([0.2, 0, 0], [0, 0, -S225, C225]),
                                              "Hand": trs([0.3, 0, 0]), "Prop": prop_matrix},
              "global_translation": {"Hand": [0.2 + h, 1 - h, 0], "Prop": [0, 0, 0.5]}, "tolerance": 1e-5}),
        case("custom label, LINEAR key at t = 2; Arm keeps its authored rotation, not the Wave animation's", "MUST",
             section, {"animation": "Custom"},
             {"poseType": "ACME_custom", "local": {"Hips": trs([0, 0.9, 0]), "Arm": trs([0.2, 0, 0]),
                                                   "Hand": trs([0.3, 0, 0]), "Prop": prop_matrix},
              "global_translation": {"Hand": [0.5, 0.9, 0], "Prop": [0, 0, 0.5]}, "tolerance": 1e-5}),
        case("only tagged animations are reference poses", "MUST", "KHR_character_reference_pose: Extension usage",
             {"query": "tagged_animations"}, {"animations": ["APose", "Custom"]}),
    ]
    return a.finish(), expected_file("REF-Poses", "reference_pose",
        "Resolved reference-pose values: sampled values for targeted TRS properties, authored initial values "
        "(including matrix) for everything else.", cases)


# ── Invalid set ───────────────────────────────────────────────────────────────────────────────

def minimal_hint_asset(extension, value, at="node"):
    a = Asset([extension])
    mesh = a.mesh("Quad")
    if at == "node":
        a.node("Root", mesh=mesh, extensions={extension: value})
    else:
        a.node("Root", mesh=mesh)
        a.root["meshes"][mesh]["primitives"][0]["extensions"] = {extension: value}
    return a


def invalid_set():
    files, manifest = {}, {}

    def add(name, root, codes, reason, section):
        files[name + ".gltf"] = root
        manifest[name + ".gltf"] = {"error_codes": codes, "reason": reason, "spec_section": section}

    a = minimal_hint_asset(NODE_HINT, {"roles": ["third_person"]})
    a.root["extensionsRequired"] = [NODE_HINT]
    add("node-hint-required", a.finish(), ["HINT_REQUIRED"],
        "KHR_node_visibility_hint is listed in extensionsRequired.", "KHR_node_visibility_hint: Extension usage")

    a = minimal_hint_asset(PRIMITIVE_HINT, {"roles": ["first_person"]}, at="primitive")
    a.root["extensionsRequired"] = [PRIMITIVE_HINT]
    add("primitive-hint-required", a.finish(), ["HINT_REQUIRED"],
        "KHR_mesh_primitive_visibility_hint is listed in extensionsRequired.",
        "KHR_mesh_primitive_visibility_hint: Extension usage")

    a = minimal_hint_asset(CAMERA_HINT, {"role": "portrait"})
    a.root["extensionsRequired"] = [CAMERA_HINT]
    add("camera-hint-required", a.finish(), ["HINT_REQUIRED"],
        "KHR_node_camera_hint is listed in extensionsRequired.", "KHR_node_camera_hint: Extension usage")

    a = minimal_hint_asset(NODE_HINT, {"role": "third_person"})
    add("role-without-roles", a.finish(), ["SCHEMA", "HINT_WITHOUT_ROLES"],
        "A visibility hint has the retired single role and no roles array.",
        "KHR_node_visibility_hint: Extension object (roles is required)")

    a = Asset([CAMERA_HINT])
    a.node("Root", children=[1])
    a.node("SelfTarget", translation=[0, 1, 2], extensions={CAMERA_HINT: {"role": "default_view", "targetNode": 1}})
    add("camera-target-self", a.finish(), ["CAMERA_TARGET_SELF"],
        "A camera hint's targetNode refers to the node that carries the hint.", "KHR_node_camera_hint: Subject point")

    a, entries = expression_asset(["blink", "blink"])
    add("duplicate-expression-name", a.finish(), ["DUPLICATE_EXPRESSION_NAME"],
        "Two expression entries share the name 'blink'.", "KHR_character_expression: Extension structure")

    a, entries = expression_asset(["smile"], [MAPPING])
    a.root["extensions"][MAPPING] = {"expressionSetMappings": {"demo_v1": {"Smile": [{"source": 0, "weight": 1.5}]}}}
    add("mapping-weight-above-one", a.finish(), ["SCHEMA"],
        "A mapping weight is 1.5; weights are limited to [0, 1].", "KHR_character_expression_mapping: Mapping entries")

    a, entries = expression_asset(["jawOpen"])
    a.root["nodes"][1]["matrix"] = [1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1]
    add("joint-channel-matrix-node", a.finish(), ["MATRIX_TARGET"],
        "A joint channel targets the translation of a node that defines matrix.",
        "KHR_character_expression: Channel targets")

    a = Asset([CHARACTER, EXPRESSION, JOINT, MORPH])
    mesh = a.mesh("Morph", morph=True)
    a.root["meshes"][mesh]["weights"] = [0]
    a.node("Root", children=[1])
    a.node("Face", mesh=mesh)
    animation = a.animation("smile", [(node_target(1, "weights"), [0, 1], [0, 1], "SCALAR", "LINEAR", 5126)])
    a.root["extensions"] = {CHARACTER: {}, EXPRESSION: {"expressions": [
        {"name": "smile", "animation": animation, "extensions": {MORPH: {"channels": [0]}, JOINT: {"channels": [0]}}}]}}
    add("channel-in-two-derived-extensions", a.finish(), ["CHANNEL_IN_TWO_DERIVED", "CHANNEL_CLASS"],
        "Channel 0 is listed by both KHR_character_expression_morphtarget and KHR_character_expression_joint on "
        "the same entry (it is also not a node TRS channel, which the joint extension requires).",
        "KHR_character_expression: Derived expression extensions")
    return files, {"license": "CC0-1.0", "spec": SPEC,
                   "summary": "Each file is valid except for one rule; a validator must reject it with the listed "
                              "error code(s) and no others.", "files": manifest}


FIXTURES = [vh_multi_role, vh_unknown_custom, vh_always_plus, vh_compose, sup_overlap, sup_custom,
            expr_rot_small, expr_scale, tt_atlas, cam_default, look_animated, ref_poses]


def dump(value) -> bytes:
    return (json.dumps(value, indent=2, ensure_ascii=False) + "\n").encode("utf-8")


def outputs() -> dict:
    result = {}
    for build in FIXTURES:
        asset, expected = build()
        name = expected["fixture"]
        result[OUT / name / (name + ".gltf")] = dump(asset)
        result[OUT / name / "expected.json"] = dump(expected)
    files, manifest = invalid_set()
    for name, asset in files.items():
        result[OUT / "invalid" / name] = dump(asset)
    result[OUT / "invalid" / "expected.json"] = dump(manifest)
    return result


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    parser.add_argument("--check", action="store_true", help="compare instead of writing")
    args = parser.parse_args()
    generated = outputs()
    mismatched = [path for path, data in generated.items() if not path.exists() or path.read_bytes() != data]
    known = set(generated) | {OUT / "README.md"}
    stray = [path for path in OUT.rglob("*") if path.is_file() and path not in known] if OUT.exists() else []
    if args.check:
        problems = [str(p.relative_to(REPO)) for p in mismatched] + [f"{p.relative_to(REPO)} (not generated)" for p in stray]
        if problems:
            print("conformance fixtures differ from the generator: " + ", ".join(problems), file=sys.stderr)
            return 1
        print(f"conformance fixtures up to date ({len(generated)} files)")
        return 0
    for path in mismatched:
        path.parent.mkdir(parents=True, exist_ok=True)
        path.write_bytes(generated[path])
        print(f"wrote {path.relative_to(REPO)}")
    for path in stray:
        print(f"warning: {path.relative_to(REPO)} is not generated", file=sys.stderr)
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
