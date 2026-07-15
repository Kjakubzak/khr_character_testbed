# Sample Asset Attribution

All assets in this folder are license-clean and safe to redistribute as part of this open-source,
Khronos-community glTF example. Each entry lists its source and SPDX license identifier.

## Synthetic/ — tool-generated, committed

| Asset | Source | License (SPDX) | Notes |
|---|---|---|---|
| `SC-Face.glb` | Generated in code by `Generate Sample Characters` (Tool B) | `CC0-1.0` | Synthetic head mesh with procedural blendshapes (`jawOpen`, `blink`, `lookLeft/Right/Up/Down`, `smile`), a jaw joint, named eye bones (`LeftEye`/`RightEye`), an overlapping `aa` expression (drives the `jawOpen` shape, for additive-vs-override demos) and a `happy` expression that blend-masks `aa`, plus a `Smile` vocabulary mapping. Emits `KHR_character` + `KHR_character_expression` (`_morphtarget`, `_joint`, `_mask`, `_mapping`). |
| `SC-FacePlus.glb` | Generated in code by `Generate Sample Characters` (Tool B) | `CC0-1.0` | SC-Face (same blendshapes, jaw joint, eye bones, `aa`/`happy` mask + `Smile` mapping) plus a texture expression (UV offset + a 2-texture index-swap, snapping at 0.5) on a distinct, CPU-readable lit material. Emits `KHR_character_expression` with `_joint`, `_mask`, `_mapping`, AND `_texture` (+ `KHR_animation_pointer` / `KHR_texture_transform`). |
| `SC-Body.glb` | Generated in code by `Generate Sample Characters` (Tool B) | `CC0-1.0` | Synthetic T-pose humanoid skeleton (unique neutral bone names) with a skeleton mapping, a TPose reference pose, and a portrait camera hint. Emits `KHR_character` + `KHR_character_skeleton_mapping` + `KHR_character_reference_pose` + `KHR_node_camera_hint`. Bones only (no skinned mesh). |
| `SC-LookAt.glb` | Generated in code by `Generate Sample Characters` (Tool B) | `CC0-1.0` | Minimal carrier for `KHR_node_lookat_target`: a `KHR_character` root whose `GazeSolver.AuthoredTargets` mark two nodes as look-at targets (one carries a `hint`, one is hint-less). No expressions or skeleton. Emits `KHR_character` + `KHR_node_lookat_target`. |
| `SC-Partial.glb` | Generated in code by `Generate Sample Characters` (Tool B) | `CC0-1.0` | Deliberately minimal: `KHR_character` + a single morph expression only (no skeleton, camera, or look-at). Used to show most capabilities simply *absent* in the Health tri-state. Emits `KHR_character` + `KHR_character_expression` (`_morphtarget`). |
| `SC-PseudoVRM.glb` | Generated in code by `Generate Sample Characters` (Tool B) | `CC0-1.0` | The SC-Partial body whose exported GLB is post-processed to inject **synthetic** `VRMC_vrm` / `VRMC_springBone` tokens into `extensionsUsed` + a stub root extension, so it *reads* like a VRM-origin asset. **Not a real VRM** (the tokens are empty stubs). Drives the always-on neutralization gate — a KHR re-export drops the `VRMC_*`. |
| `SC-Degraded.glb` | Generated in code by `Generate Sample Characters` (Tool B) | `CC0-1.0` | The SC-Body humanoid whose exported GLB is post-processed so a **required** humanoid joint (`leftFoot`) points at a non-existent node, so on import the mapping resolves its other bones but flags `leftFoot` missing → `SkeletonMapping: Degraded`. The only committed fixture that exercises the Degraded tri-state (loaded by the Health demo picker). Emits `KHR_character` + `KHR_character_skeleton_mapping` + `KHR_character_reference_pose` + `KHR_node_camera_hint`. |
| `SC-ExprEdge.glb` | Generated in code by `Generate Sample Characters` (Tool B) | `CC0-1.0` | Two morph expressions where one **BLOCK-masks** the other (mask edge cases). Emits `KHR_character` + `KHR_character_expression` (`_morphtarget`, `_mask`). |
| `VH-Node.glb` | Generated in code by `Generate Sample Characters` (Tool B) | `CC0-1.0` | `KHR_node_visibility_hint` on a single-mesh Head. Emits `KHR_node_visibility_hint`. |
| `VH-Primitive.glb` | Generated in code by `Generate Sample Characters` (Tool B) | `CC0-1.0` | `KHR_mesh_primitive_visibility_hint` on sub-mesh 1 of a 2-primitive Body. Emits `KHR_mesh_primitive_visibility_hint`. |
| `VH-ViewContext.glb` | Generated in code by `Generate Sample Characters` (Tool B) | `CC0-1.0` | Both visibility-hint extensions on one hierarchy (node + primitive view context). Emits `KHR_node_visibility_hint` + `KHR_mesh_primitive_visibility_hint`. |

