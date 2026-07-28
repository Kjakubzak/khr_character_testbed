#!/usr/bin/env python3
"""Add declaration-only KHR_xmp_json_ld dependencies to character GLBs."""

import json
import pathlib
import struct
import sys


JSON_CHUNK = 0x4E4F534A


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
    used = root.get("extensionsUsed")
    if not isinstance(used, list) or "KHR_character" not in used:
        return False
    if "KHR_xmp_json_ld" in used:
        return False

    used.insert(used.index("KHR_character") + 1, "KHR_xmp_json_ld")
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
            print(f"updated {path}")
    print(f"updated {changed} GLB file(s)")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
