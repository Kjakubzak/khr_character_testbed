using System.Collections.Generic;
using System.Linq;

namespace Samples.Shared
{
    /// <summary>
    /// One-record-per-demo descriptor. Consolidates data that used to live in two hardcoded lists
    /// (<c>SceneBuilder</c>'s scene-path + controller-type list in the editor assembly, and
    /// <c>DemoHubRegistry.Demos</c>'s title + description list at runtime). Adding a new demo means
    /// appending ONE entry to <see cref="DemoCatalog.All"/>; the editor scene builder AND the runtime
    /// hub launcher AND the per-demo controller fallback (see <see cref="FallbackFile"/>) all read
    /// from that single list.
    ///
    /// The <see cref="ControllerTypeName"/> is a string ("Full.Type.Name, AssemblyName") rather than
    /// a <see cref="System.Type"/> so this file can live in <c>Samples.Shared</c> without forcing
    /// <c>Samples.Shared.asmdef</c> to reference every downstream demo asmdef (which would create
    /// circular dependencies — every demo asmdef already references <c>Samples.Shared</c>).
    /// SceneBuilder resolves the name via <c>Type.GetType</c> at build-scenes time and hard-errors
    /// on an unresolvable name — the check moves from compile-time to editor-invoke-time, which is
    /// acceptable for an editor-only tool.
    /// </summary>
    public readonly struct DemoDescriptor
    {
        /// <summary>Short scene name used by <see cref="UnityEngine.SceneManagement.SceneManager.LoadScene(string)"/>
        /// at runtime. Must match the filename (without <c>.unity</c>) of <see cref="ScenePath"/>.</summary>
        public readonly string SceneName;
        /// <summary>Hub-button label (e.g. "Character Showcase").</summary>
        public readonly string Title;
        /// <summary>Hub-button hint / one-liner blurb.</summary>
        public readonly string Description;
        /// <summary>Committed synthetic fixture the demo falls back to when the hero character is
        /// absent (typically "SC-Face.glb" or "SC-Body.glb"). Consumed by
        /// <see cref="CharacterLoader.LoadDemoCharacterAsync"/> via <see cref="FallbackFor"/>.
        /// Empty string when the demo doesn't load a character (e.g. GlbViewer takes user input,
        /// VisibilityHints uses its own synthetic set).</summary>
        public readonly string FallbackFile;
        /// <summary>Full project-relative scene path (e.g. "Assets/Samples/KhrCharacter/Scenes/X.unity").
        /// Editor-facing — used by SceneBuilder to save the scene to disk.</summary>
        public readonly string ScenePath;
        /// <summary>Assembly-qualified controller type name (e.g.
        /// "Samples.Characters.ExpressionsDemoController, Samples.KhrCharacter"). Editor-facing —
        /// SceneBuilder resolves this to attach the controller to the built scene.</summary>
        public readonly string ControllerTypeName;
        /// <summary>Attach an <c>OrbitCameraRig</c> to the built scene's Main Camera. True for
        /// every demo except the hub itself.</summary>
        public readonly bool AddOrbit;
        /// <summary>True when the scene appears as a launch button in <c>SampleHub</c>. The hub scene
        /// itself is <c>false</c> (you don't launch the hub from the hub).</summary>
        public readonly bool RegisterInHub;

        public DemoDescriptor(
            string sceneName, string title, string description, string fallbackFile,
            string scenePath, string controllerTypeName, bool addOrbit, bool registerInHub)
        {
            SceneName = sceneName;
            Title = title;
            Description = description;
            FallbackFile = fallbackFile;
            ScenePath = scenePath;
            ControllerTypeName = controllerTypeName;
            AddOrbit = addOrbit;
            RegisterInHub = registerInHub;
        }
    }

    /// <summary>Single source of truth for the sample scenes shipped by this project. Both
    /// <c>SceneBuilder</c> (editor-side scene generation) and <c>SampleHubController</c> (runtime hub
    /// launcher) enumerate this catalog. See <see cref="DemoDescriptor"/> for the field-level
    /// contract.</summary>
    public static class DemoCatalog
    {
        // Assembly names — kept as constants so a rename shows up as one edit here rather than
        // scattered through descriptor entries below.
        private const string SharedAsm = "Samples.Shared";
        private const string GlbViewerAsm = "Samples.GlbViewer";
        private const string CharactersAsm = "Samples.KhrCharacter";
        private const string VisibilityHintsAsm = "Samples.VisibilityHints";

