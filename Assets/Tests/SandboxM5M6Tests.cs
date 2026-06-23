using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityGLTF.KhrCharacter;
using Samples.Shared;

namespace KhrCharacterTestbed.Tests
{
    /// <summary>
    /// Phase-3 functional proofs (bounded PlayMode): M5 (export SC-FacePlus to an in-memory GLB → re-import → the
    /// re-imported character matches A and the exported wire is Khronos-neutral) and M6 (an imported SC-Body does
    /// not auto-play / snap to the T-pose on load). Both reference real plugin types, so they also act as
    /// anti-hollow gates.
    /// </summary>
    public class SandboxM5M6Tests
    {
        private readonly List<Object> _created = new List<Object>();

        [TearDown]
        public void TearDown()
        {
            foreach (var o in _created)
                if (o != null) Object.DestroyImmediate(o);
            _created.Clear();
        }

        [UnityTest]
        public IEnumerator M5_RoundTrip_ExportReimportPreservesCharacter()
        {
            string path = CharacterLoader.SyntheticPath("SC-FacePlus.glb");
            Assert.IsTrue(File.Exists(path),
                $"SC-FacePlus.glb not found at '{path}'. Run Generate Sample Characters first.");

            var taskA = CharacterLoader.LoadAsync(path, null);
            yield return WaitFor(taskA, 30f);
            var sceneA = ResolveScene(taskA);

            var a = sceneA.GetComponent<KhrCharacter>();
            Assert.IsNotNull(a, "Character A should import as a KhrCharacter.");
            var ecA = a.Expressions;
            Assert.IsNotNull(ecA, "Character A should have an ExpressionController.");
            int countA = ecA.Count;

            // Export A to an in-memory GLB and inspect the exported root for neutrality.
            byte[] glb = CharacterLoader.ExportToGlb(a.gameObject, out var exportedRoot);
            Assert.IsNotNull(glb);
            Assert.Greater(glb.Length, 0, "exported GLB must be non-empty.");
            Assert.IsNotNull(exportedRoot, "exporter should expose its GLTFRoot.");

            bool requiredEmpty = exportedRoot.ExtensionsRequired == null || exportedRoot.ExtensionsRequired.Count == 0;
            Assert.IsTrue(requiredEmpty,
                "exported extensionsRequired must be empty (Khronos-neutral wire). Found: " +
                (exportedRoot.ExtensionsRequired == null ? "null" : string.Join(", ", exportedRoot.ExtensionsRequired)));

            // Re-import the bytes as B and verify it round-trips to a character with matching expressions.
            var taskB = CharacterLoader.LoadFromBytesAsync(glb, null);
            yield return WaitFor(taskB, 30f);
            var sceneB = ResolveScene(taskB);

            var b = sceneB.GetComponent<KhrCharacter>();
            Assert.IsNotNull(b, "Re-imported character B must be a KhrCharacter.");
            var ecB = b.Expressions;
            Assert.IsNotNull(ecB, "Character B should have an ExpressionController.");
            Assert.GreaterOrEqual(ecB.Count, 1, "Character B should have at least one expression.");
            Assert.AreEqual(countA, ecB.Count, "Expression count should round-trip (within caveats).");
        }

        [UnityTest]
        public IEnumerator M6_NoAutoPlay_OnImport()
        {
            string path = CharacterLoader.SyntheticPath("SC-Body.glb");
            Assert.IsTrue(File.Exists(path),
                $"SC-Body.glb not found at '{path}'. Run Generate Sample Characters first.");

            var task = CharacterLoader.LoadAsync(path, null);
            yield return WaitFor(task, 30f);
            var scene = ResolveScene(task);

            // Let the frame in which a legacy Animation would otherwise auto-play its default clip pass.
            yield return null;

            var animation = scene.GetComponent<Animation>();
            if (animation != null)
                Assert.IsFalse(animation.isPlaying,
                    "an imported character must not auto-play its clips on load (no T-pose snap / import suppression).");
            // A null Animation host means nothing can auto-play, which also satisfies M6.
            yield return null;
        }

        // ── helpers ──────────────────────────────────────────────────────────

        private GameObject ResolveScene(Task<GameObject> task)
        {
            Assert.IsTrue(task.IsCompleted, "glTF import did not complete within the timeout.");
            if (task.Exception != null) throw task.Exception;
            var scene = task.Result;
            Assert.IsNotNull(scene, "Imported scene root is null.");
            _created.Add(scene);
            return scene;
        }

        private static IEnumerator WaitFor(Task task, float timeoutSeconds)
        {
            float deadline = Time.realtimeSinceStartup + timeoutSeconds;
            while (!task.IsCompleted && Time.realtimeSinceStartup < deadline)
                yield return null;
        }
    }
}
