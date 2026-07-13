using System.Collections.Generic;

namespace Samples.Shared
{
    /// <summary>
    /// Thin catalog-backed shim preserving the pre-refactor <c>DemoHubRegistry.Demos</c> API for any
    /// consumer that still references it. New code should read <see cref="DemoCatalog.HubDemos"/>
    /// directly — this shim exists so downstream users of this project (or forks) don't get a
    /// compile break from the rename.
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

        /// <summary>Backed by <see cref="DemoCatalog.HubDemos"/>. Populated lazily so descriptor
        /// changes at authoring time survive without recompiling this file.</summary>
        public static IReadOnlyList<DemoEntry> Demos
        {
            get
            {
                var list = new List<DemoEntry>();
                foreach (var d in DemoCatalog.HubDemos)
                    list.Add(new DemoEntry(d.SceneName, d.Title, d.Description));
                return list;
            }
        }
    }
}
