#!/usr/bin/env python3
"""Rebuild the hero's per-role visibility-hint variants from the base hero, deterministically.

Usage: python3 Tools/ci/make_hero_variants.py [--check]

Run it after replacing or re-normalizing ``khr-character-example.glb`` (Git LFS; pull it first). It:

1. normalizes the base hero in place with ``normalize-character-fixtures.py`` and restores its
   suppressions when they are missing (see ``HERO_SUPPRESSIONS``), so its wire matches the normative
   draft in KhronosGroup/glTF#2512 at 7ab5368a (glTF-private 314c95c5 carries the same payload);
2. writes the three variants next to it. Each starts from the normalized base with the base's
   primitive hints removed, then adds one example:

   - ``-always``: the Body node gets ``roles: ["always"]``;
   - ``-first-person``: a small cube "view-model" node is added under the scene with
     ``roles: ["first_person"]`` (its geometry is appended to the binary chunk);
   - ``-third-person``: the Face primitives get ``roles: ["third_person"]`` and the Body and Hair
     nodes get ``roles: ["always"]``.

``--check`` writes nothing and fails when a committed file differs from what the script would write.
Only the JSON chunk and, for ``-first-person``, the end of the binary chunk differ from the base.
"""

import argparse
import copy
import importlib.util
import json
import pathlib
import struct
import sys

HERE = pathlib.Path(__file__).resolve().parent
REPO = HERE.parents[1]
HERO_DIR = REPO / "Assets" / "SampleAssets" / "VRM_KHR_Examples"
BASE = HERO_DIR / "khr-character-example.glb"
JSON_CHUNK = 0x4E4F534A
BIN_CHUNK = 0x004E4942

_spec = importlib.util.spec_from_file_location("normalize", HERE / "normalize-character-fixtures.py")
normalize = importlib.util.module_from_spec(_spec)
_spec.loader.exec_module(normalize)

NODE_HINT = normalize.NODE_HINT
PRIMITIVE_HINT = normalize.PRIMITIVE_HINT
SUPPRESSION = normalize.SUPPRESSION

# The base hero lost its suppressions in an earlier normalization. These are the masks of the
# three.js testbed hero (0b5vr/khr-character-testbed, viewer/src/assets/khr-character-example.glb),
# which its converter adapted from the VRM overrideMouth settings ("blend"), translated to the
# current suppression shape: target by expression name, blend amount, no name echo, no threshold.
# happy and surprised overlap on aa/ou/oh, so the sum-and-clamp rule matters here.
HERO_SUPPRESSIONS = {
    "happy": [("aa", 1), ("ih", 1), ("ou", 1), ("ee", 1), ("oh", 1)],
    "surprised": [("aa", 0.5), ("ou", 0.5), ("oh", 0.5)],
}

# Face primitive labels, keyed by the part token of the VRoid material name
# ("N00_000_00_<part>_00_<kind> (Instance)").
PART_LABELS = {"Face": "Head part"}

# The first-person view-model: a 12 cm cube, 1.42 m up and 15 cm forward of the scene root.
CUBE_POSITIONS = [
    -0.06, -0.06, -0.06, 0.06, -0.06, -0.06, 0.06, 0.06, -0.06, -0.06, 0.06, -0.06,
    -0.06, -0.06, 0.06, 0.06, -0.06, 0.06, 0.06, 0.06, 0.06, -0.06, 0.06, 0.06,
]
CUBE_INDICES = [
    0, 2, 1, 0, 3, 2, 4, 5, 6, 4, 6, 7, 0, 1, 5, 0, 5, 4,
    2, 3, 7, 2, 7, 6, 1, 2, 6, 1, 6, 5, 0, 4, 7, 0, 7, 3,
]


