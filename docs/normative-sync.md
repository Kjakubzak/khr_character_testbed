# Normative draft integration

This testbed follows `Kjakubzak/glTF-private` `avatar_ext_normative` at
`9dc0781cebd1a24a0869aa98d2a3b902754ecd92` (21 October 2026).
The draft remains subject to change. Runtime composition choices are documented host policy.

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
ambiguous legacy string references fail with an error. Animation channels, hierarchy, hints, vendor data and binary
chunks are preserved. The committed Blender and VRM-origin examples have undergone this migration.

## Integration cases

| Asset / test | Required behavior and host policy |
|---|---|
| `SC-SharedTargets` | Two mixed tracks remain valid while sharing translation, quaternion, scale and morph properties. Mandatory additive totals are `translation.x=1.7` and `weight=0.8`; this Unity host selects the highest active entry's absolute scale/quaternion sample. |
| Single response | Authored `b` differs from each sampled neutral `n`; a single active entry still uses `b+(s-n)` for additive properties. A half driver produces a 45° quaternion response without a second driver multiplication. Zero then positive sampling is stateless. |
| Canonical mapping | `Together=0.8` plus `Half=1` yields full native drivers `[1,0.8]`; input mutation cannot alter the immutable result. Native mapping precedes suppression. Independent labels, empty optional labels and known aliases are accepted. |
| `VH-Ancestors` | An `always` mesh and primitive below a `third_person` ancestor remain hidden in a first-person-only view. A nested first-person node needs both tags. Unannotated nodes retain visibility. |
| Custom roles | Unsupported roles use the application fallback locally. A fallback or supported custom role cannot reopen another supported false ancestor or core visibility. `ACME_mirror_only` is interpreted by an explicit host resolver. |
| Views and instances | Repeated calls with different tags and two imported instances cannot leak visibility state. Primitive predicates AND the cumulative node result. |
| Core visibility | A false parent and true child preserve their own Boolean states. Imported renderer output is hidden while GameObjects and cameras remain active. STEP unsigned-byte pointer animation changes the Boolean, and the renderer follows it. The test verifies the actual imported curve binding and selects clamped host playback when sampling its final key; the importer's ordinary clips loop by default. Both scene import and standalone `LoadNodeAsync` attach the default core rendering adapter at the imported instance root. |
| Camera and animation | Hint-hidden ancestors still animate; nested cameras and transform paths remain active. Camera hints follow the complete core camera frame, including reflected/nonuniform transforms. Zero scale defines no initial pose and leaves the host camera unchanged. |
| Graphics | Red nested geometry appears in the matching view and disappears under an ancestor veto. A green backdrop behind it proves hidden geometry does not occlude color through a depth write. Both tags reveal the additional primitive. A repeated original view restores the original pixel count and material/state snapshots. |

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
