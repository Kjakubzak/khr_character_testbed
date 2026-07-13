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

        /// <summary>Load a committed synthetic fixture and yield until ready; exposes the scene root via .Current.</summary>
        public static SceneLoad LoadSynthetic(string fileName, List<Object> created) => new SceneLoad(fileName, created);

        /// <summary>Load a FromBlender fixture by file name (see <c>Assets/SampleAssets/FromBlender</c>)
        /// and yield until ready; exposes the scene root via .Current. Complements
        /// <see cref="LoadSynthetic"/> — same wait/timeout/teardown behaviour.</summary>
        public static SceneLoad LoadFromBlender(string fileName, List<Object> created)
            => new SceneLoad(CharacterLoader.FromBlenderPath(fileName), fileName, created);

        /// <summary>Load a fixture by absolute path. Used by the "iterate every catalog fixture"
        /// tests via <see cref="AllCatalogFixturePaths"/>.</summary>
        public static SceneLoad LoadFromAbsolutePath(string absolutePath, List<Object> created)
            => new SceneLoad(absolutePath, System.IO.Path.GetFileName(absolutePath), created);

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

            public SceneLoad(string fileName, List<Object> created)
                : this(CharacterLoader.SyntheticPath(fileName), fileName, created) {}

            // Absolute-path constructor — used by LoadFromBlender + any future non-Synthetic loader.
            // Keeps LoadSynthetic's existence-guard message so failure output points at the missing file
            // regardless of which fixture set the caller was after.
            internal SceneLoad(string absolutePath, string displayName, List<Object> created)
            {
                _created = created;
                Assert.IsTrue(File.Exists(absolutePath),
                    $"{displayName} not found at '{absolutePath}'. " +
                    "Run the appropriate regenerator (Generate Sample Characters for SC-*, " +
                    "or tests/fixtures/regenerate.py in the khr_character_blender repo for FromBlender/*).");
                _task = CharacterLoader.LoadAsync(absolutePath, null);
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
        //
        // The allow-list is a mutable HashSet so a test suite (or a downstream fork with different policy) can add
        // entries at runtime via ``RegisterNeutralExtension``. The initial contents match the hardcoded defaults
        // that shipped in the pre-refactor version, so behaviour is unchanged unless a caller opts in.
        private static readonly HashSet<string> _neutralAllowList = new HashSet<string>(
            System.StringComparer.Ordinal)
        {
            "KHR_materials_unlit",
            "KHR_texture_transform",
            "KHR_animation_pointer",
        };

        /// <summary>Read-only view of the mutable core-neutral allow-list. Kept as a
        /// <see cref="string"/>[] for source-level backwards compat with pre-refactor consumers
        /// that iterated the array; new consumers should call <see cref="IsNeutralExtension"/> or
        /// <see cref="RegisterNeutralExtension"/> instead of touching this directly.</summary>
        public static string[] CoreNeutralAllowList
        {
            get { var arr = new string[_neutralAllowList.Count]; _neutralAllowList.CopyTo(arr); return arr; }
        }

        /// <summary>Add an extension token to the core-neutral allow-list. Idempotent. Meant for
        /// tests or downstream forks that want to permit additional non-KHR tokens without
        /// modifying source. Returns false when the token is null/empty or already present.</summary>
        public static bool RegisterNeutralExtension(string extension)
        {
            if (string.IsNullOrEmpty(extension)) return false;
            return _neutralAllowList.Add(extension);
        }

        /// <summary>True when an extension token is Khronos-neutral (^KHR_ or on the core allow-list).</summary>
        public static bool IsNeutralExtension(string extension)
        {
            if (string.IsNullOrEmpty(extension)) return false;
            if (extension.StartsWith("KHR_", System.StringComparison.Ordinal)) return true;
            return _neutralAllowList.Contains(extension);
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
