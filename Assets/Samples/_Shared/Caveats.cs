using System.Collections.Generic;

namespace Samples.Shared
{
    /// <summary>
    /// The canonical, single-source-of-truth caveat registry for the demos. Replaces the scattered inline caveat
    /// strings (the old, gap-riddled `C#`/`N#` magic literals) with one densely-numbered list (C1..Cn, no gaps) that
    /// every demo renders from via <see cref="Render"/>. Keeping the text here (not duplicated per scene) means a
    /// caveat is worded once, numbered once, and documented once (see <c>docs/caveats.md</c>, generated from this list).
    ///
    /// Each demo declares only WHICH caveats apply (by the descriptive <see cref="Caveat"/> key); the id + text come
    /// from here. So a demo can never over-promise silently, and the README's "every scene carries a caveat banner"
    /// claim is backed by an actual shared component rather than hand-copied strings.
    /// </summary>
    public enum Caveat
    {
        /// <summary>Universal: the whole extension set is a draft.</summary>
        Draft,
        /// <summary>Animation import bakes CUBICSPLINE tangents to sampled LINEAR keys.</summary>
        CubicSplineToLinear,
        /// <summary>Animated UV (texture-transform) is frame-exact only for the first cycle of a multi-key clip.</summary>
        UvFirstCycleExact,
        /// <summary>Renderers sharing a material collapse to one material on round-trip.</summary>
        SharedMaterialCollapse,
        /// <summary>Duplicate node / expression names are made unique on import.</summary>
        DuplicateNamesDeduped,
        /// <summary>Expression blend mode / priority (e.g. Override) is runtime-only, not on the wire.</summary>
        BlendModeRuntimeOnly,
        /// <summary>A camera hint's projection index does not round-trip through glTF.</summary>
        CameraProjectionOffWire,
        /// <summary>One KHR_character root per glTF document.</summary>
        OneCharacterPerDocument,
        /// <summary>A skeleton mapping missing required bones degrades gracefully to Generic.</summary>
        SkeletonGracefulDegrade,
        /// <summary>The VRoid hero is VRM 1.0 (non-commercial); demos read only its KHR_character data.</summary>
        HeroNonCommercial,
        /// <summary>Geometric eye-aim is a demo convenience, not part of KHR_character.</summary>
        EyeAimNonSpec,
    }

    public static class Caveats
    {
        // Declaration order == display order == dense C-numbering (C1, C2, ...). Add new caveats at the END so the
        // existing ids stay stable; never leave a gap. This ordering is the single source of truth for docs/caveats.md.
        private static readonly Caveat[] Order =
        {
            Caveat.Draft,
            Caveat.CubicSplineToLinear,
            Caveat.UvFirstCycleExact,
            Caveat.SharedMaterialCollapse,
            Caveat.DuplicateNamesDeduped,
            Caveat.BlendModeRuntimeOnly,
            Caveat.CameraProjectionOffWire,
            Caveat.OneCharacterPerDocument,
            Caveat.SkeletonGracefulDegrade,
            Caveat.HeroNonCommercial,
            Caveat.EyeAimNonSpec,
        };

        private static readonly Dictionary<Caveat, string> Bodies = new Dictionary<Caveat, string>
        {
            [Caveat.Draft] = "Tracks glTF PR #2512 (KHR_character / avatar) — a DRAFT extension set, not ratified; the wire may still change.",
            [Caveat.CubicSplineToLinear] = "Animation import bakes CUBICSPLINE tangents to sampled LINEAR keys (curve shape approximated).",
            [Caveat.UvFirstCycleExact] = "Animated texture-transform (UV) is frame-exact only for the first cycle of a multi-key clip.",
            [Caveat.SharedMaterialCollapse] = "Renderers that share a material collapse to one material on round-trip (per-renderer identity not preserved).",
            [Caveat.DuplicateNamesDeduped] = "Duplicate node / expression names are made unique on import.",
            [Caveat.BlendModeRuntimeOnly] = "Expression blend mode / priority (e.g. Override) is runtime-only — it is not written to the glTF wire.",
            [Caveat.CameraProjectionOffWire] = "A camera hint's projection index does not round-trip through glTF.",
            [Caveat.OneCharacterPerDocument] = "One KHR_character root per glTF document.",
            [Caveat.SkeletonGracefulDegrade] = "A skeleton mapping with missing / invalid required bones degrades gracefully to Generic (never throws).",
            [Caveat.HeroNonCommercial] = "The VRoid \"hero\" is VRM 1.0 (non-commercial); the demos read only its KHR_character data. The synthetic SC-* fallbacks are CC0.",
            [Caveat.EyeAimNonSpec] = "Geometric eye-aim is a demo convenience, not part of KHR_character.",
        };

        /// <summary>Stable display id for a caveat, e.g. <c>"C1"</c>. Dense (no gaps) by construction.</summary>
        public static string Id(Caveat c) => "C" + (System.Array.IndexOf(Order, c) + 1);

        /// <summary>The caveat's wording, without its id.</summary>
        public static string Body(Caveat c) => Bodies.TryGetValue(c, out var t) ? t : c.ToString();

        /// <summary>A single rendered line, e.g. <c>"[C1] Tracks glTF PR #2512 …"</c>.</summary>
        public static string Line(Caveat c) => $"[{Id(c)}] {Body(c)}";

        /// <summary>The full canonical list in id order — used by docs generation and the "all caveats" reference.</summary>
        public static IEnumerable<Caveat> All => Order;

        /// <summary>Render the given caveats as label rows on a demo panel (a "Caveats:" header + one line each). No-op
        /// when <paramref name="ui"/> is null or no caveats are passed, so callers can invoke it unconditionally.</summary>
        public static void Render(DemoUiBuilder ui, params Caveat[] caveats)
        {
            if (ui == null || caveats == null || caveats.Length == 0) return;
            ui.AddLabel("Caveats (demo limitations — see docs/caveats.md):");
            foreach (var c in caveats)
                ui.AddLabel(Line(c));
        }

        /// <summary>Convenience for callers that already have a single uGUI label to fill (e.g. one banner
        /// text element): returns the caveats joined one per line.</summary>
        public static string Join(params Caveat[] caveats)
        {
            if (caveats == null || caveats.Length == 0) return string.Empty;
            var sb = new System.Text.StringBuilder();
            for (int i = 0; i < caveats.Length; i++)
            {
                if (i > 0) sb.Append('\n');
                sb.Append(Line(caveats[i]));
            }
            return sb.ToString();
        }
    }
}
