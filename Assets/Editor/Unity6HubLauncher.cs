// Installed only into the generated local testbed projects.
using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityGLTF;
using UnityGLTF.KhrCharacter;

namespace Samples.Editor
{
    public static class Unity6HubLauncher
    {
        private const string HubPath = "Assets/Samples/_Shared/Scenes/SampleHub.unity";
        private const string PendingKey = "KhronosHandoff.capturePending";
        private static double readyAt;
        private static double captureDeadline;
        private static string screenshotPath;
        private const string ErrorKey = "KhronosHandoff.errorCount";
#if UNITY_6000_0_OR_NEWER
        private static double playDeadline;
#endif

        [Serializable]
        private class Readiness
        {
            public string utc;
            public string scene;
            public bool playing;
            public int cameras;
            public int enabledScenes;
            public string[] demos;
            public bool pluginsEnabled;
            public int hubControllers;
            public int buttons;
            public int runtimeErrors;
            public string screenshot;
            public bool screenshotWritten;
            public bool ready;
        }

        [InitializeOnLoadMethod]
        private static void Install()
        {
            EditorApplication.playModeStateChanged -= OnPlayMode;
            EditorApplication.playModeStateChanged += OnPlayMode;
            Application.logMessageReceived -= OnLog;
            Application.logMessageReceived += OnLog;
            if (SessionState.GetBool(PendingKey, false) && EditorApplication.isPlaying)
                ScheduleCapture();
        }

        [MenuItem("Samples/Unity 6/Open SampleHub")]
        public static void OpenSampleHub()
        {
            SessionState.SetInt(ErrorKey, 0);
            if (!File.Exists(HubPath))
                throw new InvalidOperationException("SampleHub scene is missing: " + HubPath);
            EditorSceneManager.OpenScene(HubPath, OpenSceneMode.Single);
            // This project carries its own Assets/Resources settings. Do not write package settings.
            var settings = GLTFSettings.GetOrCreateSettings();
            if (settings == null || !AssetDatabase.GetAssetPath(settings).StartsWith("Assets/", StringComparison.Ordinal))
                throw new InvalidOperationException("Expected project-owned UnityGLTF settings before enabling plugins.");
            Samples.Editor.SandboxCI.EnablePlugins();
            WriteReadiness(false);
            EditorApplication.ExecuteMenuItem("Window/General/Game");
        }

        [MenuItem("Samples/Unity 6/Open SampleHub and Play")]
        public static void OpenSampleHubAndPlay()
        {
            OpenSampleHub();
#if UNITY_6000_0_OR_NEWER
            // Unity Search must create its default index before the first Play transition.
            playDeadline = EditorApplication.timeSinceStartup + 60.0;
            EditorApplication.update -= StartPlayWhenIdle;
            EditorApplication.update += StartPlayWhenIdle;
#else
            StartPlay();
#endif
        }

#if UNITY_6000_0_OR_NEWER
        private static void StartPlayWhenIdle()
        {
            if (EditorApplication.isCompiling || EditorApplication.isUpdating)
            {
                if (EditorApplication.timeSinceStartup < playDeadline)
                    return;
                EditorApplication.update -= StartPlayWhenIdle;
                throw new TimeoutException("Editor did not become idle before the Play bootstrap deadline.");
            }
            EditorApplication.update -= StartPlayWhenIdle;
            var module = AppDomain.CurrentDomain.GetAssemblies()
                .FirstOrDefault(a => a.GetName().Name == "UnityEditor.QuickSearchModule");
            var database = module?.GetType("UnityEditor.Search.SearchDatabase");
            var method = database?.GetMethod("GetDefaultSearchDatabase",
                System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic |
                System.Reflection.BindingFlags.Static, null, Type.EmptyTypes, null);
            if (method == null || method.Invoke(null, null) == null)
                throw new InvalidOperationException("Unity Search default index initialization failed; Play was not started.");
            Debug.Log("[KhronosHandoff] Unity Search initialized before Play.");
            StartPlay();
        }
#endif

        private static void StartPlay()
        {
            SessionState.SetBool(PendingKey, true);
            EditorApplication.isPaused = false;
            EditorApplication.isPlaying = true;
        }

        private static void OnLog(string message, string stack, LogType type)
        {
            if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert)
                SessionState.SetInt(ErrorKey, SessionState.GetInt(ErrorKey, 0) + 1);
        }

