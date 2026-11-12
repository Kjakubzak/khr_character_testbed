#!/usr/bin/env python3
"""Validate the JSON of glTF/GLB files against the glTF 2.0 core schema, the Khronos extension schemas
of a spec checkout, and the KHR_character-family rules that JSON Schema cannot express.

Usage:
  python3 Tools/ci/validate-character-json.py --spec SPEC_ROOT FILE...
  python3 Tools/ci/validate-character-json.py --spec SPEC_ROOT --expect-invalid MANIFEST

SPEC_ROOT is a checkout of the glTF repository at the targeted draft (KhronosGroup/glTF#2512 at
7ab5368a; glTF-private 314c95c5 carries the same extension payload) containing
specification/2.0/schema and extensions/2.0/Khronos. --expect-invalid reads a manifest such as
Tools/fixtures/conformance/invalid/expected.json and requires every listed file to fail with only
the listed error codes.

jsonschema is not a dependency: a small draft 2020-12 evaluator covers every keyword the core and
Khronos extension schemas use (format and annotations are ignored). The semantic checks cover
declarations and dependencies, hints in extensionsRequired, retired names, visibility-hint roles,
index resolution, unique expression names, derived-channel classes and duplicates, channels on
matrix nodes, duplicate targets within an animation, and reference-pose sampler shape. Accessor
data is not decoded.
"""

import argparse
import json
import math
import pathlib
import re
import struct
import sys

NEVER_REQUIRED = ("KHR_node_visibility_hint", "KHR_mesh_primitive_visibility_hint", "KHR_node_camera_hint")
RETIRED = re.compile(r"^(KHR_mesh_annotation.*|KHR_virtual_transforms|KHR_character_expression_mask|"
                     r"KHR_character_expression_texture|KHR_node_lookat)$")
EXPRESSION = "KHR_character_expression"
JOINT = "KHR_character_expression_joint"
MORPH = "KHR_character_expression_morphtarget"
TEXTURE = "KHR_character_expression_texture_transform"
DERIVED = (JOINT, MORPH, TEXTURE)
DEPENDENCIES = {
    EXPRESSION: ["KHR_character"],
    JOINT: ["KHR_character", EXPRESSION],
    MORPH: ["KHR_character", EXPRESSION],
    "KHR_character_expression_suppression": ["KHR_character", EXPRESSION],
    "KHR_character_expression_mapping": ["KHR_character", EXPRESSION],
    TEXTURE: ["KHR_character", EXPRESSION, "KHR_animation_pointer", "KHR_texture_transform"],
    "KHR_character_reference_pose": ["KHR_character"],
    "KHR_character_skeleton_mapping": ["KHR_character"],
}
ROOT_OBJECTS = {  # extensions whose top-level objects must exist when they are used
    EXPRESSION: ["KHR_character", EXPRESSION],
    JOINT: ["KHR_character", EXPRESSION], MORPH: ["KHR_character", EXPRESSION],
    TEXTURE: ["KHR_character", EXPRESSION],
    "KHR_character_expression_suppression": ["KHR_character", EXPRESSION],
    "KHR_character_expression_mapping": ["KHR_character", EXPRESSION, "KHR_character_expression_mapping"],
    "KHR_character_reference_pose": ["KHR_character"],
    "KHR_character_skeleton_mapping": ["KHR_character", "KHR_character_skeleton_mapping"],
}


class Schemas:
    def __init__(self, spec):
        spec = pathlib.Path(spec)
        self.khronos = spec / "extensions/2.0/Khronos"
        self.store = {}
        for path in list((spec / "specification/2.0/schema").glob("*.json")) + list(self.khronos.glob("*/schema/*.json")):
            self.store[path.name] = json.loads(path.read_text())
        if "glTF.schema.json" not in self.store:
            raise SystemExit(f"{spec}: no specification/2.0/schema/glTF.schema.json")
        self.known_khr = {p.name for p in self.khronos.iterdir() if p.is_dir()}


def type_ok(value, kind):
    return {
        "object": isinstance(value, dict), "array": isinstance(value, list), "string": isinstance(value, str),
        "boolean": isinstance(value, bool), "null": value is None,
        "number": isinstance(value, (int, float)) and not isinstance(value, bool),
        "integer": (isinstance(value, int) and not isinstance(value, bool))
                   or (isinstance(value, float) and value.is_integer()),
    }[kind]


