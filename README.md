# khr_character_testbed

Optional [isolated Unity 6 setup](Tools/unity6/README.md) is available alongside this Unity 2022 project.

A public, open-source example project that demonstrates the Khronos **`KHR_character` / avatar glTF extensions** (glTF PR #2512) in Unity, via a [UnityGLTF](https://github.com/Kjakubzak/UnityGLTF) fork that adds the `KHR_character` import/export plugin (pending upstream into [KhronosGroup/UnityGLTF](https://github.com/KhronosGroup/UnityGLTF)).

Clone it, press Play, and watch a character import, emote, gaze, animate, switch rigs, toggle first/third-person visibility, and round-trip through this testbed's deliberately strict **KHR-only export profile** — all on license-clean sample assets.

> ⚠️ Tracks the **non-ratified** `KHR_character_*` extensions (glTF PR #2512). Schema and behavior may change as the proposal evolves. Both KHR character plugins are **disabled by default** and enabled by this project.

---

## Requirements

- **Unity 2022.2.23f1** (Built-in Render Pipeline; URP works as an upgrade)
- **Git** + **Git LFS** (`git lfs install`) — sample binaries are stored via LFS
- Internet on first open (resolves the UnityGLTF package from a pinned Git URL)

---

## Quickstart (5 minutes, zero code)

1. **Clone** (with LFS):
   ```bash
   git lfs install
   git clone <this-repo-url>
   ```
2. **Open** the folder in **Unity 2022.2.23f1**. First open resolves the UnityGLTF + `KHR_character` plugin from `Packages/manifest.json` (pinned by commit SHA) — give it a moment.
3. **First time only** — if `Assets/SampleAssets/Synthetic/*.glb` or the demo scenes are missing, generate them from the menu:
   - **Assets ▸ UnityGLTF ▸ KHR Character ▸ Generate Sample Characters**
   - **Assets ▸ UnityGLTF ▸ KHR Character ▸ Build Sample Scenes**
   *(Plugins ship enabled; if needed, **Assets ▸ UnityGLTF ▸ KHR Character ▸ Enable Plugins**.)*
4. **Open** `Assets/Samples/_Shared/Scenes/SampleHub.unity` and press **Play**.
5. Click **Expressions**, drag a slider → **the face moves.** First success. 🎉

Use the in-scene **Back to Hub** button to move between demos.

`KHR_character_expression` defines the response evaluator and a normative composition stage. Each update takes one
driver snapshot. An entry whose effective driver `e` is positive is sampled at `t = e * T` on each channel the
consumer supports: a channel listed by a derived extension the consumer supports (`_morphtarget`, `_joint`,
`_texture_transform`) whose target extensions, such as `KHR_animation_pointer` and `KHR_texture_transform`, it also
supports. Each derived extension is a separate conformance claim, so a consumer that supports only the base
extension animates nothing; the `KHR_character` plugin supports all three. Additive components compose over the
authored value as `y = b + sum(s - n)`, with no clamp; node and texture scale compose the same way. Rotations
compose approximately additively, by a method the host chooses; this Unity adapter uses
`normalize(b * exp(sum(log(n^-1 * s))))`, which is exact for a shared axis. Boolean and integer properties take the
sample of the highest active expression index. Mixing with other animation writers is also host-defined. Imports are passive by default (the `ExpressionResponseSet` computes but writes nothing); only
**Expressions**, **GazeAndCamera**, and **CharacterShowcase** explicitly opt into UnityGLTF's `ExpressionController`,
which applies the composed result to blendshapes, transforms, and `MaterialPropertyBlock`s. That scene writing is
one Unity integration route among many.

Mapping sets use the sole `expressionSetMappings` property. Their keys identify external input vocabularies;
`source` references the native expression array by index. Each input is clamped, its contributions are summed per
native expression, and that final sum is clamped before suppression. Labels do not participate in resolution.
Suppressions that share a target sum their attenuations, and the sum is clamped to [0, 1] before it is applied.

Each visibility hint lists one or more `roles`, which are ORed. Node hints along the glTF ancestor path, the
primitive hint and the independent core `KHR_node_visibility` result are ANDed, so a descendant's `always` role
cannot restore ancestor-hidden content. A standard role the application has no logic for applies everywhere, and an
unrecognized custom role is ignored, so with no role logic nothing is hidden. The hints are never listed in
`extensionsRequired`. Hints leave cameras, animation and transforms active. Shadow,
reflection and lighting behavior is the application's policy. See [the normative sync test plan](docs/normative-sync.md).

---

## The demos

Launch any of these from **SampleHub**:

| Scene | Shows | Try this |
|---|---|---|
| **CharacterShowcase** | Drive a full character through every `KHR_character` capability | The combined demo — every capability on one character (hero asset, or `SC-FacePlus` fallback) |
| **GlbViewer** | Import any `.glb`/`.gltf` at runtime + capability discovery | Load a character; read the Active/Degraded/Inert capability list |
| **Expressions** | Morph + joint + texture-transform expression control | Drag `jawOpen`, the jaw-bone slider, and the `texFx` texture-transform slider |
| **GazeAndCamera** | Expression-driven gaze + advisory camera hints | Move the target → eyes track it; click a camera-hint role |
| **RigAndPose** | Runtime Generic ↔ Humanoid rig switch + reference pose | Toggle to Humanoid (builds a Mecanim Avatar); "Apply Reference Pose" |
| **RoundTrip** | Lossless host-data export plus passive-expression safety | Round-trip SC-Body; load an expression asset to see lossy legacy-controller export fail closed |
| **Health** | Per-capability Active/Degraded/Inert triage | Load a partial character; see what's inert |
| **VisibilityHints** | Role-based visibility (`KHR_node_visibility_hint` + `KHR_mesh_primitive_visibility_hint`) | Swap between the built-in figure and the khr-character example variants; pick the role logic (no logic, first-person, spectator, mirror) — in the first-person view `third_person` content such as the head/eyes/brows hides (via an invisible-material swap), the figure's status badge (`first_person` or a custom mirror role) shows in the first-person and mirror views only, and `always` content such as hair + body stays when no ancestor hint vetoes it |
| **HumanoidAnimation** | Procedural clips on a humanoid rig | Play a wave / nod / idle-sway |
| **AnimationRigging** | Runtime aim constraint (Unity Animation Rigging) | Move the target; a bone tracks it |
| **AnimationSandbox** | Any character + any rig mode + any clip | Defaults to Generic; pick a character-adaptive procedural clip and Play (clips are filtered to the compatible rig mode) |

Every scene carries an on-screen **caveat banner** so the demo never over-promises (e.g. at most one character per asset; the camera index doesn't round-trip). The caveats come from a single registry (`Assets/Samples/_Shared/Caveats.cs`) and are indexed once in [`docs/caveats.md`](docs/caveats.md).

---

## Editor tools

Under **Assets ▸ UnityGLTF ▸ KHR Character**:

- **Enable Plugins** — turns on the (disabled-by-default) KHR character import + export plugins.
- **Generate Sample Characters** — builds the `SC-*` sample GLBs in code and exports them through the plugin; also packages the procedural `SC-SharedTargets` and `VH-Ancestors` integration fixtures.
- **Build Sample Scenes** — (re)generates the demo scenes and registers them in Build Settings.

---

## Sample assets

Three sources under `Assets/SampleAssets/` (CC0 / synthetic, or VRM-origin consumed through its `KHR_character` data while vendor extensions are ignored — see `Assets/SampleAssets/ATTRIBUTION.md`):

**`Synthetic/`** — CC0 characters and integration fixtures built in code. Controller-authored characters are
exported through the plugin; focused wire fixtures are packaged from their procedural sources:

| Asset | Carries | Used by |
|---|---|---|
| `SC-Face.glb` | `KHR_character` + expression (morph + joint) | GlbViewer, Expressions, Gaze |
| `SC-FacePlus.glb` | + texture-transform expression (`KHR_character_expression_texture_transform` on a `KHR_texture_transform` offset) | Expressions |
| `SC-Body.glb` | skeleton mapping + reference pose + portrait and first-person camera hints (humanoid-mappable) | RigAndPose, Health, procedural animation |
| `SC-SharedTargets.glb` | Shared additive translation/morph plus quaternion/scale responses and external mapping | Normative integration tests |
| `VH-Ancestors.glb` | Nested node/primitive hints, custom roles, core Boolean visibility, camera and ordinary animation | Normative integration and GPU color/depth tests |

**`VRM_KHR_Examples/`** — the "hero" character (`khr-character-example.glb`, VRM-origin, consumed via `KHR_character`; `VRMC_*` ignored) plus per-role visibility-hint variants (`-always`, `-first-person`, `-third-person`), used by CharacterShowcase and VisibilityHints. The committed wire has been normalized to the current draft. Shared eye rotations are valid; expression demos use the hero when available and synthetic fallbacks when it is absent.

**`FromBlender/`** — fixtures authored by the sibling [`khr_character_blender`](https://github.com/kenjimeta/khr_character_blender) addon (one per extension combination, incl. `visibility_hints.glb`), so the importer is tested against Blender-authored wire too.

---

## How it consumes the plugin

This project is a **pure UPM consumer** — it vendors no plugin code. `Packages/manifest.json` pins UnityGLTF (a fork that adds the `KHR_character` plugin) by commit SHA via a Git URL, and `Packages/packages-lock.json` records the resolved SHA for reproducible builds:

```jsonc
"org.khronos.unitygltf": "https://github.com/Kjakubzak/UnityGLTF.git#ca2b026b3bc82a37ea412dba815d0f771b70b383",
"testables": ["org.khronos.unitygltf"]
```

`Packages/manifest.json` is the source of truth for this pin; CI runs `Tools/ci/check-package-pin.sh` (a no-Unity preflight) to fail fast if the pinned URL/SHA is unreachable or drifts from this README / `packages-lock.json`.

---

## Project layout

```
Assets/
  Samples/
    _Shared/      shared infra (RP bootstrap, orbit camera, uGUI panel, SampleHub) + scenes
    GlbViewer/    runtime GLB viewer demo
    KhrCharacter/ Expressions / GazeAndCamera / RigAndPose / RoundTrip / Health / CharacterShowcase demos
  SampleAssets/   generated SC-* GLBs + ATTRIBUTION.md
  Editor/         Enable Plugins / Generate Sample Characters / Build Sample Scenes
  Tests/          PlayMode tests (demo spine + a KHR-only-profile smoke test)
Packages/         manifest.json (pinned plugin) + packages-lock.json (SHA)
```

---

## Running the tests

PlayMode tests cover the full demo spine and the core KHR_character behaviors (expressions, gaze, rig switch, round-trip, and the testbed's KHR-only export profile) plus a scene-smoke gate, and act as an anti-hollow gate — they fail to compile if the plugin dependency goes missing. In the Editor: **Window ▸ General ▸ Test Runner ▸ PlayMode ▸ Run All**. Headless:

```bash
Unity -batchmode -projectPath . -runTests -testPlatform PlayMode -testResults results.xml -logFile -
```

---

## KHR-only testbed export profile

Controller-authored fixtures deliberately carry only `KHR_*` tokens and mark nothing in `extensionsRequired`.
That is a project policy, not a glTF namespace rule: `EXT_*` is also reserved for Khronos multi-vendor extensions,
and an extension may be validly required when its specification permits it. Because nothing is required, a viewer
without `KHR_character` support (for example the public glTF Sample Viewer) loads the fixtures, but it also lists
and may auto-play the expression and reference-pose animations as ordinary clips; this testbed's importer keeps
them out of its clip pickers. Imported passive expression data is not treated as controller-authored: export fails
closed until a lossless passive writer exists. The RoundTrip demo shows both boundaries.

---

## License

[MIT](LICENSE). Third-party / sample-asset provenance and licenses are in `THIRD_PARTY_NOTICES.md` and `Assets/SampleAssets/ATTRIBUTION.md`.
