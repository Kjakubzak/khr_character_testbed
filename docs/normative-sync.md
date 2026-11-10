# Normative draft integration

This testbed follows `Kjakubzak/glTF-private` `avatar_ext_normative` at
`314c95c571185776f8624ceb94773ce3b976ad91` (2 November 2026), the payload of KhronosGroup/glTF PR #2512 at
`7ab5368a`. It pins the UnityGLTF fork at `ca2b026b` (Kjakubzak/UnityGLTF PR #7), which implements that revision.
The draft remains subject to change. Runtime composition choices are documented host policy.

## Changes for 314c95c5

- **Suppression sums and clamps.** The attenuations of all suppressions that target an expression are summed, the
  sum is clamped to `[0,1]`, and `v[t] = u[t] * (1 - clamp(sum, 0, 1))`. Earlier drafts multiplied one factor per
  suppression. A suppression may carry an optional `name`, which labels it and is never compared with the target's
  name. `SandboxSuppressionSumTests` uses the hero's `happy` and `surprised` mouth suppressions, which share targets.
- **Scale and rotation.** Node and texture-transform `scale` SHOULD compose additively, `b + sum(s - n)` per
  component. Rotations SHOULD compose approximately additively for small rotations; the method is the host's. The
  pinned fork uses `normalize(b * exp(sum(log(n^-1 * s))))`, which is order-independent and exact for a shared axis.
  Boolean and integer properties still take the highest active expression index.
- **Visibility roles.** Each hint carries a `roles` array; roles in one hint are ORed, and node hints along the
  ancestor path, the primitive hint and `KHR_node_visibility` are ANDed. A standard role the application has no
  logic for applies in every view, so an application with no role logic hides nothing. Unrecognized custom roles
  are ignored. The hints MUST NOT be listed in `extensionsRequired`. View tags and the host role resolver are gone:
  queries take a `VisibilityRoleContext`, `VisibilityRoleContext.NoLogic` is the default, and
  `FirstPersonRolePolicy` applies `first_person` in the user's own first-person views and `third_person` in all
  other views. The VisibilityHints demo shows no logic, first-person, spectator and mirror views.
- **Camera hints.** `SC-Body` adds a `first_person` camera hint at eye height looking along the character's forward.
  A camera hint does not by itself make a visibility role apply.

## Wire and sample migration

`expressionSetMappings` maps external inputs to native expression drivers. An entry's `source` is the authoritative
native expression index; its optional `name` is independent metadata and may be empty. Each supplied input must be finite, including unknown keys, and is
clamped to `[0,1]`, contributions are summed, and the final native driver is clamped before binary conversion and
suppression. Missing inputs contribute zero. Mapping identifiers are nonempty aliases, including `vrm1_expressions`;
skeleton identifiers include `vrm1_humanoid`. Custom identifiers need no URI form. Skeleton association names are
independent labels. Reference poses require a nonempty `poseType`; they have no implicit classification.

`Tools/ci/normalize-character-fixtures.py` migrates earlier fixture names and aliases. Where an asset contains the
former separate input mapping, that authored external-input operation becomes the sole canonical mapping. It does
not calculate an inverse of the former output projection. Numeric indices preserve duplicate expression labels;
ambiguous legacy string references fail with an error. Animation channels, hierarchy, vendor data and binary
chunks are preserved. The committed Blender and VRM-origin examples have undergone this migration.

The script also migrates assets to the wire of `avatar_ext_normative` at `314c95c5` (KhronosGroup/glTF PR #2512 at
`7ab5368a`): a visibility hint's `role` becomes the `roles` array; the visibility hints and `KHR_node_camera_hint` are
removed from `extensionsRequired`; the made-up `KHR_mesh_annotation*` and `KHR_virtual_transforms` extensions are
deleted (a first- or third-person-only annotation becomes a `KHR_mesh_primitive_visibility_hint`); and index
properties written as strings become integers. Its docstring lists every rule. `Tools/ci/make_hero_variants.py`
normalizes the hero, restores its suppressions and rebuilds its three visibility-hint variants; `--check` verifies the
committed files.

## Integration cases

| Asset / test | Required behavior and host policy |
|---|---|
| `SC-SharedTargets` | Two mixed tracks remain valid while sharing translation, quaternion, scale and morph properties. Mandatory additive totals are `translation.x=1.7` and `weight=0.8`. Scale composes additively to `4`, and the two rotations about Z compose to `270°` under this host's rotation method. |
| Single response | Authored `b` differs from each sampled neutral `n`; a single active entry still uses `b+(s-n)` for additive properties. A half driver produces a 45° quaternion response without a second driver multiplication. Zero then positive sampling is stateless. |
| Canonical mapping | `Together=0.8` plus `Half=1` yields full native drivers `[1,0.8]`; input mutation cannot alter the immutable result. Native mapping precedes suppression. Independent labels, empty optional labels and known aliases are accepted. |
| `VH-Ancestors` | An `always` mesh and primitive below a `third_person` ancestor remain hidden in the first-person view. A nested `first_person` node is drawn only where both roles apply. With no role logic everything is drawn. Unannotated nodes retain visibility. |
| Custom roles | An unrecognized custom role such as `ACME_unknown` imposes no restriction, and cannot reopen another false ancestor or core visibility. When the application recognizes `ACME_mirror_only`, content with that role is drawn only in views where it applies, such as a mirror. |
| Views and instances | Repeated calls with different role contexts and two imported instances cannot leak visibility state. Primitive predicates AND the cumulative node result. |
| Core visibility | A false parent and true child preserve their own Boolean states. Imported renderer output is hidden while GameObjects and cameras remain active. STEP unsigned-byte pointer animation changes the Boolean, and the renderer follows it. The test verifies the actual imported curve binding and selects clamped host playback when sampling its final key; the importer's ordinary clips loop by default. Both scene import and standalone `LoadNodeAsync` attach the default core rendering adapter at the imported instance root. |
| Camera and animation | Hint-hidden ancestors still animate; nested cameras and transform paths remain active. Camera hints follow the complete core camera frame, including reflected/nonuniform transforms. Zero scale defines no initial pose and leaves the host camera unchanged. |
| Graphics | Red nested geometry appears in the matching view and disappears under an ancestor veto. A green backdrop behind it proves hidden geometry does not occlude color through a depth write. A view where both roles apply reveals the additional primitive, as does a view with no role logic. A repeated original view restores the original pixel count and material/state snapshots. |

`VH-Ancestors` and `SC-SharedTargets` are procedural CC0 assets. Regenerate their `.gltf`, `.bin` and `.glb` files with:

```sh
python3 Tools/ci/generate-normative-fixtures.py
python3 Tools/ci/generate-normative-fixtures.py --check
python3 Tools/ci/test-normalize-character-fixtures.py
```

Unity's `SampleCharacterFactory` packages those sources and includes both in the normalized golden/export gate.
The small wire fixtures cover importer cases the optional controller exporter cannot author losslessly.

## Run the checks

The default local test scripts run without graphics and exclude `Graphics` explicitly. That exclusion is a separate
scope, not a skipped or passed GPU test. Run the graphics class with a real graphics device:

```sh
bash Tools/ci/run-tests.sh --platform PlayMode
bash Tools/ci/run-tests.sh --platform PlayMode --graphics --category Graphics --min-tests 1 --min-sandbox-tests 1
```

PowerShell equivalents are `Run-Tests.ps1` and `Run-Tests.ps1 -Graphics -Category Graphics -MinTests 1 -MinSandboxTests 1`.
The graphics test fails if launched with a null device. Its measurement route selects Built-in for `Camera.Render`
and restores the prior pipeline settings. PNGs and pixel-count JSON land in `Artifacts/normative-render/`.
It proves direct color/depth behavior for that route. Shadows, reflections, indirect lighting and other pipelines
remain host policy and require their own output evidence; the hint specifications do not prescribe those policies.

Run all committed GLBs through the pinned validator and regenerate/check goldens as described in [CI](ci.md).
The default project is Unity 2022.2.23f1; [the isolated Unity 6 setup](../Tools/unity6/README.md) remains available.
Unity GitHub jobs require a configured CI licence; local Editor runs do not establish that remote gate.