        // The ordered set. Order defines both the scene-build order AND the hub button order (for
        // entries where RegisterInHub is true).
        public static readonly IReadOnlyList<DemoDescriptor> All = new[]
        {
            new DemoDescriptor(
                sceneName: "SampleHub",
                title: "Sample Hub",
                description: "The launcher scene.",
                fallbackFile: "",
                scenePath: "Assets/Samples/_Shared/Scenes/SampleHub.unity",
                controllerTypeName: "Samples.Shared.SampleHubController, " + SharedAsm,
                addOrbit: false,
                registerInHub: false),
            new DemoDescriptor(
                sceneName: "CharacterShowcase",
                title: "Character Showcase",
                description: "Drive a full character through every KHR_character capability.",
                fallbackFile: "SC-FacePlus.glb",
                scenePath: "Assets/Samples/KhrCharacter/Scenes/CharacterShowcase.unity",
                controllerTypeName: "Samples.Characters.CharacterShowcaseController, " + CharactersAsm,
                addOrbit: true,
                registerInHub: true),
            new DemoDescriptor(
                sceneName: "GlbViewer",
                title: "GLB Viewer",
                description: "Load and inspect any glTF/GLB at runtime.",
                fallbackFile: "",  // takes user input; no auto-loaded fallback
                scenePath: "Assets/Samples/GlbViewer/Scenes/GlbViewer.unity",
                controllerTypeName: "Samples.GlbViewer.GlbViewerController, " + GlbViewerAsm,
                addOrbit: true,
                registerInHub: true),
            new DemoDescriptor(
                sceneName: "Expressions",
                title: "Expressions",
                description: "Drive morph, joint, and texture expressions live.",
                fallbackFile: "SC-FacePlus.glb",
                scenePath: "Assets/Samples/KhrCharacter/Scenes/Expressions.unity",
                controllerTypeName: "Samples.Characters.ExpressionsDemoController, " + CharactersAsm,
                addOrbit: true,
                registerInHub: true),
            new DemoDescriptor(
                sceneName: "GazeAndCamera",
                title: "Gaze & Camera",
                description: "Gaze targeting, camera hints, and view mode.",
                fallbackFile: "SC-Face.glb",  // primary face fallback; SC-Body loaded separately for camera hints
                scenePath: "Assets/Samples/KhrCharacter/Scenes/GazeAndCamera.unity",
                controllerTypeName: "Samples.Characters.GazeAndCameraController, " + CharactersAsm,
                addOrbit: true,
                registerInHub: true),
            new DemoDescriptor(
                sceneName: "RigAndPose",
                title: "Rig & Pose",
                description: "Switch rig mode and apply the reference pose.",
                fallbackFile: "SC-Body.glb",
                scenePath: "Assets/Samples/KhrCharacter/Scenes/RigAndPose.unity",
                controllerTypeName: "Samples.Characters.RigAndPoseController, " + CharactersAsm,
                addOrbit: true,
                registerInHub: true),
            new DemoDescriptor(
                sceneName: "RoundTrip",
                title: "Round Trip",
                description: "Export, re-import, and compare the result.",
                fallbackFile: "SC-FacePlus.glb",
                scenePath: "Assets/Samples/KhrCharacter/Scenes/RoundTrip.unity",
                controllerTypeName: "Samples.Characters.RoundTripController, " + CharactersAsm,
                addOrbit: true,
                registerInHub: true),
            new DemoDescriptor(
                sceneName: "Health",
                title: "Health",
                description: "Inspect capability health and graceful degradation.",
                fallbackFile: "SC-Face.glb",
                scenePath: "Assets/Samples/KhrCharacter/Scenes/Health.unity",
                controllerTypeName: "Samples.Characters.HealthController, " + CharactersAsm,
                addOrbit: true,
                registerInHub: true),
            new DemoDescriptor(
                sceneName: "VisibilityHints",
                title: "Visibility Hints",
                description: "First/third-person view context via KHR visibility hints (node + primitive).",
                fallbackFile: "",  // uses its own VH-* synthetic set
                scenePath: "Assets/Samples/VisibilityHints/Scenes/VisibilityHints.unity",
                controllerTypeName: "Samples.VisibilityHints.VisibilityHintsController, " + VisibilityHintsAsm,
                addOrbit: true,
                registerInHub: true),
        };

        /// <summary>Just the entries with <see cref="DemoDescriptor.RegisterInHub"/> set — the hub
        /// launcher iterates this subset.</summary>
        public static IEnumerable<DemoDescriptor> HubDemos => All.Where(d => d.RegisterInHub);

        /// <summary>Look up a descriptor by its <see cref="DemoDescriptor.SceneName"/>. Returns
        /// <c>null</c> when no entry matches (e.g. an ad-hoc scene not in the catalog).</summary>
        public static DemoDescriptor? Find(string sceneName)
        {
            if (string.IsNullOrEmpty(sceneName)) return null;
            foreach (var d in All)
                if (d.SceneName == sceneName) return d;
            return null;
        }

        /// <summary>Fallback synthetic fixture name for a demo scene, or the given default when the
        /// catalog has no entry or the entry has no fallback specified. Consumed by
        /// <see cref="CharacterLoader.LoadDemoCharacterAsync"/> so each demo controller no longer
        /// hardcodes its own fallback file.</summary>
        public static string FallbackFor(string sceneName, string defaultFile = "SC-Face.glb")
        {
            var d = Find(sceneName);
            if (d.HasValue && !string.IsNullOrEmpty(d.Value.FallbackFile))
                return d.Value.FallbackFile;
            return defaultFile;
        }

        /// <summary>Display-friendly form of <see cref="FallbackFor"/> — strips the ``.glb`` extension
        /// so it can flow into <see cref="CharacterLoader.DemoCharacterBlurb"/> (which formats the
        /// user-facing "loaded X" line).</summary>
        public static string FallbackDisplayFor(string sceneName, string defaultDisplay = "SC-Face")
        {
            var full = FallbackFor(sceneName, defaultDisplay + ".glb");
            int dot = full.LastIndexOf('.');
            return dot > 0 ? full.Substring(0, dot) : full;
        }
    }
}
