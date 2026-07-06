using System.Collections.Generic;

namespace Samples.Shared
{
    /// <summary>
    /// Neutral registry of the sample scenes the hub launcher lists. Scene names are loaded by name through the
    /// SceneManager, so each entry's <see cref="DemoEntry.SceneName"/> must match a scene added to Build Settings.
    /// </summary>
    public static class DemoHubRegistry
    {
        public readonly struct DemoEntry
        {
            public readonly string SceneName;
            public readonly string Title;
            public readonly string Description;

            public DemoEntry(string sceneName, string title, string description)
            {
                SceneName = sceneName;
                Title = title;
                Description = description;
            }
        }

        /// <summary>The ordered set of demo scenes shown on the hub.</summary>
        public static readonly IReadOnlyList<DemoEntry> Demos = new List<DemoEntry>
        {
            new DemoEntry("CharacterShowcase", "Character Showcase", "Drive a full character through every KHR_character capability."),
            new DemoEntry("GlbViewer", "GLB Viewer", "Load and inspect any glTF/GLB at runtime."),
            new DemoEntry("Expressions", "Expressions", "Drive morph, joint, and texture expressions live."),
            new DemoEntry("GazeAndCamera", "Gaze & Camera", "Gaze targeting, camera hints, and view mode."),
            new DemoEntry("RigAndPose", "Rig & Pose", "Switch rig mode and apply the reference pose."),
            new DemoEntry("RoundTrip", "Round Trip", "Export, re-import, and compare the result."),
            new DemoEntry("Health", "Health", "Inspect capability health and graceful degradation."),
            new DemoEntry("VisibilityHints", "Visibility Hints", "First/third-person view context via KHR visibility hints (node + primitive)."),
        };
    }
}
