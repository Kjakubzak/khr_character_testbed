#!/usr/bin/env python3
"""Normalize character GLBs written by older exporters to the current KHR_character wire format.

The rewrite is idempotent, so the script is safe to re-run on already normalized files:

- ``KHR_character`` becomes the empty marker ``{}`` (a legacy ``rootNode`` is dropped);
- an expression entry's legacy ``expression`` label becomes ``name``;
- ``KHR_character_expression_mask`` / ``masks`` become ``KHR_character_expression_suppression`` /
  ``suppressions``; a suppression ``name`` is dropped, and so is ``threshold`` on a blend or omitted type;
- ``KHR_character_expression_texture`` becomes ``KHR_character_expression_texture_transform``;
- both renames also apply to ``extensionsUsed`` and ``extensionsRequired``;
- the legacy ``TPose`` / ``APose`` / ``IPose`` labels become ``t_pose`` / ``a_pose`` / ``i_pose``;
- the unqualified look-at hint ``eyes`` becomes ``ACME_eye_target``;
- legacy expression-name references become expression indices.

Mapping-set identifiers are nonempty aliases, not documentation URLs. Known legacy
aliases/URLs below migrate to stable aliases; unrelated identifiers are preserved.
An authored legacy input mapping is moved to the sole expressionSetMappings
operation with target indices renamed source. This never computes an inverse.
"""

import json
import pathlib
import struct
import sys


JSON_CHUNK = 0x4E4F534A
CHARACTER = "KHR_character"
EXPRESSION = "KHR_character_expression"
MAPPING = "KHR_character_expression_mapping"
LEGACY_MASK = "KHR_character_expression_mask"
SUPPRESSION = "KHR_character_expression_suppression"
LEGACY_TEXTURE = "KHR_character_expression_texture"
TEXTURE_TRANSFORM = "KHR_character_expression_texture_transform"
REFERENCE_POSE = "KHR_character_reference_pose"
LOOKAT_TARGET = "KHR_node_lookat_target"
SKELETON_MAPPING = "KHR_character_skeleton_mapping"
XMP = "KHR_xmp_json_ld"
EXTENSION_RENAMES = {
    LEGACY_MASK: SUPPRESSION,
    LEGACY_TEXTURE: TEXTURE_TRANSFORM,
}
POSE_LABEL_REPLACEMENTS = {
    "TPose": "t_pose",
    "APose": "a_pose",
    "IPose": "i_pose",
}
LOOKAT_HINT_REPLACEMENTS = {
    "eyes": "ACME_eye_target",
}
EXPRESSION_MAPPING_IDENTIFIER_REPLACEMENTS = {
    "ARKit": "arkit_v1",
    "https://example.com/expression-vocabularies/arkit/v1": "arkit_v1",
    "demoVocabulary": "demo_v1",
    "https://example.com/expression-vocabularies/demo/v1": "demo_v1",
    "vrmExpressionPresets": "vrm1_expressions",
    "https://github.com/vrm-c/vrm-specification/blob/70ae16e93abd6da727fdf641b67aa41010c6d933/specification/VRMC_vrm-1.0/expressions.md": "vrm1_expressions",
}
SKELETON_MAPPING_IDENTIFIER_REPLACEMENTS = {
    "default": "default_v1",
    "https://example.com/skeleton-vocabularies/default/v1": "default_v1",
    "unityHumanoid": "unity_humanoid_v1",
    "https://example.com/skeleton-vocabularies/unity-humanoid/v1": "unity_humanoid_v1",
    "vrmHumanoid": "vrm1_humanoid",
    "https://github.com/vrm-c/vrm-specification/blob/70ae16e93abd6da727fdf641b67aa41010c6d933/specification/VRMC_vrm-1.0/humanoid.md": "vrm1_humanoid",
}


def entry_name(expression: dict) -> object:
    return expression["name"] if "name" in expression else expression.get("expression")


def expression_indices(root: dict) -> dict[str, int]:
    expression_root = root.get("extensions", {}).get(EXPRESSION, {})
    expressions = expression_root.get("expressions", [])
    indices: dict[str, int] = {}
    duplicates: set[str] = set()
    for index, expression in enumerate(expressions):
        if not isinstance(expression, dict):
            continue
        name = entry_name(expression)
        if not isinstance(name, str):
            continue
        if name in indices:
            duplicates.add(name)
        else:
            indices[name] = index
    for name in duplicates:
        indices.pop(name, None)
    return indices


def resolve_legacy_reference(value: object, indices: dict[str, int], path: str) -> object:
    if not isinstance(value, str):
        return value
    if value not in indices:
        raise ValueError(f"{path}: expression name {value!r} is missing or ambiguous")
    return indices[value]


