#!/usr/bin/env python3
"""Normalize dependency declarations and expression references in character GLBs."""

import json
import pathlib
import struct
import sys


JSON_CHUNK = 0x4E4F534A
CHARACTER = "KHR_character"
EXPRESSION = "KHR_character_expression"
MAPPING = "KHR_character_expression_mapping"
MASK = "KHR_character_expression_mask"
XMP = "KHR_xmp_json_ld"


def expression_indices(root: dict) -> dict[str, int]:
    expression_root = root.get("extensions", {}).get(EXPRESSION, {})
    expressions = expression_root.get("expressions", [])
    indices: dict[str, int] = {}
    duplicates: set[str] = set()
    for index, expression in enumerate(expressions):
        if not isinstance(expression, dict):
            continue
        name = expression.get("expression")
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


def normalize_json(root: dict) -> bool:
    changed = False
    used = root.get("extensionsUsed")
    if isinstance(used, list) and CHARACTER in used and XMP not in used:
        used.insert(used.index(CHARACTER) + 1, XMP)
        changed = True

    indices = expression_indices(root)
    extensions = root.get("extensions", {})
    expression_root = extensions.get(EXPRESSION, {})
    expressions = expression_root.get("expressions", [])
    if isinstance(expressions, list):
        for expression_index, expression in enumerate(expressions):
            if not isinstance(expression, dict):
                continue
            masks = expression.get("extensions", {}).get(MASK, {}).get("masks", [])
            if not isinstance(masks, list):
                continue
            for mask_index, mask in enumerate(masks):
                if not isinstance(mask, dict) or "target" not in mask:
                    continue
                resolved = resolve_legacy_reference(
                    mask["target"], indices, f"expressions[{expression_index}].masks[{mask_index}].target"
                )
                if resolved != mask["target"]:
                    mask["target"] = resolved
                    changed = True

    mapping_sets = extensions.get(MAPPING, {}).get("expressionSetMappings", {})
    if isinstance(mapping_sets, dict):
        for set_name, targets in mapping_sets.items():
            if not isinstance(targets, dict):
                continue
            for target_name, sources in targets.items():
                if not isinstance(sources, list):
                    continue
                for source_index, source in enumerate(sources):
                    if not isinstance(source, dict) or "source" not in source:
                        continue
                    resolved = resolve_legacy_reference(
                        source["source"],
                        indices,
                        f"expressionSetMappings[{set_name!r}][{target_name!r}][{source_index}].source",
                    )
                    if resolved != source["source"]:
                        source["source"] = resolved
                        changed = True
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
