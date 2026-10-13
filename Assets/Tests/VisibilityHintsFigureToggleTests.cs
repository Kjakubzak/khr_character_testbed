using System.Collections;
using System.Collections.Generic;
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
    /// <item>A view's tag set decides the standard roles: an untagged view shows only <c>always</c> and un-hinted
    /// content; custom or differently cased roles are hidden unless the host supports them.</item>
    /// <item>Disposing each scope restores every authored material and never toggles Renderer.enabled.</item>
    /// </list>
    /// </summary>
    public class VisibilityHintsFigureToggleTests
    {
        private static readonly string[] Untagged = new string[0];
        private static readonly string[] ThirdPerson = { "third_person" };
        private static readonly string[] FirstPerson = { "first_person" };
        private static readonly string[] BothTags = { "first_person", "third_person" };

        private GameObject _figure;
        private ScopedMaterialVisibilityAdapter _adapter;

        [UnityTearDown]
        public IEnumerator Cleanup()
        {
            if (_figure != null) Object.Destroy(_figure);
            _figure = null;
            yield return null;
        }

        [Test]
        public void SampleFigure_ScopesNodeAndPrimitiveHintsByViewTags()
        {
            BuildFigure();
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

            using (_adapter.ApplyForView(renderers, ThirdPerson, CoreVisible))
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

            using (_adapter.ApplyForView(renderers, FirstPerson, CoreVisible))
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

        [Test]
        public void SampleFigure_UntaggedViewShowsOnlyUnhintedAndAlwaysContent()
        {
            BuildFigure();
            var head = RendererFor("Head");
            var arms = RendererFor("Arms");
            var mask = RendererFor("Mask");
            var torso = RendererFor("Torso");

            using (_adapter.ApplyForView(new[] { head, arms, mask, torso }, Untagged, CoreVisible))
            {
                Assert.IsTrue(Hidden(head), "third_person Head needs a third_person view tag.");
                Assert.IsTrue(Hidden(arms), "first_person Arms need a first_person view tag.");
                Assert.IsTrue(Hidden(mask, 1), "the third_person Mask shell needs a third_person view tag.");
                Assert.IsFalse(Hidden(mask, 0), "the unhinted Mask strap renders in every view.");
                Assert.IsFalse(Hidden(torso), "the unhinted Torso renders in every view.");
            }
        }

        [Test]
        public void SampleFigure_ViewWithBothTagsShowsEverything()
        {
            BuildFigure();
            var head = RendererFor("Head");
            var arms = RendererFor("Arms");
            var mask = RendererFor("Mask");
            var torso = RendererFor("Torso");

            using (_adapter.ApplyForView(new[] { head, arms, mask, torso }, BothTags, CoreVisible))
            {
                Assert.IsFalse(Hidden(head));
                Assert.IsFalse(Hidden(arms));
                Assert.IsFalse(Hidden(mask, 0));
                Assert.IsFalse(Hidden(mask, 1));
                Assert.IsFalse(Hidden(torso));
            }
        }

        [Test]
        public void SampleFigure_CustomRolesAreHiddenInEveryStandardView()
        {
            BuildFigure();
            var mirrorOnly = AddHintedPart("MirrorDetail", "ACME_mirror_only");
            var cased = AddHintedPart("CasedDetail", "First_Person");
            var renderers = new[] { mirrorOnly, cased };

            foreach (var tags in new[] { Untagged, ThirdPerson, FirstPerson, BothTags })
                using (_adapter.ApplyForView(renderers, tags, CoreVisible))
                {
                    string viewName = $"{{{string.Join(", ", tags)}}}";
                    Assert.IsTrue(Hidden(mirrorOnly), $"the unsupported custom role ACME_mirror_only must be hidden in {viewName}.");
                    Assert.IsTrue(Hidden(cased), $"'First_Person' is a custom role, not first_person, and must be hidden in {viewName}.");
                }

            // The application may choose to render content whose role it does not support.
            _figure.GetComponent<ViewContextController>().RenderUnsupportedRoles = true;
            using (_adapter.ApplyForView(renderers, ThirdPerson, CoreVisible))
            {
                Assert.IsFalse(Hidden(mirrorOnly));
                Assert.IsFalse(Hidden(cased));
            }
        }

        private void BuildFigure()
        {
            _figure = VisibilityHintsController.BuildSampleFigure(null);
            Assert.IsNotNull(_figure, "BuildSampleFigure should return a figure root.");
            Assert.IsNotNull(_figure.GetComponent<ViewContextController>(), "the figure should carry a ViewContextController.");
            Assert.IsNotNull(_figure.GetComponent<NodeVisibilityHintSet>(), "the figure should carry a NodeVisibilityHintSet.");
            Assert.IsNotNull(_figure.GetComponent<PrimitiveVisibilityHintSet>(), "the figure should carry a PrimitiveVisibilityHintSet.");
            _adapter = _figure.GetComponent<ScopedMaterialVisibilityAdapter>();
            Assert.IsNotNull(_adapter, "the sample figure should explicitly install the optional material adapter.");
            Assert.IsNotNull(_adapter.NoDrawMaterial, "the material route requires a pipeline-compatible no-draw material.");
        }

        // Adds a renderer under the figure whose node carries the given role, keeping the authored hints.
        private Renderer AddHintedPart(string name, string role)
        {
            var part = GameObject.CreatePrimitive(PrimitiveType.Cube);
            part.name = name;
            part.transform.SetParent(_figure.transform, false);
            var renderer = part.GetComponent<Renderer>();
            renderer.sharedMaterial = RendererFor("Torso").sharedMaterial;

            var hints = _figure.GetComponent<NodeVisibilityHintSet>();
            var entries = new List<NodeVisibilityHintSet.NodeVisibilityEntry>(hints.Entries)
            {
                new NodeVisibilityHintSet.NodeVisibilityEntry { Node = part.transform, Role = role },
            };
            hints.Bind(entries);
            return renderer;
        }

        private bool Hidden(Renderer renderer, int slot = 0)
            => ReferenceEquals(renderer.sharedMaterials[slot], _adapter.NoDrawMaterial);

        private static bool CoreVisible(Transform node)
        {
            var renderer = node.GetComponent<Renderer>();
            return renderer == null || renderer.enabled;
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
