# KHR_character sample characters

Real characters carrying the KHR_character extension family (KhronosGroup/glTF PR 2512 at `7ab5368a`), exported from Blender with a fork of the Khronos glTF-Blender-IO add-on.

| Folder | Character | Extensions | Licence |
|---|---|---|---|
| `pubchar/` | Android, LOD2 (main sample). 90 joints, 52 mapped roles, `t_pose` + `a_pose`, 190 expressions, 17 `vrm1_expressions` presets, suppressions, 3 camera hints, look-at target. Clips: `face_expression`, `dance`, plus retargeted motion | KHR_character, _skeleton_mapping, _reference_pose, _expression (+ morphtarget, joint, mapping, suppression), KHR_node_camera_hint, KHR_node_lookat_target | CC BY-SA 4.0, Squarebit Studios |
| `pubchar_arkit/` | Android, ARKit-52 variant: 52 ARKit-named morph expressions plus 4 eye-joint looks | as above | CC BY-SA 4.0, Squarebit Studios |
| `goblin/` | Goblin. 49 roles, `t_pose`, 38 expressions, 10 presets, 8 camera hints from the authored cameras, look-at target, primitive visibility hints | as above + KHR_mesh_primitive_visibility_hint | CC BY-SA 4.0, Meta |
| `proxy/`, `synthetic_face/` | Procedural test characters | subsets | CC0 1.0 |

`character.json` beside each GLB lists what the tests expect, generated from the GLB. `clips~/` holds the retarget source clips (100STYLE, CMU, pixiv `test.vrma`, a ROM sweep). Unity does not import that folder; the harness loads the clips at runtime through `clips.json`. Full attribution is in `../ATTRIBUTION.md` and `/THIRD_PARTY_NOTICES.md`.

## Running

- **Tests:** PlayMode, category `Characters` (Test Runner, or `-runTests -testPlatform PlayMode -testCategory Characters`). With UnityGLTF `cb3918e7` everything passes except `CameraHintsAndLookAt` on the three real characters, because UnityGLTF does not import a camera hint's projection yet.
- **Stills and video:** menu **Samples → Characters → Capture Stills** or **Record Video** (Unity Recorder). Optional environment variables: `KHR_EVIDENCE_DIR`, `KHR_VIDEO_DIR`, `KHR_CHARACTERS` (comma-separated subset).
- **UnityGLTF editor import:** the KHR_character import plugin is enabled per asset in each `.glb.meta`; keep those `.meta` files.

Live retargeting goes through Unity Humanoid: UnityGLTF builds the Avatar from the KHR skeleton mapping and reference pose, and the harness replays muscle-space poses. Correctives baked into the android's own clips, and the goblin's skirt, do not move under live retargeting.
