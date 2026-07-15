# CI & Validation

This repo is a **real Unity project** that consumes the UnityGLTF / KHR Character package and tests it. CI is built
from small, reusable, cross-platform scripts under `Tools/ci/` (PowerShell `*.ps1` for local/Windows, bash `*.sh`
for Linux CI). Contributors run the **same** scripts CI runs, so there is no drift.

## The four merge gates

| # | Gate | Script | Pass criteria |
|---|------|--------|---------------|
| 1 | **Compile clean** | `Run-Tests` (Phase A) / `Warm-Library` | Unity exits 0 **and** no `error CS` in the compile log. Runs *before* any test pass — a `-runTests` against uncompilable code hangs forever, so we fail fast instead. |
| 2 | **Tests green + floor** | `Run-Tests` | Every NUnit run has `failed == 0`, `inconclusive == 0`, `skipped == 0`, the results XML is present, `total >= floor` (default **120**), **and** `sandbox >= sub-floor` (default **120**, the testbed's own `KhrCharacterTestbed.*` cases). Only the testbed's own tests run here (Unity `testables` does **not** surface a git-package's tests into a consumer), so a hollow package resolve fails **GATE 1 compile** (the `Sandbox.Tests` assembly links real plugin types); the floor kills the "0 tests ran = false green" trap. |
| 3 | **glTF-Validator** | `Validate-Glb` | The official Khronos `gltf_validator` reports `numErrors == 0` on every exported GLB. Independent, spec-authoritative — catches a bad wire even if our own tests have a bug. |
| 4 | **Round-trip goldens** | `Export-Goldens` | The normalized wire snapshot of each fixture matches its committed golden. Each FLOAT accessor is **decoded to its actual values (rounded 1e-5)** and byte-packing fields are dropped, so an interior value change can't hide behind unchanged `min`/`max` and packing jitter can't false-diff. Catches structural drift (new/renamed key, reordered array, changed value) that value round-trip tests don't. |

> **Why the floor matters most:** the most dangerous CI failure is a *silent* one — a project that compiles nothing
> and reports a green 0-test run. The floor makes that specific regression impossible to merge.

## Min-test-count floor

Two independent floors, both enforced every run (and all of `failed`/`inconclusive`/`skipped` must be 0):

- **Total floor — default 120.** Only the testbed's own tests run in this consumer: Unity `testables` does **not**
  surface a git-package's tests into a consuming project (the plugin's ~165 cases run in the plugin repo /
  `khr-test-proj`, not here). A hollow package resolve instead fails **GATE 1 (compile)**, since `Sandbox.Tests`
  links real plugin types. This floor guards the "0 tests ran = false green" trap. Parameterized:
  `Run-Tests -MinTests <n>` (PS) / `run-tests.sh --min-tests <n>` (bash), and `MIN_TESTS` in `.github/workflows/ci.yml`.
- **Independent sandbox sub-floor — default 120.** Counts only the testbed's own cases (NUnit `classname` under
  `KhrCharacterTestbed.*`), so the testbed can't go hollow even if the package count alone clears the main floor.
  Parameterized: `Run-Tests -MinSandboxTests <n>` (PS) / `run-tests.sh --min-sandbox-tests <n>` (bash), and
  `MIN_SANDBOX_TESTS` in `.github/workflows/ci.yml`.

> Hero-dependent tests never skip: the per-role hero-variant suite uses an **adaptive corpus** (committed synthetic
> `VH-*` fixtures always, plus the hero variants when their LFS objects are real GLBs) and the other hero cases fall
> back to a committed `SC-*` fixture (see `SandboxHeroVariantsTests` / `SandboxNSeriesTests`). So a checkout without the
> optional ~11 MB hero still runs real checks and keeps `skipped == 0`, and the floor (120) sits comfortably below the
> always-run count. The lone `[Category("Hero")]` tag remains only as an optional exclusion hint; it is not required to
> keep the gate green.

## Preflight lints (no Unity)

Three cheap, Unity-free jobs run before (and independently of) the gates above, so an onboarding/reproducibility defect
fails in seconds instead of after a slow editor spin-up:

