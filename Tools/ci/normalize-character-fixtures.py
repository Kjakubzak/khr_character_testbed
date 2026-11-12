#!/usr/bin/env python3
"""Normalize character GLBs written by older exporters to the KHR_character wire format of the
normative draft in KhronosGroup/glTF#2512 at 7ab5368a (glTF-private 314c95c5 carries the same payload).

Only the GLB JSON chunk is rewritten (padded to 4 bytes with spaces); every other chunk is copied
byte for byte, and a file that needs no change is not written. The rewrite is idempotent, so the
script is safe to re-run on already normalized files:

- ``KHR_character`` becomes the empty marker ``{}`` (a legacy ``rootNode`` is dropped);
- an expression entry's legacy ``expression`` label becomes ``name``;
- ``KHR_character_expression_mask`` / ``masks`` become ``KHR_character_expression_suppression`` /
  ``suppressions``; the legacy ``name`` echo of a migrated mask is dropped (a current suppression
  ``name`` is an independent label and is kept), and ``threshold`` is dropped on a blend or omitted type;
- ``KHR_character_expression_texture`` becomes ``KHR_character_expression_texture_transform``;
- both renames also apply to ``extensionsUsed`` and ``extensionsRequired``;
- the legacy ``TPose`` / ``APose`` / ``IPose`` labels become ``t_pose`` / ``a_pose`` / ``i_pose``, and a
  reference pose without ``poseType`` (implicitly a T-pose in the July draft) gets ``t_pose``;
- the unqualified look-at hints (``eyes`` and the July examples such as ``eye_target``) become
  vendor-qualified ``ACME_`` values;
- legacy expression-name references become expression indices, and index-valued properties written
  as strings (``"3"``) or integral floats become integers;
- a visibility hint's single ``role`` becomes the ``roles`` array (``third_person_only`` and
  ``first_person_only`` become ``third_person`` and ``first_person``);
- ``KHR_node_visibility_hint``, ``KHR_mesh_primitive_visibility_hint`` and ``KHR_node_camera_hint`` are
  removed from ``extensionsRequired`` (they stay in ``extensionsUsed``), and an emptied
  ``extensionsRequired`` is removed;
- the made-up ``KHR_mesh_annotation*`` and ``KHR_virtual_transforms`` extensions are deleted wherever they
  appear and from both declaration lists; a primitive whose ``KHR_mesh_annotation_renderview`` said
  ``thirdPersonOnly`` or ``firstPersonOnly`` gets a ``KHR_mesh_primitive_visibility_hint`` with the
  matching role instead (other values impose no restriction and are dropped);
- a visibility-hint extension in use is added to ``extensionsUsed`` when missing.

Some legacy content needs an author's decision and is reported or rejected, not guessed: a mapping
``weight`` outside [0, 1] raises an error; the July camera-hint roles ``framing_target`` and
``orbit_center`` and custom suppression types that are not ``VENDOR_snake_case`` print a warning.

Mapping-set identifiers are nonempty aliases, not documentation URLs. Known legacy
aliases/URLs below migrate to stable aliases; unrelated identifiers are preserved.
An authored legacy input mapping is moved to the sole expressionSetMappings
operation with target indices renamed source. This never computes an inverse.
"""

