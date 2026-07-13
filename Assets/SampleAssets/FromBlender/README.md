# FromBlender/ — KHR Character fixtures authored in the Blender addon

Ten canonical `.glb` fixtures produced by the
[`khr_character_blender`](https://github.com/kenjimeta/khr_character_blender)
addon (specifically [`tests/fixtures/regenerate.py`](https://github.com/kenjimeta/khr_character_blender/blob/master/tests/fixtures/regenerate.py)),
each isolating a specific KHR Character extension combination.

Purpose: give this testbed's UnityGLTF-based importer a stable set of
**author-side ground-truth `.glb`s** to validate against. Complements
the `Synthetic/*.glb` set (which are Unity-generated procedurally) —
these are the "authored in Blender" side of the pipeline.

**Every fixture with skeleton or expressions ships a procedural
humanoid character** — 114-vertex UV-sphere head with mouth-region
shape-key deformation + 8-segment cylinder torso weight-painted to the
axial armature bones. Recognizable as a figure in a viewer, not just
cubes. All primitives — no third-party content.

## The matrix

| Fixture | Size | KHR extensions in the wire |
|---|---:|---|
| `minimal.glb` | 256 B | `KHR_character` only (no geometry — smallest signal) |
| `skeleton.glb` | 27 KB | + `_skeleton_mapping` (5 humanoid joints) — includes torso + head skinned to armature |
| `skeleton_refpose.glb` | 30 KB | + `_reference_pose` (TPose on the armature) |
| `expressions_morph.glb` | 29 KB | UV-sphere head + smile/frown morph exprs (`+ _expression + _expression_morphtarget`) |
| `expressions_joint.glb` | 31 KB | armature + torso + head + nod joint expr (`+ _expression + _expression_joint`) |
| `expressions_mask.glb` | 30 KB | morph exprs + smile blocks frown (`+ _expression_mask`) |
| `expressions_mapping.glb` | 29 KB | morph expr + ARKit routing `mouthSmileLeft/Right → smile` at 0.5 each (`+ _expression_mapping`) |
| `node_hints.glb` | 496 B | root + camera hint ("portrait" role) + lookat target ("eyes" hint) — no geometry (`KHR_node_camera_hint + KHR_node_lookat_target`) |
| `full.glb` | 46 KB | Everything above combined — 10/11 KHR Character extensions |
| `starter.glb` | 42 KB | The canonical starter (matches `samples/generate_starter.py` output — character + humanoid armature + TPose ref pose + shape-keyed head + morph exprs) |

Only `KHR_character_expression_texture` is absent — it requires
`KHR_animation_pointer` material channels which are outside the Blender
addon's current authoring surface.

## What the character looks like

Every non-minimal fixture ships:

* **Head** — UV sphere (114 verts, 16 × 8 subdivision, ~13 cm radius)
  positioned at head-bone height (z ≈ 1.9 m). Shape keys deform the
  mouth region (front-facing vertices below the equator):
  * `smile` — corners push out + up
  * `frown` — corners push in + down
  * Deformation ≈ 15% of head radius (3.66 cm) so the difference is
    obvious in a viewer without a slider tour.
  * When an armature is present, the head is 100 % weight-skinned to
    the `head` bone so it moves rigidly with the armature.
* **Torso** — 8-segment capped cylinder (~30 cm tall, ~15 cm radius),
  positioned midway between hips and chest. Weight-painted 1.0 to the
  nearest axial bone (`hips` / `spine` / `chest` / `neck`), so
  armature-driven poses actually deform the body.

Both are procedural primitives — no third-party geometry, no scanned
content. Safe under CC0.

## Consuming these in the testbed

The [GlbViewer scene](../../Samples/GlbViewer) is designed to import any
`.glb`/`.gltf` at runtime and read out capability discovery. Drop any
fixture from this folder into the viewer to see what UnityGLTF surfaces:

1. Open `Assets/Samples/_Shared/Scenes/SampleHub.unity`.
2. Click **GlbViewer**.
3. Drag one of the fixtures into the "load a glb" slot (or use the
   file dialog).
4. Read the capability list — expected extensions listed in the table
   above.

For automated verification, the fixtures could plug into a
`SandboxFromBlenderTests.cs` (naming per this repo's convention) that
iterates the matrix and asserts UnityGLTF's KHR Character import path
correctly surfaces each fixture's extensions.

## Provenance & regeneration

Every file here was exported from the `khr_character_blender` addon on
Blender 4.5.0 via:

```
blender -b --python tests/fixtures/regenerate.py
```

To refresh (whenever the Blender addon's exporter output changes):

1. Regenerate in `khr_character_blender`.
2. Copy the resulting `tests/fixtures/*.glb` into this folder,
   overwriting.
3. Delete the corresponding `.meta` files first if Unity's importer
   pinned any stale GUIDs (usually not necessary).

The Blender project's [`tests/fixtures/README.md`](https://github.com/kenjimeta/khr_character_blender/blob/master/tests/fixtures/README.md)
has the authoritative "adding a new variation" workflow.

## License

Every fixture in this folder is procedurally generated from primitive
meshes (UV sphere for the head, cylinder for the torso) and synthetic
armatures — no third-party geometry, textures, or scanned content.
Dedicated to the public domain under
[CC0 1.0](https://creativecommons.org/publicdomain/zero/1.0/) — same
license as the sibling `Synthetic/` set.
