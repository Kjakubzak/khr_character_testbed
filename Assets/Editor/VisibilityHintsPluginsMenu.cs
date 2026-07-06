using UnityEditor;
using UnityEngine;
using UnityGLTF;
using UnityGLTF.Plugins;
using UnityGLTF.VisibilityHints;

namespace Samples.Editor
{
    /// <summary>
    /// Enables the VisibilityHints import and export plugins in the project's shared GLTFSettings asset. Both are
    /// disabled by default (non-ratified); this is the manual safety net so imported/exported glTF round-trips the
    /// view-context hints. The VisibilityHints demo scene itself authors hints at runtime and works without this,
    /// but enabling the plugins lets the GlbViewer / RoundTrip demos carry the hints through glTF too.
    /// </summary>
    public static class VisibilityHintsPluginsMenu
    {
        private const string MenuPath = "Assets/UnityGLTF/Visibility Hints/Enable Plugins";

        [MenuItem(MenuPath)]
        public static void EnablePlugins()
        {
            var settings = GLTFSettings.GetOrCreateSettings();
            if (settings == null)
            {
                Debug.LogError("[Samples] Could not load or create GLTFSettings; nothing was enabled.");
                return;
            }

            int updated = 0;

            if (settings.ImportPlugins != null)
                foreach (var plugin in settings.ImportPlugins)
                    if (plugin is VisibilityHintImportPlugin)
                        updated += SetEnabled(plugin, true);

            if (settings.ExportPlugins != null)
                foreach (var plugin in settings.ExportPlugins)
                    if (plugin is VisibilityHintExportPlugin)
                        updated += SetEnabled(plugin, true);

            EditorUtility.SetDirty(settings);
            AssetDatabase.SaveAssets();
            Debug.Log($"[Samples] VisibilityHints plugins enabled ({updated} plugin(s) updated) in the project GLTFSettings.");
        }

        private static int SetEnabled(GLTFPlugin plugin, bool value)
        {
            if (plugin == null || plugin.Enabled == value) return 0;
            plugin.Enabled = value;
            EditorUtility.SetDirty(plugin);
            return 1;
        }
    }
}