def read_glb(path: pathlib.Path) -> tuple[int, dict, bytes]:
    data = path.read_bytes()
    if len(data) < 20 or data[:4] != b"glTF":
        raise ValueError(f"{path}: not a GLB file (is the Git LFS object pulled?)")
    version = struct.unpack_from("<I", data, 4)[0]
    root, binary, offset = None, b"", 12
    while offset + 8 <= len(data):
        length, kind = struct.unpack_from("<II", data, offset)
        payload = data[offset + 8:offset + 8 + length]
        if kind == JSON_CHUNK:
            root = json.loads(payload.rstrip(b" \t\r\n\0").decode("utf-8"))
        elif kind == BIN_CHUNK:
            binary = payload
        else:
            raise ValueError(f"{path}: unexpected chunk type {kind:#x}")
        offset += 8 + length
    if root is None:
        raise ValueError(f"{path}: missing JSON chunk")
    return version, root, binary


def encode_glb(version: int, root: dict, binary: bytes) -> bytes:
    # Same JSON encoding as normalize-character-fixtures.py, so a rerun of the normalizer is a no-op.
    encoded = json.dumps(root, ensure_ascii=False, separators=(",", ":")).encode("utf-8")
    encoded += b" " * (-len(encoded) % 4)
    body = struct.pack("<II", len(encoded), JSON_CHUNK) + encoded
    if binary:
        binary += b"\0" * (-len(binary) % 4)
        body += struct.pack("<II", len(binary), BIN_CHUNK) + binary
    return b"glTF" + struct.pack("<II", version, 12 + len(body)) + body


def declare(root: dict, name: str) -> None:
    used = root.setdefault("extensionsUsed", [])
    if name not in used:
        used.append(name)


def restore_suppressions(root: dict) -> bool:
    expressions = root["extensions"]["KHR_character_expression"]["expressions"]
    index = {entry["name"]: position for position, entry in enumerate(expressions)}
    changed = False
    for source, targets in HERO_SUPPRESSIONS.items():
        entry = expressions[index[source]]
        extensions = entry.setdefault("extensions", {})
        if SUPPRESSION in extensions:
            continue
        extensions[SUPPRESSION] = {"suppressions": [
            {"target": index[target], "type": "blend", "amount": amount} for target, amount in targets]}
        changed = True
    if changed:
        declare(root, SUPPRESSION)
    return changed


def node_named(root: dict, name: str) -> dict:
    matches = [node for node in root["nodes"] if node.get("name") == name]
    if len(matches) != 1:
        raise ValueError(f"expected one node named {name!r}, found {len(matches)}")
    return matches[0]


def set_extension(target: dict, name: str, value: dict) -> None:
    target.setdefault("extensions", {})[name] = value


def without_primitive_hints(root: dict) -> dict:
    variant = copy.deepcopy(root)
    for mesh in variant.get("meshes", []):
        for primitive in mesh.get("primitives", []):
            extensions = primitive.get("extensions", {})
            if extensions.pop(PRIMITIVE_HINT, None) is not None and not extensions:
                del primitive["extensions"]
    if PRIMITIVE_HINT in variant.get("extensionsUsed", []):
        variant["extensionsUsed"].remove(PRIMITIVE_HINT)
    return variant


def always_variant(root: dict, binary: bytes) -> tuple[dict, bytes]:
    variant = without_primitive_hints(root)
    set_extension(node_named(variant, "Body"), NODE_HINT, {"roles": ["always"], "label": "Always visible"})
    declare(variant, NODE_HINT)
    return variant, binary