import json
import pathlib
import re
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
NODE_HINT = "KHR_node_visibility_hint"
PRIMITIVE_HINT = "KHR_mesh_primitive_visibility_hint"
CAMERA_HINT = "KHR_node_camera_hint"
JOINT = "KHR_character_expression_joint"
MORPH_TARGET = "KHR_character_expression_morphtarget"
# KHR_node_visibility_hint / KHR_mesh_primitive_visibility_hint "Extension usage" and
# KHR_node_camera_hint: an asset MUST NOT list these in extensionsRequired.
NEVER_REQUIRED = (NODE_HINT, PRIMITIVE_HINT, CAMERA_HINT)
LEGACY_ANNOTATION = "KHR_mesh_annotation"
LEGACY_RENDERVIEW = "KHR_mesh_annotation_renderview"
LEGACY_VIRTUAL_TRANSFORMS = "KHR_virtual_transforms"
# Made-up names that never existed in any KHR_character draft.
RETIRED_EXTENSION_PREFIXES = (LEGACY_ANNOTATION,)
RETIRED_EXTENSIONS = (LEGACY_VIRTUAL_TRANSFORMS,)
VISIBILITY_ROLE_REPLACEMENTS = {
    "third_person_only": "third_person",
    "first_person_only": "first_person",
}
# VRM firstPerson annotation types carried by KHR_mesh_annotation_renderview.renderVisibility.
# "both"/"always" and "auto" impose no author restriction a hint can express, so they map to nothing.
RENDER_VISIBILITY_ROLES = {
    "thirdPersonOnly": "third_person",
    "firstPersonOnly": "first_person",
}
LEGACY_CAMERA_ROLES = {"framing_target": "framing_view", "orbit_center": "orbit_view"}
STANDARD_SUPPRESSION_TYPES = ("blend", "block")
CUSTOM_SUPPRESSION_TYPE = re.compile(r"^[A-Z0-9]+_[a-z0-9]+(?:_[a-z0-9]+)*$")
DIGITS = re.compile(r"^[0-9]+$")
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
    "eye_target": "ACME_eye_target",
    "head_target": "ACME_head_target",
    "camera_target": "ACME_camera_target",
    "aim_target": "ACME_aim_target",
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


def warn(message: str) -> None:
    print(f"warning: {message}", file=sys.stderr)


def integral(value: object) -> object:
    """Return an integral float or a decimal string as an int; leave everything else unchanged."""
    if isinstance(value, float) and value.is_integer() and value >= 0:
        return int(value)
    if isinstance(value, str) and DIGITS.match(value):
        return int(value)
    return value


def resolve_legacy_reference(value: object, indices: dict[str, int], path: str) -> object:
    if isinstance(value, float):
        return integral(value)
    if not isinstance(value, str):
        return value
    if value in indices:
        return indices[value]
    if DIGITS.match(value):
        return int(value)
    raise ValueError(f"{path}: expression name {value!r} is missing or ambiguous")


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
    legacy = rename_key(suppression, "masks", "suppressions", SUPPRESSION)
    changed = legacy
    entries = suppression.get("suppressions", [])
    if not isinstance(entries, list):
        return changed
    for entry in entries:
        if not isinstance(entry, dict):
            continue
        # A July mask name had to echo the target's expression label; the current name labels the
        # suppression itself, so only the legacy echo is dropped.
        if legacy and "name" in entry:
            del entry["name"]
            changed = True
        kind = entry.get("type", "blend")
        if "threshold" in entry and kind == "blend":
            del entry["threshold"]
            changed = True
        if (isinstance(kind, str) and kind not in STANDARD_SUPPRESSION_TYPES
                and not CUSTOM_SUPPRESSION_TYPE.match(kind)):
            warn(f"suppression type {kind!r} is not VENDOR_snake_case; rename it by hand")
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
        if not isinstance(pose, dict):
            continue
        if "poseType" not in pose:
            # July: poseType was optional and defaulted to TPose; it is now required, with no default.
            pose["poseType"] = "t_pose"
            changed = True
        elif pose["poseType"] in POSE_LABEL_REPLACEMENTS:
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


def normalize_visibility_hint(hint: object, path: str) -> bool:
    """Turn a single legacy ``role`` into the ``roles`` array, keeping the key position."""
    if not isinstance(hint, dict):
        return False
    changed = False
    if "role" in hint:
        role = hint["role"]
        if "roles" in hint:
            existing = hint["roles"] if isinstance(hint["roles"], list) else [hint["roles"]]
            if existing != [role]:
                raise ValueError(f"{path}: legacy role {role!r} differs from roles {hint['roles']!r}")
            del hint["role"]
        else:
            rebuilt = {("roles" if key == "role" else key): ([value] if key == "role" else value)
                       for key, value in hint.items()}
            hint.clear()
            hint.update(rebuilt)
        changed = True
    roles = hint.get("roles")
    if isinstance(roles, str):
        roles = [roles]
        changed = True
    if isinstance(roles, list):
        migrated = []
        for role in roles:
            role = VISIBILITY_ROLE_REPLACEMENTS.get(role, role)
            if role not in migrated:
                migrated.append(role)
        if migrated != hint["roles"]:
            hint["roles"] = migrated
            changed = True
    return changed


