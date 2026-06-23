# Sample Asset Attribution

All assets in this folder are license-clean and safe to redistribute as part of this open-source,
Khronos-community glTF example. Each entry lists its source and SPDX license identifier.

## Synthetic/ — tool-generated, committed

| Asset | Source | License (SPDX) | Notes |
|---|---|---|---|
| `SC-Face.glb` | Generated in code by `Generate Sample Characters` (Tool B) | `CC0-1.0` | Synthetic head mesh with procedural blendshapes (`jawOpen`, `blink`, `lookLeft/Right/Up/Down`, `smile`), a jaw joint, named eye bones (`LeftEye`/`RightEye`), an overlapping `aa` expression (drives the `jawOpen` shape, for additive-vs-override demos) and a `happy` expression that blend-masks `aa`, plus a `Smile` vocabulary mapping. Emits `KHR_character` + `KHR_character_expression` (`_morphtarget`, `_joint`, `_mask`, `_mapping`). |
| `SC-FacePlus.glb` | Generated in code by `Generate Sample Characters` (Tool B) | `CC0-1.0` | SC-Face (same blendshapes, jaw joint, eye bones, `aa`/`happy` mask + `Smile` mapping) plus a texture expression (UV offset + a 2-texture index-swap, snapping at 0.5) on a distinct, CPU-readable lit material. Emits `KHR_character_expression` with `_joint`, `_mask`, `_mapping`, AND `_texture` (+ `KHR_animation_pointer` / `KHR_texture_transform`). |
| `SC-Body.glb` | Generated in code by `Generate Sample Characters` (Tool B) | `CC0-1.0` | Synthetic T-pose humanoid skeleton (unique neutral bone names) with a skeleton mapping, a TPose reference pose, and a portrait camera hint. Emits `KHR_character` + `KHR_character_skeleton_mapping` + `KHR_character_reference_pose` + `KHR_node_camera_hint`. Bones only (no skinned mesh). |

These files are produced entirely from procedural geometry created in this project — they contain no
third-party, vendor, or scanned content. They are dedicated to the public domain under
[CC0 1.0](https://creativecommons.org/publicdomain/zero/1.0/).

## Hero character — committed (Git LFS)

| Asset | Source | License (SPDX) | Notes |
|---|---|---|---|
| `khr-character-example.glb` | Authored in **VRoid Studio 2.12.0** | ⚠️ **CONFIRM BEFORE PUBLIC RELEASE** | The demo "hero" used as the default character across all scenes. The demos consume **only its `KHR_character` data** (skeleton mapping, expressions, camera hints) and **ignore** its VRM extensions (`VRMC_vrm` / `VRMC_springBone` / `VRMC_materials_mtoon` / `VRMC_character_expression_lookat`) and MToon materials. Stored via **Git LFS** (~10.5 MB). |

> ⚠️ **Unlike the synthetic `SC-*`, this asset is VRoid-origin and is NOT automatically CC0.** The demos only read its `KHR_character` data, but the **mesh and textures still originate from VRoid Studio**. Before making this repository public, confirm you have the right to redistribute it and record the SPDX license above: if you authored it yourself in VRoid Studio your chosen license applies; if it derives from a third-party VRoid/VRM model, the original author's terms govern.

## External/ — downloaded on first run (not committed)

Large CC0 base assets (e.g. from the Khronos glTF Sample Assets) are downloaded into `External/` on first
run and are **git-ignored**. When that tooling is added, each downloaded asset's source URL and SPDX license
must be appended here before use. Only CC0 / public-domain / clearly-attributed open-licensed assets are
permitted — no vendor, internal, or scanned characters.

## Rules

- Geometry, textures, and animations must be CC0, public domain, or a clearly-attributed open license.
- Record provenance (source + SPDX) for every committed or downloaded asset in this file.
- Prefer small, diffable, regenerable synthetic assets for committed fixtures.
