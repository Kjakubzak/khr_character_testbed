using System;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace KhrCharacterSamples.Editor
{
    /// <summary>
    /// Batchmode still capture (graphics on; Camera.Render needs a GPU device, so not -nographics):
    /// <c>Unity -batchmode -projectPath P -executeMethod KhrCharacterSamples.Editor.CharacterCapture.Run</c>.
    /// Enters Play mode in an empty scene, runs <see cref="CaptureDriver"/>, then exits with 0 when every character
    /// captured without error. Env: KHR_EVIDENCE_DIR, KHR_CLIPS_DIR, KHR_CHARACTERS, KHR_CAPTURE_TIMEOUT (s).
    /// </summary>
    public static class CharacterCapture
    {
        private const string Job = "capture";

        public static void Run() => PlayModeJob.Start(Job);

        [MenuItem("Samples/Characters/Capture Stills")]
        private static void Menu() => PlayModeJob.Start(Job, exitWhenDone: false);

        [InitializeOnLoadMethod]
        private static void Register() => PlayModeJob.Register(Job,
            onPlay: () => CaptureDriver.Launch(),
            isDone: () => CaptureDriver.Done,
            failures: () => CaptureDriver.Failures);
    }

    /// <summary>
    /// Starts a job in Play mode and survives the domain reload that entering Play mode causes: the pending job lives in
    /// SessionState and each job re-registers its callbacks from [InitializeOnLoadMethod].
    /// </summary>
    internal static class PlayModeJob
    {
        private const string PendingKey = "KhrCharacterSamples.pendingJob";
        private const string ExitKey = "KhrCharacterSamples.exitWhenDone";
        private const string DeadlineKey = "KhrCharacterSamples.deadline";
        private static readonly System.Collections.Generic.Dictionary<string, (Action onPlay, Func<bool> isDone, Func<int> failures)> Jobs =
            new System.Collections.Generic.Dictionary<string, (Action, Func<bool>, Func<int>)>();
        private static double _idleDeadline;

        public static void Register(string job, Action onPlay, Func<bool> isDone, Func<int> failures)
        {
            Jobs[job] = (onPlay, isDone, failures);
            EditorApplication.playModeStateChanged -= OnPlayMode;
            EditorApplication.playModeStateChanged += OnPlayMode;
            if (SessionState.GetString(PendingKey, "") == job && EditorApplication.isPlaying) Begin(job);
        }

        public static void Start(string job, bool exitWhenDone = true)
        {
            SessionState.SetString(PendingKey, job);
            SessionState.SetBool(ExitKey, exitWhenDone);
            double timeout = double.TryParse(Environment.GetEnvironmentVariable("KHR_CAPTURE_TIMEOUT"), out var t) ? t : 3600;
            SessionState.SetFloat(DeadlineKey, (float)timeout);
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            _idleDeadline = EditorApplication.timeSinceStartup + 120.0;
            EditorApplication.update -= EnterPlayWhenIdle;
            EditorApplication.update += EnterPlayWhenIdle;
        }

        private static void EnterPlayWhenIdle()
        {
            if ((EditorApplication.isCompiling || EditorApplication.isUpdating) && EditorApplication.timeSinceStartup < _idleDeadline)
                return;
            EditorApplication.update -= EnterPlayWhenIdle;
            InitializeSearchIndex();
            EditorApplication.isPaused = false;
            EditorApplication.isPlaying = true;
        }

        // The v3.1 Unity 6 launcher found that Unity Search must build its default index before the first Play
        // transition in 6000.x; do the same, but only warn if the internal API is absent.
        private static void InitializeSearchIndex()
        {
            var module = AppDomain.CurrentDomain.GetAssemblies().FirstOrDefault(a => a.GetName().Name == "UnityEditor.QuickSearchModule");
            var method = module?.GetType("UnityEditor.Search.SearchDatabase")?.GetMethod("GetDefaultSearchDatabase",
                System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static,
                null, Type.EmptyTypes, null);
            if (method == null) Debug.LogWarning("[KhrCharacterSamples] Unity Search index API not found; entering Play anyway.");
            else method.Invoke(null, null);
        }

        private static void OnPlayMode(PlayModeStateChange state)
        {
            var job = SessionState.GetString(PendingKey, "");
            if (state == PlayModeStateChange.EnteredPlayMode && Jobs.ContainsKey(job)) Begin(job);
        }

        private static void Begin(string job)
        {
            Jobs[job].onPlay();
            double deadline = EditorApplication.timeSinceStartup + SessionState.GetFloat(DeadlineKey, 3600f);
            void Poll()
            {
                bool done = Jobs[job].isDone();
                bool timedOut = EditorApplication.timeSinceStartup > deadline;
                if (!done && !timedOut) return;
                EditorApplication.update -= Poll;
                SessionState.EraseString(PendingKey);
                int failures = Jobs[job].failures() + (timedOut ? 1 : 0);
                Debug.Log($"[KhrCharacterSamples] {job} finished: failures={failures} timedOut={timedOut}");
                if (SessionState.GetBool(ExitKey, true)) EditorApplication.Exit(failures == 0 ? 0 : 1);
                else EditorApplication.isPlaying = false;
            }
            EditorApplication.update += Poll;
        }
    }
}