def is_retired_extension(name: object) -> bool:
    return isinstance(name, str) and (
        name in RETIRED_EXTENSIONS or any(name.startswith(prefix) for prefix in RETIRED_EXTENSION_PREFIXES))


def migrate_annotation(primitive: dict, path: str) -> bool:
    """Replace a primitive's KHR_mesh_annotation_renderview with the equivalent visibility hint."""
    extensions = primitive.get("extensions")
    if not isinstance(extensions, dict) or LEGACY_ANNOTATION not in extensions:
        return False
    annotation = extensions[LEGACY_ANNOTATION]
    renderview = annotation.get("extensions", {}).get(LEGACY_RENDERVIEW, {}) if isinstance(annotation, dict) else {}
    visibility = renderview.get("renderVisibility") if isinstance(renderview, dict) else None
    role = RENDER_VISIBILITY_ROLES.get(visibility)
    if visibility is not None and role is None and visibility not in ("always", "both", "auto"):
        warn(f"{path}: unknown renderVisibility {visibility!r} dropped without a hint")
    if role is None or PRIMITIVE_HINT in extensions:
        return False
    # Put the hint where the annotation was, so key order stays stable across reruns.
    rebuilt = {}
    for key, value in extensions.items():
        if key == LEGACY_ANNOTATION:
            rebuilt[PRIMITIVE_HINT] = {"roles": [role]}
        rebuilt[key] = value
    extensions.clear()
    extensions.update(rebuilt)
    return True


def strip_retired_extensions(value: object) -> bool:
    """Delete retired made-up extension objects anywhere in the tree; drop emptied containers."""
    changed = False
    if isinstance(value, dict):
        extensions = value.get("extensions")
        if isinstance(extensions, dict):
            for name in [name for name in extensions if is_retired_extension(name)]:
                del extensions[name]
                changed = True
            if changed and not extensions:
                del value["extensions"]
        for child in list(value.values()):
            changed |= strip_retired_extensions(child)
    elif isinstance(value, list):
        for child in value:
            changed |= strip_retired_extensions(child)
    return changed


def extension_names_in_use(value: object, found: set[str]) -> set[str]:
    if isinstance(value, dict):
        extensions = value.get("extensions")
        if isinstance(extensions, dict):
            found.update(name for name in extensions if isinstance(name, str))
        for child in value.values():
            extension_names_in_use(child, found)
    elif isinstance(value, list):
        for child in value:
            extension_names_in_use(child, found)
    return found


def normalize_visibility(root: dict) -> bool:
    changed = False
    for node_index, node in enumerate(root.get("nodes", [])):
        if isinstance(node, dict):
            changed |= normalize_visibility_hint(
                node.get("extensions", {}).get(NODE_HINT), f"nodes[{node_index}].{NODE_HINT}")
    for mesh_index, mesh in enumerate(root.get("meshes", [])):
        if not isinstance(mesh, dict):
            continue
        for primitive_index, primitive in enumerate(mesh.get("primitives", [])):
            if not isinstance(primitive, dict):
                continue
            path = f"meshes[{mesh_index}].primitives[{primitive_index}]"
            changed |= migrate_annotation(primitive, path)
            changed |= normalize_visibility_hint(
                primitive.get("extensions", {}).get(PRIMITIVE_HINT), f"{path}.{PRIMITIVE_HINT}")
    return changed