def equal(a, b):
    if isinstance(a, bool) or isinstance(b, bool):
        return type(a) is type(b) and a == b
    return a == b


def check(store, value, schema, path, errors, base=None):
    if base is None:
        base = schema
    if schema is True or schema == {}:
        return
    if schema is False:
        errors.append(f"{path}: not allowed")
        return
    collect = lambda sub: errors_of(store, value, sub, path, base)
    if "$ref" in schema:
        file, _, fragment = schema["$ref"].partition("#")
        document = store[file] if file else base
        target = document
        for part in [p for p in fragment.split("/") if p]:
            target = target[part]
        check(store, value, target, path, errors, document)
    for sub in schema.get("allOf", []):
        check(store, value, sub, path, errors, base)
    if "anyOf" in schema and all(collect(sub) for sub in schema["anyOf"]):
        errors.append(f"{path}: {json.dumps(value)[:60]} matches none of anyOf")
    if "oneOf" in schema and sum(not collect(sub) for sub in schema["oneOf"]) != 1:
        errors.append(f"{path}: does not match exactly one of oneOf")
    if "not" in schema and not collect(schema["not"]):
        errors.append(f"{path}: matches a 'not' schema")
    if "type" in schema:
        kinds = schema["type"] if isinstance(schema["type"], list) else [schema["type"]]
        if not any(type_ok(value, k) for k in kinds):
            errors.append(f"{path}: expected {schema['type']}, got {json.dumps(value)[:60]}")
            return
    if "enum" in schema and not any(equal(value, e) for e in schema["enum"]):
        errors.append(f"{path}: {value!r} not in enum")
    if "const" in schema and not equal(value, schema["const"]):
        errors.append(f"{path}: {value!r} is not {schema['const']!r}")
    if isinstance(value, dict):
        for key in schema.get("required", []):
            if key not in value:
                errors.append(f"{path}: missing required '{key}'")
        properties = schema.get("properties", {})
        for key, sub in properties.items():
            if key in value:
                check(store, value[key], sub, f"{path}/{key}", errors, base)
        if "additionalProperties" in schema:
            for key in value:
                if key not in properties:
                    check(store, value[key], schema["additionalProperties"], f"{path}/{key}", errors, base)
        if "propertyNames" in schema:
            for key in value:
                check(store, key, schema["propertyNames"], f"{path}/<{key}>", errors, base)
        if "minProperties" in schema and len(value) < schema["minProperties"]:
            errors.append(f"{path}: fewer than {schema['minProperties']} properties")
        for keyword in ("dependentRequired", "dependencies"):
            for key, needed in schema.get(keyword, {}).items():
                if key in value and isinstance(needed, list):
                    errors += [f"{path}: '{key}' requires '{other}'" for other in needed if other not in value]
    if isinstance(value, list):
        if "items" in schema:
            for index, item in enumerate(value):
                check(store, item, schema["items"], f"{path}/{index}", errors, base)
        if "minItems" in schema and len(value) < schema["minItems"]:
            errors.append(f"{path}: fewer than {schema['minItems']} items")
        if "maxItems" in schema and len(value) > schema["maxItems"]:
            errors.append(f"{path}: more than {schema['maxItems']} items")
        if schema.get("uniqueItems"):
            seen = [json.dumps(v, sort_keys=True) for v in value]
            if len(seen) != len(set(seen)):
                errors.append(f"{path}: items are not unique")
    if isinstance(value, str):
        if "minLength" in schema and len(value) < schema["minLength"]:
            errors.append(f"{path}: shorter than {schema['minLength']}")
        if "maxLength" in schema and len(value) > schema["maxLength"]:
            errors.append(f"{path}: longer than {schema['maxLength']}")
        if "pattern" in schema and not re.search(schema["pattern"], value):
            errors.append(f"{path}: {value!r} does not match {schema['pattern']}")
    if type_ok(value, "number"):
        if "minimum" in schema and value < schema["minimum"]:
            errors.append(f"{path}: {value} < minimum {schema['minimum']}")
        if "maximum" in schema and value > schema["maximum"]:
            errors.append(f"{path}: {value} > maximum {schema['maximum']}")
        if "exclusiveMinimum" in schema and value <= schema["exclusiveMinimum"]:
            errors.append(f"{path}: {value} <= exclusiveMinimum {schema['exclusiveMinimum']}")
        if "exclusiveMaximum" in schema and value >= schema["exclusiveMaximum"]:
            errors.append(f"{path}: {value} >= exclusiveMaximum {schema['exclusiveMaximum']}")
        if "multipleOf" in schema:
            ratio = value / schema["multipleOf"]
            if not math.isclose(ratio, round(ratio)):
                errors.append(f"{path}: not a multiple of {schema['multipleOf']}")


