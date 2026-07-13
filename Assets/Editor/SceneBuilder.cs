using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using Samples.Shared;

namespace Samples.Editor
{
    /// <summary>
    /// Programmatically builds and saves every sample scene declared in
    /// <see cref="DemoCatalog.All"/>, adds them to Build Settings, and hard-errors on any descriptor
    /// whose <see cref="DemoDescriptor.ControllerTypeName"/> can't be resolved (moves the drift
    /// check to editor-invoke time). Adding a new scene is now ONE entry in <see cref="DemoCatalog"/>
    /// — SceneBuilder needs no per-scene edit.
    /// Batchmode-safe: no blocking dialogs when run headless, so it is callable from CI via
    /// <c>-executeMethod</c>.
    /// </summary>
    public static class SceneBuilder
    {
        [MenuItem("Assets/UnityGLTF/KHR Character/Build Sample Scenes")]
        public static void BuildAllScenes()
        {
            // Protect unsaved work in interactive use; never block in batchmode.
            if (!Application.isBatchMode && !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
                return;

            var buildScenePaths = new List<string>();
            var failed = new List<string>();

            foreach (var descriptor in DemoCatalog.All)
            {
                var controllerType = System.Type.GetType(descriptor.ControllerTypeName);
                if (controllerType == null)
                {
                    failed.Add(
                        $"[Samples] SceneBuilder: descriptor '{descriptor.SceneName}' references " +
                        $"unresolvable controller type '{descriptor.ControllerTypeName}'. " +
                        "Check the assembly-qualified name in DemoCatalog.All.");
                    continue;
                }
                BuildScene(descriptor, controllerType);
                buildScenePaths.Add(descriptor.ScenePath);
            }

            RegisterBuildScenes(buildScenePaths.ToArray());
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            if (failed.Count > 0)
            {
                foreach (var msg in failed) Debug.LogError(msg);
                if (!Application.isBatchMode)
                    EditorUtility.DisplayDialog("Build Sample Scenes",
                        $"Built {buildScenePaths.Count} scene(s); {failed.Count} descriptor(s) failed. See Console.",
                        "OK");
                return;
            }

            Debug.Log($"[Samples] Built {buildScenePaths.Count} sample scene(s) from DemoCatalog " +
                      "and added them to Build Settings.");
            if (!Application.isBatchMode)
                EditorUtility.DisplayDialog("Build Sample Scenes",
                    $"Built {buildScenePaths.Count} sample scene(s) and added them to Build Settings.",
                    "OK");
        }

        /// <summary>Editor-invoke-time validation — call from CI to catch stale descriptors before
        /// they wedge scene building. Returns 0 on all-good, non-zero on failure (CI exit code).</summary>
        public static int ValidateCatalog()
        {
            int failures = 0;
            foreach (var d in DemoCatalog.All)
            {
                if (System.Type.GetType(d.ControllerTypeName) == null)
                {
                    Debug.LogError($"[Samples] ValidateCatalog: '{d.SceneName}' controller type " +
                                   $"'{d.ControllerTypeName}' does not resolve.");
                    failures++;
                }
                var parent = Path.GetDirectoryName(d.ScenePath);
                if (!string.IsNullOrEmpty(parent) && !Directory.Exists(Path.GetFullPath(parent)))
                {
                    Debug.LogWarning($"[Samples] ValidateCatalog: '{d.SceneName}' parent folder " +
                                     $"'{parent}' does not exist. SceneBuilder will create it.");
                }
                if (!string.IsNullOrEmpty(d.FallbackFile))
                {
                    var fallbackAbs = Path.Combine(CharacterLoader.SyntheticDirectory, d.FallbackFile);
                    if (!File.Exists(fallbackAbs))
                    {
                        Debug.LogWarning($"[Samples] ValidateCatalog: '{d.SceneName}' fallback " +
                                         $"'{d.FallbackFile}' not found at '{fallbackAbs}'. Run " +
                                         "Generate Sample Characters to produce it.");
                    }
                }
            }
            return failures;
        }

        private static void BuildScene(DemoDescriptor descriptor, System.Type controllerType)
        {
            EnsureFolder(descriptor.ScenePath);

            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            var camGo = new GameObject("Main Camera", typeof(Camera));
            camGo.tag = "MainCamera";
            camGo.transform.position = new Vector3(0f, 0.9f, -2.5f);
            if (descriptor.AddOrbit) camGo.AddComponent<OrbitCameraRig>();

            new GameObject("RenderPipelineBootstrap", typeof(RenderPipelineBootstrap));
            new GameObject("EventSystem", typeof(EventSystem), typeof(StandaloneInputModule));

            // The controller GO name matches SceneName (matches the pre-refactor convention where
            // "SampleHub" hosted the SampleHubController, etc.).
            new GameObject(descriptor.SceneName, controllerType);

            // Shared camera-control / QoL panel. Self-builds at runtime and no-ops on scenes with no OrbitCameraRig.
            new GameObject("CameraControlPanel", typeof(CameraControlPanel));

            // Shared 3D visualizer + inspection panel + keyboard shortcuts. Each self-builds at runtime and no-ops
            // gracefully when its target is absent (e.g. the hub scene), mirroring CameraControlPanel.
            new GameObject("AssetVisualizer", typeof(AssetVisualizer));
            new GameObject("AssetInspectionPanel", typeof(AssetInspectionPanel));
            new GameObject("DemoShortcuts", typeof(DemoShortcuts));

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene, descriptor.ScenePath);
        }

        private static void EnsureFolder(string assetPath)
        {
            var dir = Path.GetDirectoryName(assetPath);
            if (string.IsNullOrEmpty(dir)) return;
            var full = Path.GetFullPath(dir);
            if (!Directory.Exists(full)) Directory.CreateDirectory(full);
        }

        private static void RegisterBuildScenes(params string[] scenePaths)
        {
            var list = new List<EditorBuildSettingsScene>(EditorBuildSettings.scenes);
            foreach (var path in scenePaths)
            {
                bool alreadyRegistered = false;
                foreach (var existing in list)
                    if (existing.path == path) { existing.enabled = true; alreadyRegistered = true; break; }
                if (!alreadyRegistered)
                    list.Add(new EditorBuildSettingsScene(path, enabled: true));
            }
            EditorBuildSettings.scenes = list.ToArray();
        }
    }
}