def first_person_variant(root: dict, binary: bytes) -> tuple[dict, bytes]:
    variant = without_primitive_hints(root)
    declared = variant["buffers"][0]["byteLength"]
    if len(binary) != declared + (-declared % 4):
        raise ValueError(f"binary chunk is {len(binary)} bytes but buffer 0 declares {declared}")
    data = bytearray(binary)
    views = variant["bufferViews"]
    positions_offset = len(data)
    data += struct.pack(f"<{len(CUBE_POSITIONS)}f", *CUBE_POSITIONS)
    indices_offset = len(data)
    data += struct.pack(f"<{len(CUBE_INDICES)}H", *CUBE_INDICES)
    views.append({"buffer": 0, "byteOffset": positions_offset, "byteLength": indices_offset - positions_offset,
                  "target": 34962})
    views.append({"buffer": 0, "byteOffset": indices_offset, "byteLength": len(data) - indices_offset,
                  "target": 34963})
    accessors = variant["accessors"]
    accessors.append({"bufferView": len(views) - 2, "componentType": 5126, "count": len(CUBE_POSITIONS) // 3,
                      "type": "VEC3", "min": [-0.06, -0.06, -0.06], "max": [0.06, 0.06, 0.06]})
    accessors.append({"bufferView": len(views) - 1, "componentType": 5123, "count": len(CUBE_INDICES),
                      "type": "SCALAR"})
    variant["materials"].append({
        "name": "VH_Example_FirstPerson",
        "pbrMetallicRoughness": {"baseColorFactor": [0.15, 0.75, 1, 1], "metallicFactor": 0, "roughnessFactor": 1},
        "doubleSided": True, "extensions": {"KHR_materials_unlit": {}}})
    declare(variant, "KHR_materials_unlit")
    variant["meshes"].append({"name": "VH_FirstPersonViewModel", "primitives": [{
        "attributes": {"POSITION": len(accessors) - 2}, "indices": len(accessors) - 1,
        "material": len(variant["materials"]) - 1, "mode": 4}]})
    variant["nodes"].append({
        "name": "VH_FirstPersonViewModel", "mesh": len(variant["meshes"]) - 1, "translation": [0, 1.42, 0.15],
        "extensions": {NODE_HINT: {"roles": ["first_person"], "label": "First-person view-model"}}})
    variant["scenes"][variant.get("scene", 0)]["nodes"].append(len(variant["nodes"]) - 1)
    variant["buffers"][0]["byteLength"] = len(data)
    declare(variant, NODE_HINT)
    return variant, bytes(data)


def third_person_variant(root: dict, binary: bytes) -> tuple[dict, bytes]:
    variant = without_primitive_hints(root)
    face = variant["meshes"][node_named(variant, "Face")["mesh"]]
    for primitive in face["primitives"]:
        part = variant["materials"][primitive["material"]]["name"].split("_")[3]
        label = PART_LABELS.get(part, part)
        set_extension(primitive, PRIMITIVE_HINT, {"roles": ["third_person"], "label": f"{label} (hidden in first person)"})
    set_extension(node_named(variant, "Body"), NODE_HINT, {"roles": ["always"], "label": "Body (always visible)"})
    set_extension(node_named(variant, "Hair"), NODE_HINT, {"roles": ["always"], "label": "Hair (always visible)"})
    declare(variant, NODE_HINT)
    declare(variant, PRIMITIVE_HINT)
    return variant, binary


VARIANTS = {
    "khr-character-example-always.glb": always_variant,
    "khr-character-example-first-person.glb": first_person_variant,
    "khr-character-example-third-person.glb": third_person_variant,
}


def outputs() -> dict[pathlib.Path, bytes]:
    version, root, binary = read_glb(BASE)
    normalize.normalize_json(root)
    restore_suppressions(root)
    result = {BASE: encode_glb(version, root, binary)}
    for name, build in VARIANTS.items():
        variant, variant_binary = build(root, binary)
        if normalize.normalize_json(variant):
            raise AssertionError(f"{name}: derived variant is not normalized")
        result[HERO_DIR / name] = encode_glb(version, variant, variant_binary)
    return result


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    parser.add_argument("--check", action="store_true", help="compare instead of writing")
    args = parser.parse_args()
    mismatched = []
    for path, data in outputs().items():
        current = path.read_bytes() if path.exists() else None
        if current == data:
            continue
        if args.check:
            mismatched.append(str(path.relative_to(REPO)))
        else:
            path.write_bytes(data)
            print(f"wrote {path.relative_to(REPO)}")
    if mismatched:
        print("hero files differ from make_hero_variants.py output: " + ", ".join(mismatched), file=sys.stderr)
        return 1
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
