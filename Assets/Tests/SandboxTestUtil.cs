using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using NUnit.Framework;
using Newtonsoft.Json.Linq;
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
            CharacterExpressionHostPolicy expressionPolicy = CharacterExpressionHostPolicy.LegacyControllerWithAutoPlaySuppression)
            => new SceneLoad(fileName, created, expressionPolicy);

        /// <summary>Load a FromBlender fixture by file name (see <c>Assets/SampleAssets/FromBlender</c>)
        /// and yield until ready; exposes the scene root via .Current. Complements
        /// <see cref="LoadSynthetic"/> — same wait/timeout/teardown behaviour.</summary>
        public static SceneLoad LoadFromBlender(
            string fileName,
            List<Object> created,
            CharacterExpressionHostPolicy expressionPolicy = CharacterExpressionHostPolicy.LegacyControllerWithAutoPlaySuppression)
            => new SceneLoad(CharacterLoader.FromBlenderPath(fileName), fileName, created, expressionPolicy);

        /// <summary>Load a fixture by absolute path. Used by the "iterate every catalog fixture"
        /// tests via <see cref="AllCatalogFixturePaths"/>.</summary>
        public static SceneLoad LoadFromAbsolutePath(
            string absolutePath,
            List<Object> created,
            CharacterExpressionHostPolicy expressionPolicy = CharacterExpressionHostPolicy.LegacyControllerWithAutoPlaySuppression)
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

        /// <summary>
        /// Imports an in-memory GLB (see <see cref="PatchGlbJson"/>) and yields until ready; exposes the scene root via
        /// .Current. Same wait, timeout and teardown behavior as <see cref="SceneLoad"/>.
        /// </summary>
        public sealed class BytesLoad : CustomYieldInstruction
        {
            private readonly Task<GameObject> _task;
            private readonly float _deadline;
            private readonly List<Object> _created;

            public new GameObject Current { get; private set; }

            public BytesLoad(byte[] glb, List<Object> created, CharacterExpressionHostPolicy expressionPolicy)
            {
                _created = created;
                _task = CharacterLoader.LoadFromBytesAsync(glb, null, expressionPolicy);
                _deadline = Time.realtimeSinceStartup + 30f;
            }

            public override bool keepWaiting
            {
                get
                {
                    if (!_task.IsCompleted && Time.realtimeSinceStartup < _deadline) return true;
                    Assert.IsTrue(_task.IsCompleted, "in-memory glTF import did not complete within 30s.");
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

        // ── In-memory wire edits ─────────────────────────────────────────────

        /// <summary>
        /// Returns a copy of a binary glTF whose JSON chunk <paramref name="patch"/> has edited; every other chunk is
        /// copied unchanged. Lets a test vary one wire detail of a committed fixture without committing a new asset.
        /// </summary>
        public static byte[] PatchGlbJson(byte[] glb, System.Action<JObject> patch)
        {
            Assert.IsNotNull(glb);
            Assert.IsTrue(glb.Length >= 20 && System.BitConverter.ToUInt32(glb, 0) == 0x46546C67u, "not a binary glTF.");
            var chunks = new List<(uint Type, byte[] Data)>();
            int offset = 12;
            while (offset + 8 <= glb.Length)
            {
                int length = (int)System.BitConverter.ToUInt32(glb, offset);
                uint type = System.BitConverter.ToUInt32(glb, offset + 4);
                var data = new byte[length];
                System.Array.Copy(glb, offset + 8, data, 0, length);
                chunks.Add((type, data));
                offset += 8 + length;
            }
            int jsonChunk = chunks.FindIndex(c => c.Type == 0x4E4F534Au);
            Assert.GreaterOrEqual(jsonChunk, 0, "the GLB has no JSON chunk.");

            var json = JObject.Parse(System.Text.Encoding.UTF8.GetString(chunks[jsonChunk].Data));
            patch(json);
            var text = System.Text.Encoding.UTF8.GetBytes(json.ToString(Newtonsoft.Json.Formatting.None));
            var padded = new byte[(text.Length + 3) & ~3];
            System.Array.Copy(text, padded, text.Length);
            for (int i = text.Length; i < padded.Length; i++) padded[i] = (byte)' ';
            chunks[jsonChunk] = (chunks[jsonChunk].Type, padded);

            using (var stream = new MemoryStream())
            using (var writer = new BinaryWriter(stream))
            {
                int total = 12;
                foreach (var chunk in chunks) total += 8 + chunk.Data.Length;
                writer.Write(0x46546C67u);
                writer.Write(2u);
                writer.Write((uint)total);
                foreach (var chunk in chunks)
                {
                    writer.Write((uint)chunk.Data.Length);
                    writer.Write(chunk.Type);
                    writer.Write(chunk.Data);
                }
                writer.Flush();
                return stream.ToArray();
            }
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
