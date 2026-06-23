# CI & Validation

This repo is a **real Unity project** that consumes the UnityGLTF / KHR Character package and tests it. CI is built
from small, reusable, cross-platform scripts under `Tools/ci/` (PowerShell `*.ps1` for local/Windows, bash `*.sh`
for Linux CI). Contributors run the **same** scripts CI runs, so there is no drift.

## The four merge gates

| # | Gate | Script | Pass criteria |
|---|------|--------|---------------|
| 1 | **Compile clean** | `Run-Tests` (Phase A) / `Warm-Library` | Unity exits 0 **and** no `error CS` in the compile log. Runs *before* any test pass — a `-runTests` against uncompilable code hangs forever, so we fail fast instead. |
| 2 | **Tests green + floor** | `Run-Tests` | Every NUnit run has `failed == 0`, `inconclusive == 0`, `skipped == 0`, the results XML is present, **and** `total >= floor` (default **6**). The floor kills the "0 tests ran = false green" trap: if the dependency resolves *hollow* (no KHR plugin), the suite shrinks toward 0 and this turns red instead of green. |
| 3 | **glTF-Validator** | `Validate-Glb` | The official Khronos `gltf_validator` reports `numErrors == 0` on every exported GLB. Independent, spec-authoritative — catches a bad wire even if our own tests have a bug. |
| 4 | **Round-trip goldens** | `Export-Goldens` | The normalized wire snapshot of each fixture matches its committed golden. Catches *structural* drift (new/renamed key, reordered array, changed accessor min/max) that value round-trip tests don't. |

> **Why the floor matters most:** the most dangerous CI failure is a *silent* one — a project that compiles nothing
> and reports a green 0-test run. The floor makes that specific regression impossible to merge.

## Min-test-count floor

- Default **6** — the sandbox's own PlayMode suite (smoke + M1–M6). Parameterized: `Run-Tests -MinTests <n>` (PS) /
  `run-tests.sh --min-tests <n>` (bash), and `MIN_TESTS` in `.github/workflows/ci.yml`.
- **Raise it** to ~150 once the consumed package's KHR test suite (≈161–163 cases) is confirmed running via
  `testables`. A low effective count is the signal that the package dependency resolved without the KHR plugin.

## Running locally

All scripts default `-ProjectPath` to the repo root and auto-locate Unity from
`ProjectSettings/ProjectVersion.txt` (override with `UNITY_PATH`). Every Unity launch is **detached + bounded-polled
+ hard-timeout-killed** — no unbounded waits.

### Windows (PowerShell)

```powershell
# Gate 1: prime Library/ (slow once; cached after)
./Tools/ci/Warm-Library.ps1

# Gates 1+2: compile, then run the PlayMode suite with the floor
./Tools/ci/Run-Tests.ps1 -Platform PlayMode -MinTests 6
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
./Tools/ci/run-tests.sh --platform PlayMode --min-tests 6
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

`Validate-Glb` looks for `gltf_validator` on `PATH` (or `$env:GLTF_VALIDATOR`). If it is absent the gate **skips
non-fatally** with this message. Install one of:

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

```powershell
./Tools/ci/Export-Goldens.ps1 -Update    # or: ./Tools/ci/export-goldens.sh --update
```

Commit the changed goldens — **the golden diff is the change record** reviewers see. A first-time run (no committed
goldens) skips the diff with a message to run `-Update` once.

## GitHub Actions (`.github/workflows/ci.yml`)

- **Provider:** GitHub Actions + [game-ci](https://game.ci) `unity-test-runner` (pinned editor image + license).
- **Unity:** `6000.0.76f1` (exact patch).
- **Render pipeline:** **Built-in = required gate**; **URP = nightly / non-blocking** (TODO stub at the bottom of
  the workflow); HDRP deferred. The KHR test / glTF-validation / golden gates are RP-agnostic.
- **Library cache:** keyed on `Packages/manifest.json` + `Packages/packages-lock.json` + `ProjectVersion.txt`.
  Commit `packages-lock.json` so the resolved dependency commit SHA is recorded and a new commit busts the cache.
- **License (one-time setup):** add a repository secret **`UNITY_LICENSE`** with a Unity **Personal** license
  (`.ulf`). See <https://game.ci/docs/github/activation>.

> The golden job uses game-ci's builder as a licensed headless host to run the `SandboxCI.ExportGoldens`
> `-executeMethod` seam, then diffs the snapshots. Exact wiring may need tuning for your game-ci version; the gate
> logic (the diff) is provider-agnostic.
