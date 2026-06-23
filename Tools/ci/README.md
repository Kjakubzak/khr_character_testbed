# Tools/ci — reusable test / validation harness

Cross-platform, KHR-agnostic CI entrypoints. Each has a PowerShell (`*.ps1`, local/Windows) and bash (`*.sh`,
Linux CI) form with the same behavior and the same exit-code contract (`0` = pass). Parameterized by project path
(`-ProjectPath` / `--project`, defaults to the repo root) and Unity path (`$UNITY_PATH` / auto-located from
`ProjectSettings/ProjectVersion.txt`).

| Script | Gate | What it does |
|--------|------|--------------|
| `_Common.ps1` / `_common.sh` | — | Shared helpers: locate Unity, detached launch + bounded poll + hard-timeout kill, lockfile guard, compile-log check. |
| `Warm-Library` | 1 | Cold-prime `Library/` (resolve packages + import + compile) so later runs are fast. |
| `Run-Tests` | 1 + 2 | Compile-only first, then `-runTests` per platform; enforces `failed/inconclusive/skipped == 0` **and** the min-test-count floor. |
| `Validate-Glb` | 3 | Runs the Khronos `gltf_validator` over every exported GLB; gates on `numErrors == 0`; skips non-fatally if the validator is absent. |
| `Export-Goldens` | 4 | Re-export + normalize the fixture wire (via `SandboxCI.ExportGoldens`); `-Check` diffs against committed goldens, `-Update` rewrites them. |

Full documentation, local examples, validator install, golden updates, and the GitHub Actions wiring: see
[`docs/ci.md`](../../docs/ci.md).

**Design invariants** (encoded so each known failure is structurally impossible or loudly detected):
compile-then-test sequencing (a `-runTests` against bad code hangs forever); detached + bounded-polled launches
(no unbounded waits); one Unity per project (lockfile guard); plugins enabled **in code** (never trust the
committed settings asset); min-test-count floor (kills the "0 tests = false green" hollow-dependency trap).