- **Doc version lint** — `Check-DocsVersion.ps1` / `check-docs-version.sh`. Fails when any Unity-editor-version string
  in `README.md` / `docs/ci.md` / the workflows disagrees with `ProjectSettings/ProjectVersion.txt` (the single source
  of truth), so "clone and press Play" can never advertise the wrong editor.
- **Package pin preflight** — `Check-PackagePin.ps1` / `check-package-pin.sh`. The project pins UnityGLTF to a personal
  fork by commit SHA (a single point of failure for first-open **and** every Unity CI job). This resolves the pinned
  URL (`git ls-remote` + a shallow fetch of the exact SHA — the same resolution UPM performs) and fails fast with an
  actionable message if the fork is unreachable/renamed/private or the commit was force-pushed away. It also asserts
  the pin agrees across `Packages/manifest.json` (the source of truth), `Packages/packages-lock.json`, and `README.md`
  so the documented owner/SHA can't silently re-drift. The `tests`/nightly jobs `needs:` this preflight, so a doomed
  resolve never burns Unity CI minutes. Run `-SkipRemote` / `--skip-remote` to check only consistency when offline.
- **Attribution check** — `Check-Attribution.ps1` / `check-attribution.sh`. Fails when a committed
  `Assets/SampleAssets/**/*.glb` has no matching provenance row in `ATTRIBUTION.md`, so a new sample asset can't land
  without its license/source documented.

## Running locally

All scripts default `-ProjectPath` to the repo root and auto-locate Unity from
`ProjectSettings/ProjectVersion.txt` (override with `UNITY_PATH`). Every Unity launch is **detached + bounded-polled
+ hard-timeout-killed** — no unbounded waits.

### Windows (PowerShell)

```powershell
# Gate 1: prime Library/ (slow once; cached after)
./Tools/ci/Warm-Library.ps1

# Gates 1+2: compile, then run the PlayMode suite with the floor
./Tools/ci/Run-Tests.ps1 -Platform PlayMode -MinTests 12 -MinSandboxTests 12
./Tools/ci/Run-Tests.ps1 -Platform Both          # also run EditMode

# Gate 3: validate every exported GLB (no Unity needed)
./Tools/ci/Validate-Glb.ps1

# Gate 4: check the wire snapshots against the committed goldens
./Tools/ci/Export-Goldens.ps1 -Check
./Tools/ci/Export-Goldens.ps1 -Update            # intentionally regenerate goldens (review the diff)
```

### Linux / macOS (bash)

```bash
./Tools/ci/warm-library.sh
./Tools/ci/run-tests.sh --platform PlayMode --min-tests 12 --min-sandbox-tests 12
./Tools/ci/validate-glb.sh
./Tools/ci/export-goldens.sh --check
./Tools/ci/export-goldens.sh --update
```

Artifacts land under `Logs/ci/` (logs, per-run `*.pid`, NUnit XML) and `Artifacts/` (`glb/`, `snapshots/`,
`reports/`). One Unity per project: the scripts refuse to launch if `Temp/UnityLockfile` is present.

## The editor seam — `SandboxCI`

`Assets/Editor/SandboxCI.cs` is the `-executeMethod` bridge between the scripts and Unity. It **enables the export
plugins in code** (never trusting the committed settings asset, which can silently drift):

- `Samples.Editor.SandboxCI.EnablePlugins` — enable the KHR Character import/export + AnimationPointer plugins.
- `Samples.Editor.SandboxCI.ExportAllFixtures` — regenerate the committed `SC-*.glb` under `Assets/SampleAssets/Synthetic`.
- `Samples.Editor.SandboxCI.ExportGoldens` — export fixtures to `Artifacts/glb/` and write normalized wire
  snapshots to `Artifacts/snapshots/`.

## Installing the glTF-Validator (gate 3)

`Validate-Glb` looks for `gltf_validator` on `PATH` (or `$env:GLTF_VALIDATOR`). Locally, if it is absent the gate
**skips non-fatally** with this message. On CI it runs with `--required` (PS: `-Required`), so a missing validator
(or an unparseable report) is a **hard error**, not a silent skip. Install one of:

```bash
npm i -g gltf-validator           # provides the gltf_validator CLI
# or download a prebuilt binary from https://github.com/KhronosGroup/glTF-Validator/releases
#    and set GLTF_VALIDATOR=/path/to/gltf_validator
```

