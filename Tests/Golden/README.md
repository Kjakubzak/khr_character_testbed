# Tests/Golden

Normalized **wire snapshots** of the exported fixtures (one `*.json` per fixture, e.g. `SC-Face.json`,
`SC-FacePlus.json`, `SC-Body.json`). Gate 4 (round-trip goldens) re-exports each fixture, normalizes its glTF JSON
(decodes every FLOAT accessor to its rounded-1e-5 `values`, drops byte-packing fields and volatile
`asset.generator`/`copyright`, sorts every object's keys, pretty-prints), and diffs the result against the committed
golden here. Any un-acknowledged difference fails the gate — the diff is the change record.

## Generating / updating

These files are produced by Unity, so they are **not committed by the initial scaffolding**. Generate them once and
commit:

```powershell
./Tools/ci/Export-Goldens.ps1 -Update      # Windows
./Tools/ci/export-goldens.sh --update      # Linux/macOS
```

Re-run `-Update` whenever you intentionally change the wire, and review the resulting diff in code review. When this
folder has no `*.json`, `Export-Goldens -Check` skips the diff with a message to run `-Update` first.
