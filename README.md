# khr_character_testbed

A public, open-source example project that demonstrates the Khronos **`KHR_character` / avatar glTF extensions** (glTF PR #2512) in Unity, via a [UnityGLTF](https://github.com/Kjakubzak/UnityGLTF) fork that adds the `KHR_character` import/export plugin (pending upstream into [KhronosGroup/UnityGLTF](https://github.com/KhronosGroup/UnityGLTF)).

Clone it, press Play, and watch a character import, emote, gaze, animate, switch rigs, toggle first/third-person visibility, and round-trip through a **Khronos-neutral** glTF wire — all on license-clean sample assets.

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

---

## The demos

Launch any of these from **SampleHub**:

| Scene | Shows | Try this |
|---|---|---|
| **CharacterShowcase** | Drive a full character through every `KHR_character` capability | The combined demo — every capability on one character (hero asset, or `SC-FacePlus` fallback) |
| **GlbViewer** | Import any `.glb`/`.gltf` at runtime + capability discovery | Load a character; read the Active/Degraded/Inert capability list |
| **Expressions** | Morph + joint + texture expression control | Drag `jawOpen`, the jaw-bone slider, and the texture swap (snaps at 0.5) |
| **GazeAndCamera** | Expression-driven gaze + advisory camera hints | Move the target → eyes track it; click a camera-hint role |
| **RigAndPose** | Runtime Generic ↔ Humanoid rig switch + reference pose + no-T-pose-snap | Toggle to Humanoid (builds a Mecanim Avatar); "Apply Reference Pose" |
| **RoundTrip** | Export → re-import + **Khronos-neutrality** readout | Export in memory; see `extensionsUsed` (canonical `KHR_*`) and an **empty `extensionsRequired`** |
| **Health** | Per-capability Active/Degraded/Inert triage | Load a partial character; see what's inert |
| **VisibilityHints** | First/third-person view-context visibility (`KHR_node_visibility_hint` + `KHR_mesh_primitive_visibility_hint`) | Swap between the built-in figure and the khr-character example variants; toggle first-person — head/eyes/brows hide (via an invisible-material swap) while hair + body stay |
| **HumanoidAnimation** | Procedural clips on a humanoid rig | Play a wave / nod / idle-sway |
| **AnimationRigging** | Runtime aim constraint (Unity Animation Rigging) | Move the target; a bone tracks it |
| **AnimationSandbox** | Any character + any rig mode + any clip | Defaults to Generic; pick a character-adaptive procedural clip and Play (clips are filtered to the compatible rig mode) |

Every scene carries an on-screen **caveat banner** so the demo never over-promises (e.g. one character per document; the camera index doesn't round-trip). The caveats come from a single registry (`Assets/Samples/_Shared/Caveats.cs`) and are indexed once in [`docs/caveats.md`](docs/caveats.md).

---

## Editor tools

Under **Assets ▸ UnityGLTF ▸ KHR Character**:

- **Enable Plugins** — turns on the (disabled-by-default) KHR character import + export plugins.
- **Generate Sample Characters** — builds the `SC-*` sample GLBs *in code* and **dogfood-exports** them through the plugin (so the samples themselves are produced by the exporter under test).
- **Build Sample Scenes** — (re)generates the demo scenes and registers them in Build Settings.

---

## Sample assets

Three sources under `Assets/SampleAssets/` (CC0 / synthetic, or VRM-origin consumed vendor-neutrally — see `Assets/SampleAssets/ATTRIBUTION.md`):

**`Synthetic/`** — CC0 characters built in code and dogfood-exported through the plugin:

| Asset | Carries | Used by |
|---|---|---|
| `SC-Face.glb` | `KHR_character` + expression (morph + joint) | GlbViewer, Expressions, Gaze |
| `SC-FacePlus.glb` | + texture expression (UV transform + index-swap) | Expressions, RoundTrip |
| `SC-Body.glb` | skeleton mapping + reference pose + camera hint (humanoid-mappable) | RigAndPose, Health, procedural animation |

**`VRM_KHR_Examples/`** — the "hero" character (`khr-character-example.glb`, VRM-origin, consumed via `KHR_character`; `VRMC_*` ignored) plus per-role visibility-hint variants (`-always`, `-first-person`, `-third-person`), used by CharacterShowcase and VisibilityHints.

**`FromBlender/`** — fixtures authored by the sibling [`khr_character_blender`](https://github.com/kenjimeta/khr_character_blender) addon (one per extension combination, incl. `visibility_hints.glb`), so the importer is tested against Blender-authored wire too.

---

## How it consumes the plugin

This project is a **pure UPM consumer** — it vendors no plugin code. `Packages/manifest.json` pins UnityGLTF (a fork that adds the `KHR_character` plugin) by commit SHA via a Git URL, and `Packages/packages-lock.json` records the resolved SHA for reproducible builds:

```jsonc
"org.khronos.unitygltf": "https://github.com/Kjakubzak/UnityGLTF.git#e2d6c9002a6e46495572d0584e04cd657259c614",
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
  Tests/          PlayMode tests (demo spine + a neutral-wire smoke test)
Packages/         manifest.json (pinned plugin) + packages-lock.json (SHA)
```

---

## Running the tests

PlayMode tests cover the full demo spine and the core KHR_character behaviors (expressions, gaze, rig switch, round-trip, neutrality) plus a scene-smoke gate, and act as an anti-hollow gate — they fail to compile if the plugin dependency goes missing. In the Editor: **Window ▸ General ▸ Test Runner ▸ PlayMode ▸ Run All**. Headless:

```bash
Unity -batchmode -projectPath . -runTests -testPlatform PlayMode -testResults results.xml -logFile -
```

---

## Khronos neutrality

The whole point: an exported character carries **zero vendor tokens** and marks **nothing** in `extensionsRequired` (canonical `KHR_*` in `extensionsUsed` only). A vendor-agnostic glTF viewer still loads and renders the base mesh — the RoundTrip demo shows this live.

---

## License

[MIT](LICENSE). Third-party / sample-asset provenance and licenses are in `THIRD_PARTY_NOTICES.md` and `Assets/SampleAssets/ATTRIBUTION.md`.