Pin a specific validator version in CI so local and CI match. The validator reports our `KHR_character_*`
extensions as `UNSUPPORTED_EXTENSION` *info* (they are non-ratified) — that is expected; their structural
correctness is covered by the **golden snapshot** (gate 4), not the validator.

## Updating goldens (gate 4)

Goldens live in `Tests/Golden/*.json` (one per fixture). When you intentionally change the wire:

> **Format note (one-time):** goldens are now **decoded-accessor** snapshots — each FLOAT accessor stores its rounded
> (1e-5) `values` instead of raw `byteOffset`/`min`/`max`, and byte-packing fields (`bufferView`/`buffer` offsets and
> lengths) are dropped. The first run after this change shows a large diff; regenerate once with `-Update` and commit
> it as the new baseline.

```powershell
./Tools/ci/Export-Goldens.ps1 -Update    # or: ./Tools/ci/export-goldens.sh --update
```

Commit the changed goldens — **the golden diff is the change record** reviewers see. A first-time run (no committed
goldens) skips the diff with a message to run `-Update` once.

## GitHub Actions (`.github/workflows/ci.yml`)

- **Provider:** GitHub Actions + [game-ci](https://game.ci) `unity-test-runner` (pinned editor image + license).
- **Unity:** `2022.3.76f1` (exact patch, matches `ProjectSettings/ProjectVersion.txt`).
- **Render pipeline:** **Built-in = required gate**; **URP = nightly / non-blocking** (see `.github/workflows/ci-nightly.yml`;
  the URP cell is WIRED - it activates a committed URP pipeline asset and runs the suite under URP via xvfb);
  HDRP deferred. The KHR test / glTF-validation / golden gates are RP-agnostic.
- **Library cache:** keyed on `Packages/manifest.json` + `Packages/packages-lock.json` + `ProjectVersion.txt`.
  Commit `packages-lock.json` so the resolved dependency commit SHA is recorded and a new commit busts the cache.
- **License (one-time setup):** add a repository secret **`UNITY_LICENSE`** with a Unity **Personal** license
  (`.ulf`). See <https://game.ci/docs/github/activation>.

> The golden job uses game-ci's builder as a licensed headless host to run the `SandboxCI.ExportGoldens`
> `-executeMethod` seam, then diffs the snapshots. Exact wiring may need tuning for your game-ci version; the gate
> logic (the diff) is provider-agnostic.

## Nightly + demo smoke tests (optional polish)

A separate non-blocking workflow (`.github/workflows/ci-nightly.yml`) runs on a daily cron (and manual dispatch). It
never gates a PR — the per-push `ci.yml` is the required gate. The nightly:

- **Built-in cell:** re-runs the full PlayMode suite to catch environment / dependency drift.
- **URP cell:** wired + non-blocking (`continue-on-error`). It activates the committed URP pipeline asset
  (`Assets/Settings/URP`) via `SandboxCI.ActivateUrp`, then runs the full suite under URP on game-ci's xvfb display
  (NOT `-nographics`). Materials follow the active pipeline (`Samples.Shared.RenderPipelineUtil`: Standard on Built-in,
  URP Lit under URP), so the Built-in goldens stay byte-stable while the demo-scene smoke tests exercise URP
  shader-clean boot. NOTE: the local `Tools/ci` scripts use `-nographics`, under which URP crashes during its material
  reimport - so validate the URP cell on CI (xvfb), not locally; the Built-in suite + goldens are fully local-validated.

**Demo-scene smoke tests** (`Assets/Tests/SandboxSceneSmokeTests.cs`) additively boot each built demo scene with the
hero forced off (`CharacterLoader.ForceSyntheticForTests`, so boot is fast + deterministic) and assert a clean boot:
the scene loads with root objects + a Camera, and no static renderer falls back to the magenta error shader. They run
in the normal suite (Gate 2), so a broken scene/material reds the required gate too.

**Schema conformance** is gated by the PlayMode suite (`SandboxSchemaConformanceTests`, Gate 2); a standalone
`Validate-Schema` CI script would duplicate it, so it is intentionally not added.
