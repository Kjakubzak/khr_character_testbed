# Demo caveats

This is the **canonical index** of the demo caveats — the honest "this demo does not fully round-trip / is
runtime-only / is non-spec" notes surfaced on-screen in the sample scenes.

The single source of truth is the code registry **`Assets/Samples/_Shared/Caveats.cs`** (`enum Caveat` +
`Caveats` helper). Demos render their applicable caveats with `Caveats.Render(ui, Caveat.X, Caveat.Y, …)`, so the
wording and the `C#` numbering live in exactly one place (no more scattered magic strings or numbering gaps). The
ids below are dense (`C1…C10`) and assigned by declaration order in that file; **add new caveats at the end** so
existing ids stay stable.

## The caveats

| Id | Key (`Caveat.…`) | Caveat |
|----|------------------|--------|
| C1 | `Draft` | Tracks glTF PR #2512 (KHR_character / avatar) — a DRAFT extension set, not ratified; the wire may still change. |
| C2 | `CubicSplineToLinear` | Animation import bakes CUBICSPLINE tangents to sampled LINEAR keys (curve shape approximated). |
| C3 | `DuplicateExpressionLabels` | Duplicate expression names are valid, so a name does not identify an expression; the demos address expressions by their authoritative array index. |
| C4 | `HostDefinedWriterMix` | Expression composition is normative (y = b + sum(s - n) per additive component, no clamp); only how the result mixes with other animation writers is host-defined. |
| C5 | `CameraProjectionOffWire` | A camera hint's projection index does not round-trip through glTF. |
| C6 | `OneCharacterPerDocument` | KHR_character marks the asset as containing at most one character; it names no root or member nodes. |
| C7 | `SkeletonGracefulDegrade` | The optional Unity Humanoid adapter reports Degraded when its selected mapping lacks a HumanTrait-required role; the glTF mapping itself is not thereby invalid. |
| C8 | `HeroNonCommercial` | The VRoid "hero" is VRM 1.0 (non-commercial); the demos read only its KHR_character data. The synthetic SC-* fallbacks are CC0. |
| C9 | `EyeAimNonSpec` | Geometric eye-aim is a demo convenience, not part of KHR_character. |
| C10 | `HeroPendingReexport` | The hero is pending re-export: its expression entries have no name and its look expressions rotate the same eye nodes (a prohibited single-writer conflict), so its expression data does not import. Expression demos use the synthetic fallback until it is re-exported. |

## Which demo surfaces which caveat

Every demo surfaces **C1 (Draft)**; the capability demos add the caveats relevant to what they exercise. The demos
that load or offer the hero also show **C10** while `CharacterLoader.HeroNeedsReexport` is true, so it disappears
by itself once the hero is re-exported.

| Demo scene | Caveats |
|------------|---------|
| SampleHub | C1 |
| CharacterShowcase | C1, C4, C5, C7, C8 (+C10) |
| Expressions | C1, C4 (+C10) |
| GazeAndCamera | C1, C5, C9 (+C10) |
| RigAndPose | C1, C7 (+C10) |
| RoundTrip | C1, C5, C6 |
| Health | C1, C7, C8 (+C10) |
| VisibilityHints | C1 (+C10) |
| GlbViewer | C1 (+C10) |
| HumanoidAnimation | C1, C2 (+C10) |
| AnimationRigging | C1 (+C10) |
| AnimationSandbox | C1, C2 (+C10) |

*(The `N#` markers still shown on some demo control labels — e.g. `[N4]` first-person toggle — are demo **control**
ids, not caveats; they are intentionally separate from this fidelity/spec caveat index.)*
