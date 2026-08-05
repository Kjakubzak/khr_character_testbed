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
    /// definition of this testbed's deliberately strict KHR-only export profile. Lives in the test assembly so every suite can use it.
    /// </summary>
    public static class SandboxTestUtil
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

        /// <summary>Load a committed synthetic fixture under the controller-owned test policy and yield until ready;
        /// exposes the scene root via .Current. Tests of passive or unsuppressed host behavior pass a policy explicitly.</summary>
        public static SceneLoad LoadSynthetic(
            string fileName,
            List<Object> created,
            CharacterExpressionHostPolicy expressionPolicy = CharacterExpressionHostPolicy.LegacyControllerWithSuppression)
            => new SceneLoad(fileName, created, expressionPolicy);

        /// <summary>Load a FromBlender fixture by file name (see <c>Assets/SampleAssets/FromBlender</c>)
        /// and yield until ready; exposes the scene root via .Current. Complements
        /// <see cref="LoadSynthetic"/> — same wait/timeout/teardown behaviour.</summary>
        public static SceneLoad LoadFromBlender(
            string fileName,
            List<Object> created,
            CharacterExpressionHostPolicy expressionPolicy = CharacterExpressionHostPolicy.LegacyControllerWithSuppression)
            => new SceneLoad(CharacterLoader.FromBlenderPath(fileName), fileName, created, expressionPolicy);

        /// <summary>Load a fixture by absolute path. Used by the "iterate every catalog fixture"
        /// tests via <see cref="AllCatalogFixturePaths"/>.</summary>
        public static SceneLoad LoadFromAbsolutePath(
            string absolutePath,
            List<Object> created,
            CharacterExpressionHostPolicy expressionPolicy = CharacterExpressionHostPolicy.LegacyControllerWithSuppression)
            => new SceneLoad(
                absolutePath,
                System.IO.Path.GetFileName(absolutePath),
                created,
                expressionPolicy);

        /// <summary>Enumerate absolute paths of every fixture across every registered
        /// <see cref="CharacterLoader.AssetSourceCatalog"/> source. Used with NUnit's
        /// <c>[ValueSource]</c> for tests that iterate the full universe of discovered fixtures.
        ///
        /// The result is a stable, sorted array so NUnit's per-run test discovery names each
        /// case deterministically.</summary>
        public static string[] AllCatalogFixturePaths()
        {
            var list = new List<string>();
            foreach (var pair in CharacterLoader.AssetSourceCatalog.EnumerateAll())
                list.Add(pair.Path);
            list.Sort(System.StringComparer.Ordinal);
            return list.ToArray();
        }

        public sealed class SceneLoad : CustomYieldInstruction
        {
            private readonly Task<GameObject> _task;
            private readonly float _deadline;
            private readonly List<Object> _created;
            /// <summary>The imported scene root once <see cref="keepWaiting"/> flips false. Uses
            /// <c>new</c> to shadow <see cref="CustomYieldInstruction.Current"/> — CustomYieldInstruction's
            /// Current is a yield-instruction protocol member (a nullable "value to yield"); ours is
            /// the loaded GameObject. Rename would break every caller.</summary>
            public new GameObject Current { get; private set; }

            public SceneLoad(
                string fileName,
                List<Object> created,
                CharacterExpressionHostPolicy expressionPolicy)
                : this(CharacterLoader.SyntheticPath(fileName), fileName, created, expressionPolicy) {}

            // Absolute-path constructor — used by LoadFromBlender + any future non-Synthetic loader.
            // Keeps LoadSynthetic's existence-guard message so failure output points at the missing file
            // regardless of which fixture set the caller was after.
            internal SceneLoad(
                string absolutePath,
                string displayName,
                List<Object> created,
                CharacterExpressionHostPolicy expressionPolicy)
            {
                _created = created;
                Assert.IsTrue(File.Exists(absolutePath),
                    $"{displayName} not found at '{absolutePath}'. " +
                    "Run the appropriate regenerator (Generate Sample Characters for SC-*, " +
                    "or tests/fixtures/regenerate.py in the khr_character_blender repo for FromBlender/*).");
                _task = CharacterLoader.LoadAsync(absolutePath, null, expressionPolicy);
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

        // ── Testbed export policy: the ^KHR_ allow-list ──────────────────────

        // This is an intentionally narrow project profile, not a glTF namespace rule. KHR_ and EXT_ are both
        // spec-maintainer-reserved prefixes (EXT_ is multi-vendor); this testbed chooses to emit only KHR_ tokens
        // so accidental vendor dependencies are obvious in its generated fixtures.
        public static bool IsAllowedByKhrOnlyProfile(string extension)
            => !string.IsNullOrEmpty(extension)
                && extension.StartsWith("KHR_", System.StringComparison.Ordinal);

        public static void AssertExtensionsMatchKhrOnlyProfile(
            IEnumerable<string> extensions,
            string surfaceLabel)
        {
            if (extensions == null) return;
            foreach (var e in extensions)
                Assert.IsTrue(IsAllowedByKhrOnlyProfile(e),
                    $"{surfaceLabel} must match this testbed's KHR-only export profile; found token '{e}'.");
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
