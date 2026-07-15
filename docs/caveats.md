# Demo caveats

This is the **canonical index** of the demo caveats — the honest "this demo does not fully round-trip / is
runtime-only / is non-spec" notes surfaced on-screen in the sample scenes.

The single source of truth is the code registry **`Assets/Samples/_Shared/Caveats.cs`** (`enum Caveat` +
`Caveats` helper). Demos render their applicable caveats with `Caveats.Render(ui, Caveat.X, Caveat.Y, …)`, so the
wording and the `C#` numbering live in exactly one place (no more scattered magic strings or numbering gaps). The
ids below are dense (`C1…C11`) and assigned by declaration order in that file; **add new caveats at the end** so
existing ids stay stable.

## The caveats

| Id | Key (`Caveat.…`) | Caveat |
|----|------------------|--------|
| C1 | `Draft` | Tracks glTF PR #2512 (KHR_character / avatar) — a DRAFT extension set, not ratified; the wire may still change. |
| C2 | `CubicSplineToLinear` | Animation import bakes CUBICSPLINE tangents to sampled LINEAR keys (curve shape approximated). |
| C3 | `UvFirstCycleExact` | Animated texture-transform (UV) is frame-exact only for the first cycle of a multi-key clip. |
| C4 | `SharedMaterialCollapse` | Renderers that share a material collapse to one material on round-trip (per-renderer identity not preserved). |
| C5 | `DuplicateNamesDeduped` | Duplicate node / expression names are made unique on import. |
| C6 | `BlendModeRuntimeOnly` | Expression blend mode / priority (e.g. Override) is runtime-only — it is not written to the glTF wire. |
| C7 | `CameraProjectionOffWire` | A camera hint's projection index does not round-trip through glTF. |
| C8 | `OneCharacterPerDocument` | One KHR_character root per glTF document. |
| C9 | `SkeletonGracefulDegrade` | A skeleton mapping with missing / invalid required bones degrades gracefully to Generic (never throws). |
| C10 | `HeroNonCommercial` | The VRoid "hero" is VRM 1.0 (non-commercial); the demos read only its KHR_character data. The synthetic SC-* fallbacks are CC0. |
| C11 | `EyeAimNonSpec` | Geometric eye-aim is a demo convenience, not part of KHR_character. |

## Which demo surfaces which caveat

Every demo surfaces **C1 (Draft)**; the capability demos add the caveats relevant to what they exercise.

| Demo scene | Caveats |
|------------|---------|
| SampleHub | C1 |
| CharacterShowcase | C1, C6, C7, C9, C10 |
| Expressions | C1, C6 |
| GazeAndCamera | C1, C7, C11 |
| RigAndPose | C1, C9 |
| RoundTrip | C1, C2, C3, C4, C5, C6, C7, C8 |
| Health | C1, C9, C10 |
| VisibilityHints | C1 |
| GlbViewer | C1 |
| HumanoidAnimation | C1, C2 |
| AnimationRigging | C1 |
| AnimationSandbox | C1, C2 |

*(The `N#` markers still shown on some demo control labels — e.g. `[N4]` first-person toggle — are demo **control**
ids, not caveats; they are intentionally separate from this fidelity/spec caveat index.)*