def normalize_declarations(root: dict) -> bool:
    """Drop retired names, keep never-required hints out of extensionsRequired, declare hints in use."""
    changed = False
    for key in ("extensionsUsed", "extensionsRequired"):
        declarations = root.get(key)
        if not isinstance(declarations, list):
            continue
        kept = [name for name in declarations
                if not is_retired_extension(name) and not (key == "extensionsRequired" and name in NEVER_REQUIRED)]
        if kept != declarations:
            declarations[:] = kept
            changed = True
    if root.get("extensionsRequired") == []:
        del root["extensionsRequired"]
        changed = True
    in_use = extension_names_in_use({key: value for key, value in root.items()
                                     if key not in ("extensionsUsed", "extensionsRequired")}, set())
    missing = [name for name in (NODE_HINT, PRIMITIVE_HINT) if name in in_use
               and name not in root.get("extensionsUsed", [])]
    if missing:
        root.setdefault("extensionsUsed", []).extend(missing)
        changed = True
    return changed


def set_index(container: object, key: object, path: str) -> bool:
    """Rewrite container[key] as an int when it is a decimal string or an integral float."""
    if isinstance(container, dict) and key not in container:
        return False
    value = container[key]
    if isinstance(value, int) and not isinstance(value, bool):
        return False
    converted = integral(value)
    if not isinstance(converted, int) or isinstance(converted, bool):
        raise ValueError(f"{path}[{key!r}]: index {value!r} is not an integer")
    container[key] = converted
    return True


def normalize_index_properties(root: dict) -> bool:
    """Write index-valued KHR_character properties as integers (string or float legacy forms)."""
    changed = False
    extensions = root.get("extensions", {})
    expressions = extensions.get(EXPRESSION, {}).get("expressions", [])
    if isinstance(expressions, list):
        for expression_index, expression in enumerate(expressions):
            if not isinstance(expression, dict):
                continue
            path = f"expressions[{expression_index}]"
            changed |= set_index(expression, "animation", path)
            for derived in (JOINT, MORPH_TARGET, TEXTURE_TRANSFORM):
                channels = expression.get("extensions", {}).get(derived, {}).get("channels")
                if isinstance(channels, list):
                    for channel_index in range(len(channels)):
                        changed |= set_index(channels, channel_index, f"{path}.{derived}.channels")
    skeleton_sets = extensions.get(SKELETON_MAPPING, {}).get("skeletalRigMappings", {})
    if isinstance(skeleton_sets, dict):
        for alias, associations in skeleton_sets.items():
            if isinstance(associations, dict):
                for role, association in associations.items():
                    if isinstance(association, dict):
                        changed |= set_index(association, "node", f"skeletalRigMappings[{alias!r}][{role!r}]")
    for node_index, node in enumerate(root.get("nodes", [])):
        hint = node.get("extensions", {}).get(CAMERA_HINT) if isinstance(node, dict) else None
        if isinstance(hint, dict):
            for key in ("camera", "targetNode"):
                changed |= set_index(hint, key, f"nodes[{node_index}].{CAMERA_HINT}")
            if hint.get("role") in LEGACY_CAMERA_ROLES:
                warn(f"nodes[{node_index}].{CAMERA_HINT}: July role {hint['role']!r} is now a custom role; "
                     f"{LEGACY_CAMERA_ROLES[hint['role']]!r} needs the camera pose on the hint node, so migrate it by hand")
    return changed


def check_mapping_weights(mapping_sets: object) -> None:
    if not isinstance(mapping_sets, dict):
        return
    for alias, inputs in mapping_sets.items():
        if not isinstance(inputs, dict):
            continue
        for input_key, records in inputs.items():
            for record in records if isinstance(records, list) else []:
                weight = record.get("weight") if isinstance(record, dict) else None
                if isinstance(weight, (int, float)) and not 0 <= weight <= 1:
                    raise ValueError(f"expressionSetMappings[{alias!r}][{input_key!r}]: weight {weight} is outside "
                                     "[0, 1]; rescale it by hand (mapped drivers now saturate)")


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
    changed |= normalize_visibility(root)
    changed |= strip_retired_extensions(root)
    changed |= normalize_declarations(root)
    changed |= normalize_index_properties(root)

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

    check_mapping_weights(mapping_sets)

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
