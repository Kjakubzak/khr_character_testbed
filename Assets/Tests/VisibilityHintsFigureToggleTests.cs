using System.Collections;
using NUnit.Framework;
using Samples.VisibilityHints;
using UnityEngine;
using UnityEngine.TestTools;
using UnityGLTF.VisibilityHints;

namespace KhrCharacterTestbed.Tests
{
    /// <summary>
    /// Confirms the optional material host route realizes both passive hint types inside an explicit render scope,
    /// exercising the demo's public <see cref="VisibilityHintsController.BuildSampleFigure"/>. Complements
    /// <see cref="SandboxVisibilityHintsTests"/>, which verifies the evaluator itself is non-mutating.
    /// <list type="bullet">
    /// <item>Node hints replace all material slots only within a view where their predicate is false.</item>
    /// <item>The primitive hint replaces only the targeted faceplate slot.</item>
    /// <item>Disposing each scope restores every authored material and never toggles Renderer.enabled.</item>
    /// </list>
    /// </summary>
    public class VisibilityHintsFigureToggleTests
    {
        private GameObject _figure;

        [UnityTearDown]
        public IEnumerator Cleanup()
        {
            if (_figure != null) Object.Destroy(_figure);
            _figure = null;
            yield return null;
        }

        [Test]
        public void SampleFigure_ScopesNodeAndPrimitiveHintsByViewContext()
        {
            _figure = VisibilityHintsController.BuildSampleFigure(null);
            Assert.IsNotNull(_figure, "BuildSampleFigure should return a figure root.");

            var view = _figure.GetComponent<ViewContextController>();
            Assert.IsNotNull(view, "the figure should carry a ViewContextController.");
            Assert.IsNotNull(_figure.GetComponent<NodeVisibilityHintSet>(), "the figure should carry a NodeVisibilityHintSet.");
            Assert.IsNotNull(_figure.GetComponent<PrimitiveVisibilityHintSet>(), "the figure should carry a PrimitiveVisibilityHintSet.");
            var adapter = _figure.GetComponent<ScopedMaterialVisibilityAdapter>();
            Assert.IsNotNull(adapter, "the sample figure should explicitly install the optional material adapter.");
            Assert.IsNotNull(adapter.NoDrawMaterial, "the material route requires a pipeline-compatible no-draw material.");

            var head = RendererFor("Head");    // node hint: third_person
            var arms = RendererFor("Arms");    // node hint: first_person
            var mask = RendererFor("Mask");    // primitive hint on sub-mesh 1: third_person
            var torso = RendererFor("Torso");  // no hint: always visible
            Assert.AreEqual(2, mask.sharedMaterials.Length, "Mask should have two sub-mesh material slots.");

            var renderers = new[] { head, arms, mask, torso };
            var headMaterial = head.sharedMaterial;
            var armsMaterial = arms.sharedMaterial;
            var maskMaterials = mask.sharedMaterials;
            var torsoMaterial = torso.sharedMaterial;
            bool Hidden(Renderer renderer, int slot = 0)
                => ReferenceEquals(renderer.sharedMaterials[slot], adapter.NoDrawMaterial);
            bool CoreVisible(Transform node)
            {
                var renderer = node.GetComponent<Renderer>();
                return renderer == null || renderer.enabled;
            }

            using (adapter.ApplyForView(
                renderers,
                "third_person",
                CoreVisible))
            {
                Assert.IsFalse(Hidden(head), "third_person Head should render in the third-person scope.");
                Assert.IsTrue(Hidden(arms), "first_person Arms should be suppressed in the third-person scope.");
                Assert.IsFalse(Hidden(mask, 1), "third_person Mask shell should render in the third-person scope.");
                Assert.IsFalse(Hidden(torso), "unhinted Torso should always render.");
                Assert.IsTrue(head.enabled && arms.enabled && mask.enabled && torso.enabled,
                    "the material route must not toggle Renderer.enabled.");
            }

            CollectionAssert.AreEqual(new[] { headMaterial }, head.sharedMaterials);
            CollectionAssert.AreEqual(new[] { armsMaterial }, arms.sharedMaterials);
            CollectionAssert.AreEqual(maskMaterials, mask.sharedMaterials);
            CollectionAssert.AreEqual(new[] { torsoMaterial }, torso.sharedMaterials);

            using (adapter.ApplyForView(
                renderers,
                "first_person",
                CoreVisible))
            {
                Assert.IsTrue(Hidden(head), "third_person Head should be suppressed in the first-person scope.");
                Assert.IsFalse(Hidden(arms), "first_person Arms should render in the first-person scope.");
                Assert.IsTrue(Hidden(mask, 1), "third_person Mask shell should be suppressed in the first-person scope.");
                Assert.IsFalse(Hidden(torso));
            }

            CollectionAssert.AreEqual(new[] { headMaterial }, head.sharedMaterials);
            CollectionAssert.AreEqual(new[] { armsMaterial }, arms.sharedMaterials);
            CollectionAssert.AreEqual(maskMaterials, mask.sharedMaterials);
            CollectionAssert.AreEqual(new[] { torsoMaterial }, torso.sharedMaterials);
        }

        private Renderer RendererFor(string name)
        {
            var t = FindDescendant(_figure.transform, name);
            Assert.IsNotNull(t, $"figure should contain a '{name}' part.");
            var r = t.GetComponent<Renderer>();
            Assert.IsNotNull(r, $"'{name}' should have a Renderer.");
            return r;
        }

        private static Transform FindDescendant(Transform root, string name)
        {
            foreach (var t in root.GetComponentsInChildren<Transform>(true))
                if (t.name == name) return t;
            return null;
        }
    }
}