        private static void OnPlayMode(PlayModeStateChange state)
        {
            if (state == PlayModeStateChange.EnteredPlayMode && SessionState.GetBool(PendingKey, false))
                ScheduleCapture();
        }

        private static void ScheduleCapture()
        {
            readyAt = EditorApplication.timeSinceStartup + 2.0;
            screenshotPath = null;
            EditorApplication.update -= CaptureWhenReady;
            EditorApplication.update += CaptureWhenReady;
        }

        private static void CaptureWhenReady()
        {
            if (!EditorApplication.isPlaying || SceneManager.GetActiveScene().name != "SampleHub")
                return;
            if (EditorApplication.timeSinceStartup < readyAt)
                return;
            if (screenshotPath == null)
            {
                screenshotPath = Path.Combine(OutputDirectory(), "samplehub.png");
                ScreenCapture.CaptureScreenshot(screenshotPath);
                captureDeadline = EditorApplication.timeSinceStartup + 10.0;
                WriteReadiness(true);
                return;
            }
            if (!File.Exists(screenshotPath) && EditorApplication.timeSinceStartup < captureDeadline)
                return;
            WriteReadiness(true);
            SessionState.SetBool(PendingKey, false);
            EditorApplication.update -= CaptureWhenReady;
        }

        private static string OutputDirectory()
        {
            string directory = Path.Combine(Directory.GetParent(Application.dataPath).FullName, "Logs", "unity6");
            Directory.CreateDirectory(directory);
            return directory;
        }

        private static void WriteReadiness(bool runtime)
        {
            var scene = SceneManager.GetActiveScene();
            var settings = GLTFSettings.GetOrCreateSettings();
            bool import = settings != null && settings.ImportPlugins != null &&
                settings.ImportPlugins.Any(p => p is KhrCharacterImportPlugin && p.Enabled);
            bool export = settings != null && settings.ExportPlugins != null &&
                settings.ExportPlugins.Any(p => p is KhrCharacterExportPlugin && p.Enabled);
            var evidence = new Readiness {
                utc = DateTime.UtcNow.ToString("o"),
                scene = scene.name,
                playing = EditorApplication.isPlaying,
                cameras = Resources.FindObjectsOfTypeAll<Camera>().Count(c => c.gameObject.scene == scene &&
                    c.enabled && c.gameObject.activeInHierarchy),
                enabledScenes = EditorBuildSettings.scenes.Count(s => s.enabled),
                demos = Samples.Shared.DemoCatalog.All.Where(d => d.RegisterInHub).Select(d => d.SceneName).ToArray(),
                pluginsEnabled = import && export,
                hubControllers = Resources.FindObjectsOfTypeAll<Samples.Shared.SampleHubController>()
                    .Count(c => c.gameObject.scene == scene && c.gameObject.activeInHierarchy),
                // Avoid a new Unity.UGUI assembly dependency in the existing Editor asmdef.
                buttons = Resources.FindObjectsOfTypeAll<Component>().Count(c => c != null &&
                    c.gameObject.scene == scene && c.gameObject.activeInHierarchy &&
                    c.GetType().FullName == "UnityEngine.UI.Button"),
                runtimeErrors = SessionState.GetInt(ErrorKey, 0),
                screenshot = screenshotPath ?? "",
                screenshotWritten = screenshotPath != null && File.Exists(screenshotPath)
            };
            evidence.ready = evidence.scene == "SampleHub" && evidence.cameras > 0 &&
                evidence.enabledScenes >= 12 && evidence.demos.Length == 11 && evidence.pluginsEnabled &&
                evidence.hubControllers > 0 && evidence.runtimeErrors == 0 &&
                (!runtime || (evidence.playing && evidence.buttons >= evidence.demos.Length));
            File.WriteAllText(Path.Combine(OutputDirectory(), "readiness.json"), JsonUtility.ToJson(evidence, true));
            Debug.Log("[KhronosHandoff] " + (runtime ? "runtime" : "editor") +
                " readiness=" + evidence.ready + " scene=" + evidence.scene +
                " cameras=" + evidence.cameras + " demos=" + evidence.demos.Length +
                " buttons=" + evidence.buttons + " errors=" + evidence.runtimeErrors +
                " plugins=" + evidence.pluginsEnabled + " screenshot=" + evidence.screenshotWritten);
        }
    }
}