def normalize_mapping_identifiers(mapping_sets: object, replacements: dict[str, str]) -> bool:
    if not isinstance(mapping_sets, dict):
        return False
    changed = False
    for legacy, replacement in replacements.items():
        if legacy not in mapping_sets:
            continue
        if replacement in mapping_sets:
            raise ValueError(f"mapping contains both legacy {legacy!r} and replacement {replacement!r}")
        mapping_sets[replacement] = mapping_sets.pop(legacy)
        changed = True
    return changed


def rename_key(container: dict, legacy: str, replacement: str, path: str) -> bool:
    if legacy not in container:
        return False
    if replacement in container:
        raise ValueError(f"{path}: contains both legacy {legacy!r} and replacement {replacement!r}")
    container[replacement] = container.pop(legacy)
    return True


def rename_declarations(declarations: object) -> bool:
    if not isinstance(declarations, list):
        return False
    changed = False
    for legacy, replacement in EXTENSION_RENAMES.items():
        if legacy not in declarations:
            continue
        renamed = [replacement if name == legacy else name for name in declarations]
        deduplicated = []
        for name in renamed:
            if name not in deduplicated:
                deduplicated.append(name)
        declarations[:] = deduplicated
        changed = True
    return changed


def normalize_character_marker(extensions: dict) -> bool:
    marker = extensions.get(CHARACTER)
    if marker is None:
        return False
    if not isinstance(marker, dict):
        extensions[CHARACTER] = {}
        return True
    normalized = {key: value for key, value in marker.items() if key in ("extensions", "extras")}
    if normalized == marker:
        return False
    extensions[CHARACTER] = normalized
    return True


def normalize_suppressions(suppression: object) -> bool:
    if not isinstance(suppression, dict):
        return False
    changed = rename_key(suppression, "masks", "suppressions", SUPPRESSION)
    entries = suppression.get("suppressions", [])
    if not isinstance(entries, list):
        return changed
    for entry in entries:
        if not isinstance(entry, dict):
            continue
        if "name" in entry:
            del entry["name"]
            changed = True
        if "threshold" in entry and entry.get("type", "blend") == "blend":
            del entry["threshold"]
            changed = True
    return changed


def normalize_expression_entries(extensions: dict) -> bool:
    expressions = extensions.get(EXPRESSION, {}).get("expressions", [])
    if not isinstance(expressions, list):
        return False
    changed = False
    for expression_index, expression in enumerate(expressions):
        if not isinstance(expression, dict):
            continue
        path = f"expressions[{expression_index}]"
        if "expression" in expression:
            legacy = expression["expression"]
            if "name" in expression:
                if expression["name"] != legacy:
                    raise ValueError(f"{path}: name {expression['name']!r} differs from legacy expression {legacy!r}")
                del expression["expression"]
            else:
                renamed = {("name" if key == "expression" else key): value for key, value in expression.items()}
                expression.clear()
                expression.update(renamed)
            changed = True
        entry_extensions = expression.get("extensions")
        if not isinstance(entry_extensions, dict):
            continue
        for legacy, replacement in EXTENSION_RENAMES.items():
            changed |= rename_key(entry_extensions, legacy, replacement, f"{path}.extensions")
        changed |= normalize_suppressions(entry_extensions.get(SUPPRESSION))
    return changed


def resolve_suppression_targets(extensions: dict, indices: dict[str, int]) -> bool:
    expressions = extensions.get(EXPRESSION, {}).get("expressions", [])
    if not isinstance(expressions, list):
        return False
    changed = False
    for expression_index, expression in enumerate(expressions):
        if not isinstance(expression, dict):
            continue
        suppressions = expression.get("extensions", {}).get(SUPPRESSION, {}).get("suppressions", [])
        if not isinstance(suppressions, list):
            continue
        for suppression_index, suppression in enumerate(suppressions):
            if not isinstance(suppression, dict) or "target" not in suppression:
                continue
            resolved = resolve_legacy_reference(
                suppression["target"],
                indices,
                f"expressions[{expression_index}].suppressions[{suppression_index}].target",
            )
            if resolved != suppression["target"]:
                suppression["target"] = resolved
                changed = True
    return changed


def normalize_pose_labels(root: dict) -> bool:
    changed = False
    for animation in root.get("animations", []):
        if not isinstance(animation, dict):
            continue
        pose = animation.get("extensions", {}).get(REFERENCE_POSE)
        if isinstance(pose, dict) and pose.get("poseType") in POSE_LABEL_REPLACEMENTS:
            pose["poseType"] = POSE_LABEL_REPLACEMENTS[pose["poseType"]]
            changed = True
    return changed


