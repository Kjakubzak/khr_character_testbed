# khr_character_testbed

A public, open-source example project that demonstrates the Khronos **`KHR_character` / avatar glTF extensions** (glTF PR #2512) in Unity, via the open-source [UnityGLTF](https://github.com/KhronosGroup/UnityGLTF) package.

Clone it, press Play, and watch a character import, emote, gaze, switch rigs, and round-trip through a **Khronos-neutral** glTF wire — all on license-clean sample assets.

> ⚠️ Tracks the **non-ratified** `KHR_character_*` extensions (glTF PR #2512). Schema and behavior may change as the proposal evolves. Both KHR character plugins are **disabled by default** and enabled by this project.

---

## Requirements

- **Unity 6000.0.76f1** (Built-in Render Pipeline; URP works as an upgrade)
- **Git** + **Git LFS** (`git lfs install`) — sample binaries are stored via LFS
- Internet on first open (resolves the UnityGLTF package from a pinned Git URL)

---

## Quickstart (5 minutes, zero code)

1. **Clone** (with LFS):
   ```bash
   git lfs install
   git clone <this-repo-url>
   ```
2. **Open** the folder in **Unity 6000.0.76f1**. First open resolves the UnityGLTF + `KHR_character` plugin from `Packages/manifest.json` (pinned by commit SHA) — give it a moment.
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
| **GlbViewer** | Import any `.glb`/`.gltf` at runtime + capability discovery | Load a character; read the Active/Degraded/Inert capability list |
| **Expressions** | Morph + joint + texture expression control | Drag `jawOpen`, the jaw-bone slider, and the texture swap (snaps at 0.5) |
| **GazeAndCamera** | Expression-driven gaze + advisory camera hints | Move the target → eyes track it; click a camera-hint role |
| **RigAndPose** | Runtime Generic ↔ Humanoid rig switch + reference pose + no-T-pose-snap | Toggle to Humanoid (builds a Mecanim Avatar); "Apply Reference Pose" |
| **RoundTrip** | Export → re-import + **Khronos-neutrality** readout | Export in memory; see `extensionsUsed` (canonical `KHR_*`) and an **empty `extensionsRequired`** |
| **Health** | Per-capability Active/Degraded/Inert triage | Load a partial character; see what's inert |

Every scene carries an on-screen **caveat banner** so the demo never over-promises (e.g. one character per document; the camera index doesn't round-trip).

---

## Editor tools

Under **Assets ▸ UnityGLTF ▸ KHR Character**:

- **Enable Plugins** — turns on the (disabled-by-default) KHR character import + export plugins.
- **Generate Sample Characters** — builds the `SC-*` sample GLBs *in code* and **dogfood-exports** them through the plugin (so the samples themselves are produced by the exporter under test).
- **Build Sample Scenes** — (re)generates the demo scenes and registers them in Build Settings.

---

## Sample assets

Generated into `Assets/SampleAssets/Synthetic/` — all CC0 / synthetic, no third-party art (see `Assets/SampleAssets/ATTRIBUTION.md`):

| Asset | Carries | Used by |
|---|---|---|
| `SC-Face.glb` | `KHR_character` + expression (morph + joint) | GlbViewer, Expressions, Gaze |
| `SC-FacePlus.glb` | + texture expression (UV transform + index-swap) | Expressions, RoundTrip |
| `SC-Body.glb` | skeleton mapping + reference pose + camera hint (humanoid-mappable) | RigAndPose, Health |

---

## How it consumes the plugin

This project is a **pure UPM consumer** — it does not fork UnityGLTF. `Packages/manifest.json` pins the package by commit SHA via a Git URL, and `Packages/packages-lock.json` records the resolved SHA for reproducible builds:

```jsonc
"org.khronos.unitygltf": "https://github.com/<owner>/UnityGLTF.git#<commit-sha>",
"testables": ["org.khronos.unitygltf"]
```

---

## Project layout

```
Assets/
  Samples/
    _Shared/      shared infra (RP bootstrap, orbit camera, uGUI panel, SampleHub) + scenes
    GlbViewer/    runtime GLB viewer demo
    KhrCharacter/ Expressions / GazeAndCamera / RigAndPose / RoundTrip / Health demos
  SampleAssets/   generated SC-* GLBs + ATTRIBUTION.md
  Editor/         Enable Plugins / Generate Sample Characters / Build Sample Scenes
  Tests/          PlayMode tests (M1–M6 + a neutral-wire smoke test)
Packages/         manifest.json (pinned plugin) + packages-lock.json (SHA)
```

---

## Running the tests

PlayMode tests cover the full demo spine (M1–M6) and act as an anti-hollow gate — they fail to compile if the plugin dependency goes missing. In the Editor: **Window ▸ General ▸ Test Runner ▸ PlayMode ▸ Run All**. Headless:

```bash
Unity -batchmode -projectPath . -runTests -testPlatform PlayMode -testResults results.xml -logFile -
```

---

## Khronos neutrality

The whole point: an exported character carries **zero vendor tokens** and marks **nothing** in `extensionsRequired` (canonical `KHR_*` in `extensionsUsed` only). A vendor-agnostic glTF viewer still loads and renders the base mesh — the RoundTrip demo shows this live.

---

## License

[MIT](LICENSE). Third-party / sample-asset provenance and licenses are in `THIRD_PARTY_NOTICES.md` and `Assets/SampleAssets/ATTRIBUTION.md`.
