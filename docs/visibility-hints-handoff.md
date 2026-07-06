# VisibilityHints Demo — Hand-off Checklist

One-time steps to land the **VisibilityHints** UnityGLTF plugin and light up its testbed demo. The testbed
C# is already written (controller, enable-plugins menu, SceneBuilder/hub wiring, tests); the remaining steps
are the two things only Unity/git can produce — `.meta` files and the built `.unity` scene — plus publishing
the plugin so this project can see it.

For the general "how demos are structured" pattern, see [`adding-a-demo.md`](./adding-a-demo.md). This doc is
the specific landing procedure for VisibilityHints.

## Why this is needed

This project consumes UnityGLTF as a **pinned git package** (`Packages/manifest.json`):

```json
"org.khronos.unitygltf": "https://github.com/Kjakubzak/UnityGLTF.git#2c5c4f3067f187aa2d33f283605300aea976c253"
```

The VisibilityHints plugin is **not** in that pinned commit yet (it is currently untracked local files in the
UnityGLTF working copy, with **no `.meta` files**). Until it is committed with metas, pushed to the fork, and
the pin is bumped, this project cannot compile the new `Samples.VisibilityHints` / plugin-menu / test code, and
any committed scene referencing the plugin's components would load with "missing script" references.

## A. Land the plugin (UnityGLTF repo)

Repo: `…/GitHub/UnityGLTF` (untracked: `Runtime/Scripts/VisibilityHints/`, `Editor/Scripts/VisibilityHints/`,
`Tests/Runtime/VisibilityHints/`).

1. **Generate `.meta` files.** Open the UnityGLTF repo in Unity (its root is a Unity project) — or any project
   that references it via a local `file:` path (e.g. `khr-test-proj`, whose manifest points at
   `file:../../GitHub/UnityGLTF`). Unity writes `.meta` files in place for every new `.cs` and `.asmdef`.
   - A git-URL consumer (like this testbed) will **not** generate them in the source — it caches the package in
     `Library/PackageCache`. Metas must be committed in the package repo so GUIDs are stable across machines.
2. **Commit** the three VisibilityHints folders **including the generated `.meta` files**.
3. **Push** to the fork `github.com/Kjakubzak/UnityGLTF` (the branch this testbed tracks) and note the new
   **commit hash**.

## B. Point this testbed at it

Repo: `…/GitHub/khr_character_testbed`.

4. **Bump the pin** in `Packages/manifest.json`: replace `#2c5c4f30…` with the new commit hash from step 3.
5. **Open the testbed in Unity.** It re-resolves the package (now including VisibilityHints); the pre-written
   testbed code should compile (the `Samples.VisibilityHints` / `UnityGLTF.VisibilityHints.*` asmdef references
   now resolve).
6. **Enable the plugins** (both are disabled by default, non-ratified):
   **Assets ▸ UnityGLTF ▸ Visibility Hints ▸ Enable Plugins**. (Optional: the KHR Character enable-plugins item
   too, if you also want character round-trips.)
7. **Build the scene:** **Assets ▸ UnityGLTF ▸ KHR Character ▸ Build Sample Scenes**. This generates
   `Assets/Samples/VisibilityHints/Scenes/VisibilityHints.unity` and adds it to Build Settings (alongside the
   existing demos).

## C. Verify

8. Play `Assets/Samples/VisibilityHints/Scenes/VisibilityHints.unity` (or **SampleHub ▸ Visibility Hints**) and
   toggle **First-person view**: the head hides, the arms + visor accent appear (and restore when toggled back).
9. **Test Runner (PlayMode):**
   - `SandboxVisibilityHintsTests.VisibilityHintsScene_TogglesRendererByViewContext`
   - `SandboxSceneSmokeTests.DemoScene_BootsCleanly` — the `VisibilityHints` case.

## D. Commit the testbed changes

Commit these together (let Unity generate `.meta` for the new files first):

- **New:** `Assets/Samples/VisibilityHints/` (asmdef, `VisibilityHintsController.cs`, and the generated
  `Scenes/VisibilityHints.unity`), `Assets/Editor/VisibilityHintsPluginsMenu.cs`,
  `Assets/Tests/SandboxVisibilityHintsTests.cs` — each **with its `.meta`**.
- **Edited:** `Assets/Editor/SceneBuilder.cs`, `Assets/Editor/Samples.Editor.asmdef`,
  `Assets/Samples/_Shared/DemoHubRegistry.cs`, `Assets/Tests/SandboxSceneSmokeTests.cs`,
  `Assets/Tests/Sandbox.Tests.asmdef`.
- **Generated/updated by Unity:** `Packages/manifest.json` (the bumped pin), `ProjectSettings/EditorBuildSettings.asset`
  (the new scene), and all new `.meta` files.

## Notes

- The demo figure is built **procedurally at runtime** in `VisibilityHintsController.Start()` (no imported
  asset), so the committed `.unity` holds only the controller + camera/orbit/render-pipeline/EventSystem infra —
  the hinted parts appear on Play. This matches the other demos.
- The two steps that must happen in Unity (not scriptable here): **`.meta` generation** (step 1) and the
  **scene build** (step 7).
