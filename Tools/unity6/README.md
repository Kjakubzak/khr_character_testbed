# Isolated Unity 6 testbed

The default project remains on Unity 2022.2.23f1. This optional setup creates a separate worktree for **Unity 6000.5.4f1**, with its own Library and temporary files. It uses an immutable UnityGLTF compatibility pin and preserves asset GUIDs.

## Prepare

Install Git LFS and Unity 6000.5.4f1, then run from this repository:

```sh
git lfs install
bash Tools/unity6/prepare-testbed.sh --output ../khr_character_testbed-unity6
```

The destination must be absent and its parent directory must exist. The script refuses an existing destination or one inside the source checkout. It verifies the patch before creating the worktree. The generated worktree intentionally has local project-version/package/compatibility changes; the source checkout keeps its Unity 2022 settings.

The patch supplies the native Unity 6 project-settings migration, URP 17.5.0, Test Framework 1.7.0, Animation Rigging 1.4.1 and UGUI 2.5.0. Character animation cache keys use Unity's EntityId API in 6000.5 and later; earlier versions retain integer instance IDs. The manifest, package lock and generated README share the same immutable UnityGLTF pin.

## Open the hub

On macOS, from the generated worktree:

```sh
editor='/Applications/Unity/Hub/Editor/6000.5.4f1/Unity.app/Contents/MacOS/Unity'
"$editor" -projectPath "$PWD" -accept-apiupdate \
  -executeMethod Samples.Editor.Unity6HubLauncher.OpenSampleHubAndPlay
```

On other platforms, use the equivalent installed Editor executable. The launcher opens SampleHub, enables the project-owned character plugins, initializes Unity Search in Edit mode, and then enters Play. The menu item is **Samples → Unity 6 → Open SampleHub and Play**.

`Logs/unity6/readiness.json` records the active camera, eleven demo choices, enabled plugins, Play state and captured errors. Successful runtime readiness requires `ready`, `playing` and `screenshotWritten` to be true with `runtimeErrors` equal to zero. Inspect `Logs/unity6/samplehub.png` and the Editor log as well. The hub intentionally begins without a loaded character; select a demo to continue.

## Test

Close that project's Editor normally before running its batch tests:

```sh
bash Tools/ci/run-tests.sh --project "$PWD" --platform PlayMode \
  --min-tests 203 --min-sandbox-tests 203 --timeout 10
```

The Git package consumer runs the 203 testbed cases. Package tests require a separate local-package consumer; that full suite passed 3 EditMode and 723 PlayMode cases in the compatibility setup. A rendered hub and headless tests do not establish each demo control or depth/shadow/outline/light-output conformance.

## Limits

**Unity 6 uses the default glTF asset Inspector.** The compatibility package excludes its obsolete internal custom importer tabs and deprecated Shader Graph override workflow. Unity 2022 retains those interfaces. A port to supported Unity Editor APIs is required for full Editor feature parity.

GitHub's Unity tests and round-trip goldens still require a valid CI-compatible Unity licence. Preparing this local worktree does not configure a GitHub licence or change the default CI gates.
