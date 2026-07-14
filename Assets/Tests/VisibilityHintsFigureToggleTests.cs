using System.Collections;
using NUnit.Framework;
using Samples.VisibilityHints;
using UnityEngine;
using UnityEngine.TestTools;
using UnityGLTF.VisibilityHints;

namespace KhrCharacterTestbed.Tests
{
    /// <summary>
    /// Confirms the view-context visibility hints drive BOTH hint types when <see cref="ViewContextController.Mode"/>
    /// is toggled, exercising the demo's public <see cref="VisibilityHintsController.BuildSampleFigure"/> (the same
    /// runtime wiring a runtime-imported VH asset uses). Complements <see cref="SandboxVisibilityHintsTests"/> (which
    /// only checks one node hint on the demo scene): here both node roles and the primitive hint are asserted.
    /// <list type="bullet">
    /// <item>Node hint <c>third_person</c> (Head): <c>renderer.enabled</c> follows ThirdPerson.</item>
    /// <item>Node hint <c>first_person</c> (Arms): <c>renderer.enabled</c> follows FirstPerson.</item>
    /// <item>Primitive hint <c>third_person</c> (Mask sub-mesh 1): the faceplate sub-mesh material swaps to/from
    /// the shared invisible material (visible in third person, hidden in first person).</item>
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

        [UnityTest]
        public IEnumerator SampleFigure_TogglesNodeAndPrimitiveHintsByViewContext()
        {
            _figure = VisibilityHintsController.BuildSampleFigure(null);
            Assert.IsNotNull(_figure, "BuildSampleFigure should return a figure root.");
            yield return null; // let the initial (ThirdPerson) state settle

            var view = _figure.GetComponent<ViewContextController>();
            Assert.IsNotNull(view, "the figure should carry a ViewContextController.");
            Assert.IsNotNull(_figure.GetComponent<NodeVisibilityHintSet>(), "the figure should carry a NodeVisibilityHintSet.");
            Assert.IsNotNull(_figure.GetComponent<PrimitiveVisibilityHintSet>(), "the figure should carry a PrimitiveVisibilityHintSet.");

            var head = RendererFor("Head");    // node hint: third_person
            var arms = RendererFor("Arms");    // node hint: first_person
            var mask = RendererFor("Mask");    // primitive hint on sub-mesh 1: third_person
            var torso = RendererFor("Torso");  // no hint: always visible
            Assert.AreEqual(2, mask.sharedMaterials.Length, "Mask should have two sub-mesh material slots.");

            var invisible = InvisibleMaterialCache.Get();
            bool MaskShellHidden() => ReferenceEquals(mask.sharedMaterials[1], invisible);

            // --- Default view: ThirdPerson ---
            Assert.AreEqual(ViewContextController.ViewContext.ThirdPerson, view.Mode, "figure should start in ThirdPerson.");
            Assert.IsTrue(head.enabled, "node hint: third_person Head should be VISIBLE in ThirdPerson.");
            Assert.IsFalse(arms.enabled, "node hint: first_person Arms should be HIDDEN in ThirdPerson.");
            Assert.IsFalse(MaskShellHidden(), "primitive hint: third_person Mask shell should be VISIBLE (authored material) in ThirdPerson.");
            Assert.IsTrue(torso.enabled, "unhinted Torso should always be visible.");

            // --- Toggle to FirstPerson ---
            view.Mode = ViewContextController.ViewContext.FirstPerson;
            yield return null;
            Assert.IsFalse(head.enabled, "node hint: Head should HIDE in FirstPerson.");
            Assert.IsTrue(arms.enabled, "node hint: Arms should SHOW in FirstPerson.");
            Assert.IsTrue(MaskShellHidden(), "primitive hint: Mask shell should HIDE (invisible material) in FirstPerson.");
            Assert.IsTrue(torso.enabled, "unhinted Torso should stay visible in FirstPerson.");

            // --- Toggle back to ThirdPerson ---
            view.Mode = ViewContextController.ViewContext.ThirdPerson;
            yield return null;
            Assert.IsTrue(head.enabled, "node hint: Head should RESTORE to visible in ThirdPerson.");
            Assert.IsFalse(arms.enabled, "node hint: Arms should HIDE again in ThirdPerson.");
            Assert.IsFalse(MaskShellHidden(), "primitive hint: Mask shell should be VISIBLE again in ThirdPerson.");
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
