using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using NUnit.Framework;
using UnityEngine;
using Samples.Shared;

namespace KhrCharacterTestbed.Tests
{
    /// <summary>
    /// Shared helpers for the testbed PlayMode suites so the lenses stop copy-pasting the same scaffolding
    /// (bounded async wait, scene resolution + cleanup registration, synthetic-fixture load) and share ONE
    /// definition of the Khronos-neutral extension allow-list. Lives in the test assembly so every suite can use it.
    /// </summary>
    internal static class SandboxTestUtil
    {
        // ── Bounded async + scene resolution ─────────────────────────────────

        /// <summary>Yield until the task completes or the timeout elapses (a hang surfaces as a failed assert in
        /// <see cref="ResolveScene"/>, never an unbounded wait).</summary>
        public static IEnumerator WaitFor(Task task, float timeoutSeconds)
        {
            float deadline = Time.realtimeSinceStartup + timeoutSeconds;
            while (!task.IsCompleted && Time.realtimeSinceStartup < deadline)
                yield return null;
        }

        /// <summary>Assert the import completed without error, register the scene for teardown, and return it.</summary>
        public static GameObject ResolveScene(Task<GameObject> task, List<Object> created)
        {
            Assert.IsTrue(task.IsCompleted, "glTF import did not complete within the timeout.");
            if (task.Exception != null) throw task.Exception;
            var scene = task.Result;
            Assert.IsNotNull(scene, "Imported scene root is null.");
            created.Add(scene);
            return scene;
        }

        /// <summary>Load a committed synthetic fixture and yield until ready; exposes the scene root via .Current.</summary>
        public static SceneLoad LoadSynthetic(string fileName, List<Object> created) => new SceneLoad(fileName, created);

        public sealed class SceneLoad : CustomYieldInstruction
        {
            private readonly Task<GameObject> _task;
            private readonly float _deadline;
            private readonly List<Object> _created;
            public GameObject Current { get; private set; }

            public SceneLoad(string fileName, List<Object> created)
            {
                _created = created;
                string path = CharacterLoader.SyntheticPath(fileName);
                Assert.IsTrue(File.Exists(path), $"{fileName} not found at '{path}'. Run Generate Sample Characters first.");
                _task = CharacterLoader.LoadAsync(path, null);
                _deadline = Time.realtimeSinceStartup + 30f;
            }

            public override bool keepWaiting
            {
                get
                {
                    if (!_task.IsCompleted && Time.realtimeSinceStartup < _deadline) return true;
                    Assert.IsTrue(_task.IsCompleted, "glTF import did not complete within 30s.");
                    if (_task.Exception != null) throw _task.Exception;
                    Current = _task.Result;
                    Assert.IsNotNull(Current, "Imported scene root is null.");
                    _created.Add(Current);
                    return false;
                }
            }
        }

        // ── Wire neutrality: the ^KHR_ allow-list ────────────────────────────

        // The neutrality contract is a POSITIVE allow-list, not a vendor blocklist: an extension is neutral iff it
        // is Khronos-namespaced (^KHR_) or one of a short set of explicitly-blessed core extensions. This replaces
        // the old "VRM" substring check, which silently passed every non-VRM vendor token (VRMC_, FB_, MSFT_,
        // ADOBE_, AGI_, GODOT_, CESIUM_, ...). All three core entries are themselves KHR_-prefixed today (so they
        // already pass the ^KHR_ rule); they are listed explicitly to document intent and keep the door open for a
        // future ratified non-KHR token without weakening the check.
        public static readonly string[] CoreNeutralAllowList =
        {
            "KHR_materials_unlit",
            "KHR_texture_transform",
            "KHR_animation_pointer",
        };

        /// <summary>True when an extension token is Khronos-neutral (^KHR_ or on the short core allow-list).</summary>
        public static bool IsNeutralExtension(string extension)
        {
            if (string.IsNullOrEmpty(extension)) return false;
            if (extension.StartsWith("KHR_", System.StringComparison.Ordinal)) return true;
            foreach (var allowed in CoreNeutralAllowList)
                if (extension == allowed) return true;
            return false;
        }

        /// <summary>True when an extension token is NOT Khronos-neutral (would taint a public-clean wire).</summary>
        public static bool IsVendorExtension(string extension) => !IsNeutralExtension(extension);

        /// <summary>Assert every token on an extension surface (extensionsUsed / extensionsRequired) is neutral.</summary>
        public static void AssertExtensionsNeutral(IEnumerable<string> extensions, string surfaceLabel)
        {
            if (extensions == null) return;
            foreach (var e in extensions)
                Assert.IsTrue(IsNeutralExtension(e),
                    $"{surfaceLabel} must be Khronos-neutral (^KHR_ allow-list); found non-neutral token '{e}'.");
        }

        // ── Misc ─────────────────────────────────────────────────────────────

        /// <summary>Depth-first search for a descendant transform by exact name (includes inactive).</summary>
        public static Transform FindDeep(Transform root, string name)
        {
            if (root == null) return null;
            foreach (var t in root.GetComponentsInChildren<Transform>(true))
                if (t != null && t.name == name) return t;
            return null;
        }
    }
}