These files are produced entirely from procedural geometry created in this project — they contain no
third-party, scanned, or real vendor content. (`SC-PseudoVRM`'s `VRMC_*` tokens are synthetic empty stubs, not
real VRM data; `SC-Degraded` is `SC-Body` with a deliberately dangling bone index.) They are dedicated to the
public domain under [CC0 1.0](https://creativecommons.org/publicdomain/zero/1.0/).

## FromBlender/ — authored in `khr_character_blender`, committed

Eleven canonical KHR-Character `.glb` variations exported by the
[`khr_character_blender`](https://github.com/kenjimeta/khr_character_blender)
Blender addon (specifically [`tests/fixtures/regenerate.py`](https://github.com/kenjimeta/khr_character_blender/blob/master/tests/fixtures/regenerate.py)),
one per extension-combination shape. See [`FromBlender/README.md`](./FromBlender/README.md)
for the full matrix, per-fixture extensions, and how to consume them.

| Asset | Source | License (SPDX) | Notes |
|---|---|---|---|
| `FromBlender/minimal.glb` | `khr_character_blender::build_minimal` | `CC0-1.0` | Just `KHR_character` on an empty root — smallest possible signal. |
| `FromBlender/skeleton.glb` | `khr_character_blender::build_skeleton` | `CC0-1.0` | + `KHR_character_skeleton_mapping` (5 humanoid joints). No mesh, no ref pose. |
| `FromBlender/skeleton_refpose.glb` | `khr_character_blender::build_skeleton_refpose` | `CC0-1.0` | + `KHR_character_reference_pose` (TPose) on the armature action. |
| `FromBlender/expressions_morph.glb` | `khr_character_blender::build_expressions_morph` | `CC0-1.0` | Root + head with `smile`/`frown` shape keys + two morph expressions. Emits `KHR_character_expression` + `_morphtarget`. |
| `FromBlender/expressions_joint.glb` | `khr_character_blender::build_expressions_joint` | `CC0-1.0` | Root + armature + one bone-driven expression (`nod`). Emits `KHR_character_expression` + `_joint`. |
| `FromBlender/expressions_mask.glb` | `khr_character_blender::build_expressions_mask` | `CC0-1.0` | Morph expressions where `smile` blocks `frown` via a mask. `+ _expression_mask`. |
| `FromBlender/expressions_mapping.glb` | `khr_character_blender::build_expressions_mapping` | `CC0-1.0` | Morph expression + ARKit routing (`mouthSmileLeft`/`mouthSmileRight` → local `smile` at 0.5 each). `+ _expression_mapping`. |
| `FromBlender/node_hints.glb` | `khr_character_blender::build_node_hints` | `CC0-1.0` | Root + camera hint ("portrait" role) + lookat target ("eyes" hint). Emits `KHR_node_camera_hint` + `KHR_node_lookat_target`. |
| `FromBlender/visibility_hints.glb` | `khr_character_blender::build_visibility_hints` | `CC0-1.0` | Root + a `first_person` node hint (on an empty view-model) + a 2-primitive cube whose accent primitive is `third_person`. Emits `KHR_node_visibility_hint` + `KHR_mesh_primitive_visibility_hint`. |
| `FromBlender/full.glb` | `khr_character_blender::build_full` | `CC0-1.0` | Everything above combined — 10/11 KHR Character extensions. Only `_texture` absent (needs `KHR_animation_pointer` material channels, outside the addon's surface). |
| `FromBlender/starter.glb` | `khr_character_blender::samples/generate_starter.py` | `CC0-1.0` | The canonical "typical character" — matches `samples/generate_starter.py` output (armature + skeleton mapping + TPose ref pose + head with smile/frown). |

All files procedurally generated from primitive meshes and synthetic
armatures — no third-party content. Regeneration: `blender -b --python
tests/fixtures/regenerate.py` in the `khr_character_blender` checkout,
then copy the resulting `tests/fixtures/*.glb` into this folder.

## Hero character — committed (Git LFS)

| Asset | Source | License (SPDX) | Notes |
|---|---|---|---|
| `khr-character-example.glb` | By **0b5vr**, authored in **VRoid Studio 2.12.0** | `LicenseRef-VRM-1.0` | The demo "hero" used as the default character across all scenes. The demos consume **only its `KHR_character` data** (skeleton mapping, expressions, camera hints) and **ignore** its VRM extensions (`VRMC_vrm` / `VRMC_springBone` / `VRMC_materials_mtoon` / `VRMC_character_expression_lookat`) and MToon materials. Stored via **Git LFS** (~10.5 MB). |
| `khr-character-example-always.glb` | Derived from `khr-character-example.glb` via `tools/make_hero_variants.py` | `LicenseRef-VRM-1.0` | Hero role-variant isolating the `always` (default-visible) `KHR_node_visibility_hint` role. Inherits the base hero's VRM 1.0 terms (**non-commercial**). Stored via **Git LFS** (~11 MB). |
| `khr-character-example-first-person.glb` | Derived from `khr-character-example.glb` via `tools/make_hero_variants.py` | `LicenseRef-VRM-1.0` | Hero role-variant isolating the `first_person` `KHR_node_visibility_hint` role. Inherits the base hero's VRM 1.0 terms (**non-commercial**). Stored via **Git LFS** (~11 MB). |
| `khr-character-example-third-person.glb` | Derived from `khr-character-example.glb` via `tools/make_hero_variants.py` | `LicenseRef-VRM-1.0` | Hero role-variant isolating the `third_person` `KHR_node_visibility_hint` role; also carries a `KHR_mesh_primitive_visibility_hint` example. Inherits the base hero's VRM 1.0 terms (**non-commercial**). Stored via **Git LFS** (~11 MB). |

> **License — read from the model's embedded `VRMC_vrm.meta`** (authoritative for its declared terms): author
> **0b5vr**, under the [**VRM 1.0 license**](https://vrm.dev/licenses/1.0/). There is no SPDX short-identifier for the
> VRM license, so it is recorded above with the SPDX `LicenseRef-` form (`LicenseRef-VRM-1.0`). Declared permissions:
> **redistribution allowed** (`allowRedistribution: true`) and **modification + redistribution allowed**
> (`modification: allowModificationRedistribution`) — so committing/redistributing it in this repo is permitted —
> **avatar use for everyone** (`avatarPermission: everyone`), and **credit unnecessary** (`creditNotation: unnecessary`;
> we credit 0b5vr regardless).
>
> ⚠️ **Non-commercial.** `commercialUsage: personalNonProfit` — unlike the CC0 `SC-*` fixtures, this asset is
> restricted to **personal / non-profit use**, and the metadata further disallows excessively violent or sexual,
> political/religious, and antisocial/hate usage. Its mesh and textures remain 0b5vr's VRoid content under these terms
> (the demos read only its `KHR_character` data). **If you need a fully unrestricted / commercial-friendly default,
> swap in a CC0 model or rely on the synthetic `SC-*` characters** (the demos fall back to `SC-*` automatically when
> the hero is absent).

## External/ — downloaded on first run (not committed)

Large CC0 base assets (e.g. from the Khronos glTF Sample Assets) are downloaded into `External/` on first
run and are **git-ignored**. When that tooling is added, each downloaded asset's source URL and SPDX license
must be appended here before use. Only CC0 / public-domain / clearly-attributed open-licensed assets are
permitted — no vendor, internal, or scanned characters.

## Rules

- Geometry, textures, and animations must be CC0, public domain, or a clearly-attributed open license.
- Record provenance (source + SPDX) for every committed or downloaded asset in this file.
- Prefer small, diffable, regenerable synthetic assets for committed fixtures.