def normalize_lookat_hints(root: dict) -> bool:
    changed = False
    for node in root.get("nodes", []):
        if not isinstance(node, dict):
            continue
        target = node.get("extensions", {}).get(LOOKAT_TARGET)
        if isinstance(target, dict) and target.get("hint") in LOOKAT_HINT_REPLACEMENTS:
            target["hint"] = LOOKAT_HINT_REPLACEMENTS[target["hint"]]
            changed = True
    return changed


def normalize_json(root: dict) -> bool:
    changed = False
    used = root.get("extensionsUsed")
    required = root.get("extensionsRequired")
    root_extensions = root.get("extensions", {})
    if isinstance(used, list) and XMP in used and XMP not in root_extensions:
        used.remove(XMP)
        changed = True
    if isinstance(required, list) and XMP in required and XMP not in root_extensions:
        required.remove(XMP)
        changed = True
    changed |= rename_declarations(used)
    changed |= rename_declarations(required)

    extensions = root_extensions
    changed |= normalize_character_marker(extensions)
    changed |= normalize_expression_entries(extensions)
    changed |= normalize_pose_labels(root)
    changed |= normalize_lookat_hints(root)

    indices = expression_indices(root)
    changed |= resolve_suppression_targets(extensions, indices)

    mapping_extension = extensions.get(MAPPING, {})
    mapping_sets = mapping_extension.get("expressionSetMappings", {})
    changed |= normalize_mapping_identifiers(mapping_sets, EXPRESSION_MAPPING_IDENTIFIER_REPLACEMENTS)
    legacy_inputs = mapping_extension.pop("expressionSetInputMappings", None)
    if isinstance(legacy_inputs, dict):
        changed = True
        normalize_mapping_identifiers(legacy_inputs, EXPRESSION_MAPPING_IDENTIFIER_REPLACEMENTS)
        for alias, commands in legacy_inputs.items():
            converted = {}
            for command, records in commands.items():
                converted[command] = [
                    {("source" if key == "target" else key): value for key, value in record.items()}
                    for record in records
                ]
            mapping_sets[alias] = converted
        mapping_extension["expressionSetMappings"] = mapping_sets
    if isinstance(mapping_sets, dict):
        for alias, inputs in mapping_sets.items():
            if not isinstance(inputs, dict):
                continue
            for input_key, records in inputs.items():
                if not isinstance(records, list):
                    continue
                for record_index, record in enumerate(records):
                    if not isinstance(record, dict) or "source" not in record:
                        continue
                    resolved = resolve_legacy_reference(record["source"], indices,
                        f"expressionSetMappings[{alias!r}][{input_key!r}][{record_index}].source")
                    if resolved != record["source"]:
                        record["source"] = resolved
                        changed = True

    skeleton_mapping_sets = extensions.get(SKELETON_MAPPING, {}).get("skeletalRigMappings", {})
    changed |= normalize_mapping_identifiers(
        skeleton_mapping_sets, SKELETON_MAPPING_IDENTIFIER_REPLACEMENTS
    )
    return changed


def update(path: pathlib.Path) -> bool:
    data = path.read_bytes()
    if len(data) < 20 or data[:4] != b"glTF":
        raise ValueError(f"{path}: not a GLB file")

    version, _ = struct.unpack_from("<II", data, 4)
    chunks = []
    offset = 12
    root = None
    while offset + 8 <= len(data):
        length, chunk_type = struct.unpack_from("<II", data, offset)
        start = offset + 8
        end = start + length
        if end > len(data):
            raise ValueError(f"{path}: truncated GLB chunk")
        payload = data[start:end]
        if chunk_type == JSON_CHUNK:
            root = json.loads(payload.rstrip(b" \t\r\n\0").decode("utf-8"))
        chunks.append((chunk_type, payload))
        offset = end

    if root is None:
        raise ValueError(f"{path}: missing JSON chunk")
    if not normalize_json(root):
        return False

    encoded = json.dumps(root, ensure_ascii=False, separators=(",", ":")).encode("utf-8")
    encoded += b" " * ((4 - len(encoded) % 4) % 4)
    chunks = [
        (chunk_type, encoded if chunk_type == JSON_CHUNK else payload)
        for chunk_type, payload in chunks
    ]
    body = b"".join(
        struct.pack("<II", len(payload), chunk_type) + payload
        for chunk_type, payload in chunks
    )
    path.write_bytes(b"glTF" + struct.pack("<II", version, 12 + len(body)) + body)
    return True


def main() -> int:
    if len(sys.argv) < 2:
        print(f"usage: {sys.argv[0]} FILE.glb [...]", file=sys.stderr)
        return 2
    changed = 0
    for argument in sys.argv[1:]:
        path = pathlib.Path(argument)
        if update(path):
            changed += 1
            print(f"normalized {path}")
    print(f"normalized {changed} GLB file(s)")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
