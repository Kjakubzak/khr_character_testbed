using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using UnityGLTF;
using UnityGLTF.KhrCharacter;

namespace Samples.Shared
{
    /// <summary>
    /// Hub / launcher screen. Builds a panel with a plugin-status banner (red when the KHR Character plugins are
    /// disabled) and one launch button per registered demo scene. Scenes load by name through the SceneManager, so
    /// they must be in Build Settings (run the Build Sample Scenes tool first).
    /// </summary>
    public class SampleHubController : MonoBehaviour
    {
        private void Start()
        {
            var ui = DemoUiBuilder.Create("KHR Character Samples");
            ui.AddLabel("Community example: the Khronos KHR_character / avatar glTF extensions via UnityGLTF.");

            bool pluginsOn = PluginsEnabled();
            var banner = ui.AddLabel(pluginsOn
                ? "KHR Character plugins: ENABLED."
                : "KHR Character plugins are DISABLED - run Assets > UnityGLTF > KHR Character > Enable Plugins.");
            banner.color = pluginsOn ? new Color(0.4f, 0.9f, 0.45f) : new Color(0.95f, 0.4f, 0.4f);

            foreach (var demo in DemoCatalog.HubDemos)
            {
                string sceneName = demo.SceneName;
                ui.AddButton(demo.Title, () => Launch(sceneName));
            }

            ui.AddLabel("Tracks glTF PR #2512 (non-ratified).");
        }

        private static bool PluginsEnabled()
        {
            var settings = GLTFSettings.GetOrCreateSettings();
            if (settings == null) return false;

            bool import = false;
            if (settings.ImportPlugins != null)
                foreach (var plugin in settings.ImportPlugins)
                    if (plugin is KhrCharacterImportPlugin && plugin.Enabled) import = true;

            bool export = false;
            if (settings.ExportPlugins != null)
                foreach (var plugin in settings.ExportPlugins)
                    if (plugin is KhrCharacterExportPlugin && plugin.Enabled) export = true;

            return import && export;
        }

        private static void Launch(string sceneName)
        {
            if (string.IsNullOrEmpty(sceneName)) return;
            if (Application.CanStreamedLevelBeLoaded(sceneName))
                SceneManager.LoadScene(sceneName);
            else
                Debug.LogWarning($"[Samples] Scene '{sceneName}' is not in Build Settings; run Build Sample Scenes first.");
        }
    }
}
