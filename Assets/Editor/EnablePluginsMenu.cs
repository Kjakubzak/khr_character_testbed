using UnityEditor;
using UnityEngine;
using UnityGLTF;
using UnityGLTF.KhrCharacter;
using UnityGLTF.Plugins;

namespace Samples.Editor
{
    /// <summary>
    /// Tool A — enables the KHR Character import and export plugins (plus the export-side AnimationPointer plugin,
    /// which the character export depends on) in the project's shared GLTFSettings asset. Both KHR plugins are
    /// disabled by default; this is the manual safety net when the committed settings asset is not already enabled.
    /// </summary>
    public static class EnablePluginsMenu
    {
        private const string MenuPath = "Assets/UnityGLTF/KHR Character/Enable Plugins";

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
                    if (plugin is KhrCharacterImportPlugin)
                        updated += SetEnabled(plugin, true);

            if (settings.ExportPlugins != null)
                foreach (var plugin in settings.ExportPlugins)
                    if (plugin is KhrCharacterExportPlugin || plugin is AnimationPointerExport)
                        updated += SetEnabled(plugin, true);

            EditorUtility.SetDirty(settings);
            AssetDatabase.SaveAssets();
            Debug.Log($"[Samples] KHR Character plugins enabled ({updated} plugin(s) updated) in the project GLTFSettings.");
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