def errors_of(store, value, schema, path, base=None):
    errors = []
    check(store, value, schema, path, errors, base)
    return errors


def read_json(path):
    data = pathlib.Path(path).read_bytes()
    if data.startswith(b"version https://git-lfs"):
        raise ValueError(f"{path}: Git LFS pointer (run git lfs pull)")
    if data[:4] != b"glTF":
        return json.loads(data)
    offset = 12
    while offset + 8 <= len(data):
        length, kind = struct.unpack_from("<II", data, offset)
        if kind == 0x4E4F534A:
            return json.loads(data[offset + 8:offset + 8 + length].rstrip(b" \0"))
        offset += 8 + length
    raise ValueError(f"{path}: no JSON chunk")


def placements(root):
    def of(owner, prefix, path):
        for name, value in (owner.get("extensions") or {}).items():
            yield prefix, f"{path}/extensions/{name}", name, value
    yield from of(root, "glTF", "")
    for i, node in enumerate(root.get("nodes", [])):
        yield from of(node, "node", f"/nodes/{i}")
    for i, mesh in enumerate(root.get("meshes", [])):
        for j, primitive in enumerate(mesh.get("primitives", [])):
            yield from of(primitive, "mesh.primitive", f"/meshes/{i}/primitives/{j}")
    for i, animation in enumerate(root.get("animations", [])):
        yield from of(animation, "animation", f"/animations/{i}")
    for i, entry in enumerate((root.get("extensions") or {}).get(EXPRESSION, {}).get("expressions", [])):
        yield from of(entry, "expression", f"/extensions/{EXPRESSION}/expressions/{i}")


def names_in_use(value, found):
    if isinstance(value, dict):
        found.update((value.get("extensions") or {}).keys())
        for child in value.values():
            names_in_use(child, found)
    elif isinstance(value, list):
        for child in value:
            names_in_use(child, found)
    return found


def channel_property(channel):
    target = channel.get("target", {})
    if target.get("path") == "pointer":
        return (target.get("extensions") or {}).get("KHR_animation_pointer", {}).get("pointer")
    if "node" in target:
        return f"/nodes/{target['node']}/{target.get('path')}"
    return None


def resolve_pointer(root, pointer):
    owner = root
    for part in pointer.strip("/").split("/"):
        try:
            owner = owner[int(part)] if isinstance(owner, list) else owner[part]
        except (KeyError, IndexError, ValueError, TypeError):
            return None, False
    return owner, True


