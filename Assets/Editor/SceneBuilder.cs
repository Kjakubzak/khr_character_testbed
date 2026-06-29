using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using Samples.Shared;
using Samples.GlbViewer;
using Samples.Characters;

namespace Samples.Editor
{
    /// <summary>
    /// Programmatically builds and saves the sample scenes (SampleHub, GlbViewer, Expressions, GazeAndCamera,
    /// RigAndPose, RoundTrip, Health, CharacterShowcase), each with a camera (+ orbit rig where relevant), a
    /// render-pipeline bootstrap, an EventSystem, and the scene's controller, then registers them in Build Settings.
    /// Batchmode-safe: no blocking dialogs when run headless, so it is callable from CI via -executeMethod.
    /// </summary>
    public static class SceneBuilder
    {
        private const string HubScenePath = "Assets/Samples/_Shared/Scenes/SampleHub.unity";
        private const string GlbViewerScenePath = "Assets/Samples/GlbViewer/Scenes/GlbViewer.unity";
        private const string ExpressionsScenePath = "Assets/Samples/KhrCharacter/Scenes/Expressions.unity";
        private const string GazeScenePath = "Assets/Samples/KhrCharacter/Scenes/GazeAndCamera.unity";
        private const string RigScenePath = "Assets/Samples/KhrCharacter/Scenes/RigAndPose.unity";
        private const string RoundTripScenePath = "Assets/Samples/KhrCharacter/Scenes/RoundTrip.unity";
        private const string HealthScenePath = "Assets/Samples/KhrCharacter/Scenes/Health.unity";
        private const string ShowcaseScenePath = "Assets/Samples/KhrCharacter/Scenes/CharacterShowcase.unity";

        [MenuItem("Assets/UnityGLTF/KHR Character/Build Sample Scenes")]
        public static void BuildAllScenes()
        {
            // Protect unsaved work in interactive use; never block in batchmode.
            if (!Application.isBatchMode && !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
                return;

            BuildScene<SampleHubController>(HubScenePath, "SampleHub", addOrbit: false);
            BuildScene<GlbViewerController>(GlbViewerScenePath, "GlbViewer", addOrbit: true);
            BuildScene<ExpressionsDemoController>(ExpressionsScenePath, "Expressions", addOrbit: true);
            BuildScene<GazeAndCameraController>(GazeScenePath, "GazeAndCamera", addOrbit: true);
            BuildScene<RigAndPoseController>(RigScenePath, "RigAndPose", addOrbit: true);
            BuildScene<RoundTripController>(RoundTripScenePath, "RoundTrip", addOrbit: true);
            BuildScene<HealthController>(HealthScenePath, "Health", addOrbit: true);
            BuildScene<CharacterShowcaseController>(ShowcaseScenePath, "CharacterShowcase", addOrbit: true);

            RegisterBuildScenes(HubScenePath, GlbViewerScenePath, ExpressionsScenePath, GazeScenePath, RigScenePath,
                RoundTripScenePath, HealthScenePath, ShowcaseScenePath);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            Debug.Log("[Samples] Built sample scenes: SampleHub, GlbViewer, Expressions, GazeAndCamera, RigAndPose, RoundTrip, Health, CharacterShowcase (added to Build Settings).");
            if (!Application.isBatchMode)
                EditorUtility.DisplayDialog("Build Sample Scenes",
                    "Built all sample scenes (incl. CharacterShowcase) and added them to Build Settings.", "OK");
        }

        private static void BuildScene<TController>(string scenePath, string controllerName, bool addOrbit)
            where TController : Component
        {
            EnsureFolder(scenePath);

            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            var camGo = new GameObject("Main Camera", typeof(Camera));
            camGo.tag = "MainCamera";
            camGo.transform.position = new Vector3(0f, 0.9f, -2.5f);
            if (addOrbit) camGo.AddComponent<OrbitCameraRig>();

            new GameObject("RenderPipelineBootstrap", typeof(RenderPipelineBootstrap));
            new GameObject("EventSystem", typeof(EventSystem), typeof(StandaloneInputModule));
            new GameObject(controllerName, typeof(TController));

            // Shared camera-control / QoL panel. Self-builds at runtime and no-ops on scenes with no OrbitCameraRig.
            new GameObject("CameraControlPanel", typeof(CameraControlPanel));

            // Shared 3D visualizer + inspection panel + keyboard shortcuts. Each self-builds at runtime and no-ops
            // gracefully when its target is absent (e.g. the hub scene), mirroring CameraControlPanel.
            new GameObject("AssetVisualizer", typeof(AssetVisualizer));
            new GameObject("AssetInspectionPanel", typeof(AssetInspectionPanel));
            new GameObject("DemoShortcuts", typeof(DemoShortcuts));

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene, scenePath);
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
                bool exists = false;
                foreach (var existing in list)
                    if (existing.path == path) { exists = true; break; }
                if (!exists) list.Add(new EditorBuildSettingsScene(path, true));
            }
            EditorBuildSettings.scenes = list.ToArray();
        }
    }
}
