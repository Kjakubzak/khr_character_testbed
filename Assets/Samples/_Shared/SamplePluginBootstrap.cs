using UnityEngine;

namespace Samples.Shared
{
    /// <summary>
    /// Enables the (disabled-by-default, non-ratified) KHR-Character + visibility-hint import plugins ONCE before any
    /// scene loads, so hint/character import is deterministic regardless of which demo scene ran first. Previously the
    /// visibility-hint plugin was enabled only as a side effect of the VisibilityHints scene's Start(), which made
    /// import order-dependent (Area 1 A2 / Area 4 A2 / Area 5 A18). <see cref="CharacterLoader.EnableImportPlugin"/>
    /// is the single source of truth for which import plugins this project turns on; this just calls it eagerly.
    /// </summary>
    public static class SamplePluginBootstrap
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void EnablePluginsBeforeSceneLoad()
        {
            CharacterLoader.EnableImportPlugin();
        }
    }
}