def validate(schemas, root):
    """Return (errors, warnings); each error is (code, message)."""
    errors, warnings = [], []
    err = lambda code, message: errors.append((code, message))
    errors += [("SCHEMA", f"core: {e}") for e in errors_of(schemas.store, root, schemas.store["glTF.schema.json"], "")]
    for prefix, path, name, value in placements(root):
        schema = schemas.store.get(f"{prefix}.{name}.schema.json")
        if schema is not None:
            errors += [("SCHEMA", e) for e in errors_of(schemas.store, value, schema, path)]
        elif name.startswith("KHR_character") or name in NEVER_REQUIRED or name == "KHR_node_lookat_target":
            err("PLACEMENT", f"{path}: {name} is not allowed on a {prefix} object")

    used, required = root.get("extensionsUsed", []), root.get("extensionsRequired", [])
    exts = root.get("extensions") or {}
    present = names_in_use({k: v for k, v in root.items() if k not in ("extensionsUsed", "extensionsRequired")}, set())
    for name in sorted(present - set(used)):
        err("NOT_DECLARED", f"extension {name} is present but not in extensionsUsed")
    for name in sorted(set(required) - set(used)):
        err("NOT_DECLARED", f"extensionsRequired lists {name}, which is not in extensionsUsed")
    for name in sorted(set(required) & set(NEVER_REQUIRED)):
        err("HINT_REQUIRED", f"{name} is listed in extensionsRequired (MUST NOT)")
    for name in sorted(set(used) | present):
        if RETIRED.match(name):
            err("RETIRED_NAME", f"retired or made-up extension name {name}")
        elif name.startswith("KHR_") and name not in schemas.known_khr:
            err("UNKNOWN_KHR", f"KHR_ extension {name} has no folder in the spec checkout")
        for dependency in DEPENDENCIES.get(name, []) if name in used else []:
            if dependency not in used:
                err("DEPENDENCY", f"{name} requires {dependency} in extensionsUsed")
        for top in ROOT_OBJECTS.get(name, []) if name in used else []:
            if top not in exts:
                err("DEPENDENCY", f"{name} requires a top-level {top} object")
    for name in present:
        if name.startswith("KHR_character") and name not in (exts.keys() | {JOINT, MORPH, TEXTURE,
                "KHR_character_expression_suppression", "KHR_character_reference_pose"}):
            err("PLACEMENT", f"{name} must be on the top-level object")
    for i, node in enumerate(root.get("nodes", [])):
        if "KHR_character" in (node.get("extensions") or {}):
            err("PLACEMENT", f"/nodes/{i}: KHR_character must be on the top-level object")

    nodes, animations = root.get("nodes", []), root.get("animations", [])
    accessors = root.get("accessors", [])
    for prefix, path, name, value in placements(root):
        if name in ("KHR_node_visibility_hint", "KHR_mesh_primitive_visibility_hint") and isinstance(value, dict):
            roles = value.get("roles")
            if not isinstance(roles, list) or not roles:
                err("HINT_WITHOUT_ROLES", f"{path}: hint without a roles array" + (" (has legacy 'role')" if "role" in value else ""))
                continue
            if "role" in value:
                # The schema allows extra members; a leftover legacy role beside roles has no meaning.
                warnings.append(f"{path}: legacy 'role' beside 'roles' is ignored")
            if "always" in roles and len(roles) > 1:
                warnings.append(f"{path}: 'always' listed with other roles")

    for a, animation in enumerate(animations):
        seen = {}
        for c, channel in enumerate(animation.get("channels", [])):
            prop = channel_property(channel)
            if prop is None:
                continue
            if prop in seen:
                err("DUPLICATE_TARGET", f"/animations/{a}: channels {seen[prop]} and {c} both target {prop}")
            seen[prop] = c
            match = re.match(r"^/nodes/(\d+)/(translation|rotation|scale|weights)", prop)
            if match and int(match.group(1)) < len(nodes) and "matrix" in nodes[int(match.group(1))] \
                    and match.group(2) != "weights":
                err("MATRIX_TARGET", f"/animations/{a}/channels/{c}: targets {prop} on a node with matrix")
        pose = (animation.get("extensions") or {}).get("KHR_character_reference_pose")
        if pose is not None:
            for c, channel in enumerate(animation.get("channels", [])):
                target = channel.get("target", {})
                sampler = animation["samplers"][channel["sampler"]]
                if "node" not in target or target.get("path") not in ("translation", "rotation", "scale"):
                    err("REFERENCE_POSE", f"/animations/{a}/channels/{c}: reference poses target node TRS only")
                if accessors[sampler["input"]].get("count") != 1:
                    err("REFERENCE_POSE", f"/animations/{a}/channels/{c}: input accessor count must be 1")
                if sampler.get("interpolation", "LINEAR") not in ("LINEAR", "STEP"):
                    err("REFERENCE_POSE", f"/animations/{a}/channels/{c}: interpolation must be LINEAR or STEP")

    expressions = exts.get(EXPRESSION, {}).get("expressions", [])
    labels = [entry.get("name") for entry in expressions]
    for name in sorted({n for n in labels if isinstance(n, str) and labels.count(n) > 1}):
        err("DUPLICATE_EXPRESSION_NAME", f"expression name {name!r} is not unique")
    for i, entry in enumerate(expressions):
        index = entry.get("animation")
        if not (isinstance(index, int) and 0 <= index < len(animations)):
            err("INDEX", f"expressions[{i}].animation {index!r} does not resolve")
            continue
        animation = animations[index]
        for channel in animation.get("channels", []):
            sampler = animation["samplers"][channel["sampler"]]
            if accessors[sampler["input"]].get("count", 0) < 2:
                err("EXPRESSION_SAMPLER", f"expressions[{i}]: sampler {channel['sampler']} has fewer than two keys")
        listed = {}
        for derived in DERIVED:
            for c in (entry.get("extensions") or {}).get(derived, {}).get("channels", []):
                if not (isinstance(c, int) and 0 <= c < len(animation.get("channels", []))):
                    err("INDEX", f"expressions[{i}].{derived}: channel {c!r} does not resolve")
                    continue
                if c in listed:
                    err("CHANNEL_IN_TWO_DERIVED", f"expressions[{i}]: channel {c} is listed by {listed[c]} and {derived}")
                listed[c] = derived
                prop = channel_property(animation["channels"][c]) or ""
                ok = {JOINT: re.match(r"^/nodes/\d+/(translation|rotation|scale)$", prop),
                      MORPH: re.match(r"^/nodes/\d+/weights(/\d+)?$", prop),
                      TEXTURE: re.match(r"^/.+/extensions/KHR_texture_transform/(offset|scale|rotation)$", prop)}[derived]
                if derived == TEXTURE and ok:
                    owner, found = resolve_pointer(root, prop.rsplit("/", 1)[0])
                    ok = found and isinstance(owner, dict) and animation["channels"][c]["target"].get("path") == "pointer"
                if not ok:
                    err("CHANNEL_CLASS", f"expressions[{i}].{derived}: channel {c} ({prop}) is not a {derived} channel")
        for k, s in enumerate((entry.get("extensions") or {}).get("KHR_character_expression_suppression", {}).get("suppressions", [])):
            target = s.get("target")
            if not (isinstance(target, int) and 0 <= target < len(expressions)):
                err("INDEX", f"expressions[{i}].suppressions[{k}].target {target!r} does not resolve")
            if "threshold" in s and s.get("type", "blend") == "blend":
                warnings.append(f"expressions[{i}].suppressions[{k}]: threshold on a blend suppression")
    for alias, inputs in exts.get("KHR_character_expression_mapping", {}).get("expressionSetMappings", {}).items():
        for key, records in inputs.items():
            for record in records:
                source = record.get("source")
                if not (isinstance(source, int) and 0 <= source < len(expressions)):
                    err("INDEX", f"mapping {alias}/{key}: source {source!r} does not resolve")
    for alias, roles in exts.get("KHR_character_skeleton_mapping", {}).get("skeletalRigMappings", {}).items():
        for role, association in roles.items():
            node = association.get("node")
            if not (isinstance(node, int) and 0 <= node < len(nodes)):
                err("INDEX", f"skeleton {alias}/{role}: node {node!r} does not resolve")
    for i, node in enumerate(nodes):
        hint = (node.get("extensions") or {}).get("KHR_node_camera_hint")
        if not isinstance(hint, dict):
            continue
        target, camera = hint.get("targetNode"), hint.get("camera")
        if target == i:
            err("CAMERA_TARGET_SELF", f"/nodes/{i}: camera hint targetNode refers to its own node")
        elif target is not None and not (isinstance(target, int) and 0 <= target < len(nodes)):
            err("INDEX", f"/nodes/{i}: camera hint targetNode {target!r} does not resolve")
        if camera is not None and not (isinstance(camera, int) and 0 <= camera < len(root.get("cameras", []))):
            err("INDEX", f"/nodes/{i}: camera hint camera {camera!r} does not resolve")
    return errors, warnings


