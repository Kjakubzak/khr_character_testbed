using System.Collections;
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityGLTF.KhrCharacter;
using Samples.Shared;

namespace KhrCharacterTestbed.Tests
{
    /// <summary>
    /// Phase-3 functional proofs (bounded PlayMode): M5 verifies that imported passive expression data cannot be
    /// silently re-exported through the lossy legacy controller; M6 verifies explicit autoplay suppression for an
    /// expression asset. Both reference real plugin types as anti-hollow gates.
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
        public IEnumerator M5_ImportedExpressionExportRefusesLossyProjection()
        {
            string path = CharacterLoader.SyntheticPath("SC-FacePlus.glb");
            Assert.IsTrue(File.Exists(path),
                $"SC-FacePlus.glb not found at '{path}'. Run Generate Sample Characters first.");

            var taskA = CharacterLoader.LoadAsync(
                path,
                null,
                CharacterExpressionHostPolicy.Passive);
            yield return SandboxTestUtil.WaitFor(taskA, 30f);
            var sceneA = SandboxTestUtil.ResolveScene(taskA, _created);

            var a = sceneA.GetComponent<KhrCharacter>();
            Assert.IsNotNull(a, "Character A should import as a KhrCharacter.");
            Assert.IsNotNull(a.ExpressionResponses, "Character A should retain the passive wire response set.");
            Assert.IsNull(a.Expressions, "the export safety proof must not depend on a lossy controller projection");

            var exception = Assert.Catch<System.InvalidOperationException>(
                () => CharacterLoader.ExportToGlb(a.gameObject, out _));
            StringAssert.Contains("passive ExpressionResponseSet", exception.Message);
        }

        [UnityTest]
        public IEnumerator M6_ControllerOwnedPolicySuppressesExpressionAutoPlay()
        {
            string path = CharacterLoader.SyntheticPath("SC-Face.glb");
            Assert.IsTrue(File.Exists(path),
                $"SC-Face.glb not found at '{path}'. Run Generate Sample Characters first.");

            var task = CharacterLoader.LoadAsync(
                path,
                null,
                CharacterExpressionHostPolicy.LegacyControllerWithSuppression);
            yield return SandboxTestUtil.WaitFor(task, 30f);
            var scene = SandboxTestUtil.ResolveScene(task, _created);

            var hub = scene.GetComponent<KhrCharacter>();
            Assert.IsNotNull(hub?.Expressions,
                "M6 must exercise the optional controller-owned host policy, not a vacuous asset without responses.");

            // Let the frame in which a legacy Animation would otherwise auto-play its default clip pass.
            yield return null;

            var animation = scene.GetComponentInChildren<Animation>(true);
            Assert.IsNotNull(animation, "SC-Face must provide expression clips for the suppression proof.");
            Assert.IsFalse(animation.isPlaying,
                "the explicitly selected controller-owned policy must suppress expression clip autoplay.");
            yield return null;
        }
    }
}