def main():
    parser = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    parser.add_argument("--spec", required=True, help="glTF spec checkout with specification/ and extensions/")
    parser.add_argument("--expect-invalid", help="manifest of files that must fail with the listed codes")
    parser.add_argument("files", nargs="*")
    args = parser.parse_args()
    schemas = Schemas(args.spec)
    failures = 0
    if args.expect_invalid:
        manifest_path = pathlib.Path(args.expect_invalid)
        manifest = json.loads(manifest_path.read_text())
        for name, want in manifest["files"].items():
            errors, _ = validate(schemas, read_json(manifest_path.parent / name))
            codes = sorted({code for code, _ in errors})
            ok = bool(errors) and set(codes) <= set(want["error_codes"])
            failures += not ok
            print(f"{'PASS' if ok else 'FAIL'} {name}: rejected with {codes or 'nothing'}; expected {want['error_codes']}")
            for code, message in errors[:6]:
                print(f"     {code}: {message}")
        return 1 if failures else 0
    for file in args.files:
        errors, warnings = validate(schemas, read_json(file))
        failures += bool(errors)
        print(f"{'FAIL' if errors else 'PASS'} {file} ({len(errors)} errors, {len(warnings)} warnings)")
        for code, message in errors[:25]:
            print(f"     {code}: {message}")
        for message in warnings[:10]:
            print(f"     warning: {message}")
    return 1 if failures else 0


if __name__ == "__main__":
    raise SystemExit(main())
