using System;
using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using Samples.Shared;
using UnityEngine;
using UnityEngine.TestTools;
using UnityGLTF;
using UnityGLTF.KhrCharacter;
using UnityGLTF.VisibilityHints;

namespace KhrCharacterTestbed.Tests
{
    public class SandboxNormativeSyncTests
    {
        private readonly List<UnityEngine.Object> _created = new List<UnityEngine.Object>();

        [TearDown]
        public void Cleanup()
        {
            foreach (var value in _created)
                if (value != null) UnityEngine.Object.DestroyImmediate(value);
            _created.Clear();
        }

        [UnityTest]
        public IEnumerator SharedTargets_KeepAdditiveContributorsAndAbsoluteOtherResponses()
        {
            var load = Load("SC-SharedTargets.glb");
            yield return load;
            var set = load.Current.GetComponent<KhrCharacter>()?.ExpressionResponses;
            Assert.IsNotNull(set);
            Assert.AreEqual(2, set.Count);
            Assert.IsTrue(set.Validation.IsComplete);
            Assert.AreEqual(4, set.Evaluate(0, 1).Records.Count);
            Assert.AreEqual(4, set.Evaluate(1, 1).Records.Count,
                "shared rotations/scales must not reject either whole mixed track");
            var target = SandboxTestUtil.FindDeep(load.Current.transform, "SharedTarget");
            var position = target.localPosition;
            var scale = target.localScale;
            var rotation = target.localRotation;
            var composition = set.Compose(set.BuildSnapshot(new double?[] { 1, 1 }));
            AssertValue(composition, "/nodes/1/translation", new[] { 1.7, 0.0, 0.0 });
            AssertValue(composition, "/nodes/1/weights", new[] { 0.8 });
            AssertValue(composition, "/nodes/1/scale", new[] { 3.0, 3.0, 3.0 });
            Assert.IsTrue(composition.TryGetRecord("/nodes/1/rotation", out var q));
            double norm = 0;
            foreach (double value in q.Value) norm += value * value;
            Assert.AreEqual(1, norm, 1e-6);
            Assert.AreEqual(1, Math.Abs(q.Value[2]), 1e-6,
                "this Unity host chooses the highest active entry's ordinary 180-degree sample");
            Assert.AreEqual(position, target.localPosition);
            Assert.AreEqual(scale, target.localScale);
            Assert.AreEqual(rotation, target.localRotation);
        }

        [UnityTest]
        public IEnumerator SingleContributor_UsesItsOwnNeutralAndSamplesDriverOnlyOnce()
        {
            var load = Load("SC-SharedTargets.glb");
            yield return load;
            var set = load.Current.GetComponent<KhrCharacter>().ExpressionResponses;
            var composition = set.Compose(set.BuildSnapshot(new double?[] { 1, 0 }));
            AssertValue(composition, "/nodes/1/translation", new[] { 1.3, 0.0, 0.0 });
            AssertValue(composition, "/nodes/1/weights", new[] { 0.5 });
            Assert.IsTrue(set.Compose(set.BuildSnapshot(new double?[] { 0.5, 0 }))
                .TryGetRecord("/nodes/1/rotation", out var q));
            Assert.AreEqual(Math.Sin(Math.PI / 8), Math.Abs(q.Value[2]), 1e-6,
                "half driver samples 45 degrees; the host must not accidentally scale this record again");
            Assert.IsEmpty(set.Evaluate(0, 0).Records);
            Assert.AreEqual(4, set.Evaluate(0, 1).Records.Count,
                "a previous zero driver does not affect subsequent stateless sampling");
        }

        [UnityTest]
        public IEnumerator CanonicalMapping_SumsExternalInputsBeforeClampAndIsImmutable()
        {
            var load = Load("SC-SharedTargets.glb");
            yield return load;
            var set = load.Current.GetComponent<KhrCharacter>().ExpressionResponses;
            var inputs = new Dictionary<string, double> { ["Together"] = 0.8, ["Half"] = 1, ["unknown"] = 1 };
            var mapped = set.EvaluateExpressionSetMapping("demo_v1", inputs);
            Assert.AreEqual(2, mapped.Count);
            Assert.AreEqual(1, mapped[0], 1e-6, "0.8 + 0.5 is clamped only after summing");
            Assert.AreEqual(0.8, mapped[1], 1e-6);
            inputs["Together"] = 0;
            Assert.AreEqual(0.8, mapped[1], 1e-6, "caller mutation cannot alter the completed result");
            Assert.Throws<NotSupportedException>(() => ((IList<double>)mapped)[0] = 0);
            var missing = set.EvaluateExpressionSetMapping("demo_v1", new Dictionary<string, double>());
            Assert.AreEqual(0, missing[0]); Assert.AreEqual(0, missing[1]);
            Assert.IsTrue(set.Validation.IsComplete, "independent mapping-entry name is not a mismatch");
        }

        [UnityTest]
        public IEnumerator NestedHints_UseTheSameViewAcrossAncestorsAndInstances()
        {
            var first = Load("VH-Ancestors.glb"); yield return first;
            var second = Load("VH-Ancestors.glb"); yield return second;
            var view = first.Current.GetComponent<ViewContextController>();
            var other = second.Current.GetComponent<ViewContextController>();
            Assert.IsNotNull(view); Assert.IsNotNull(other);
            var always = SandboxTestUtil.FindDeep(first.Current.transform, "AlwaysChild");
            var child = SandboxTestUtil.FindDeep(first.Current.transform, "FirstChild");
            var free = SandboxTestUtil.FindDeep(first.Current.transform, "FreeChild");
            var firstTags = new[] { "first_person" };
            var thirdTags = new[] { "third_person" };
            var bothTags = new[] { "first_person", "third_person" };
            Assert.IsFalse(view.ShouldRenderNodeForView(always, firstTags, true));
            Assert.IsTrue(view.ShouldRenderNodeForView(always, thirdTags, true));
            Assert.IsFalse(view.ShouldRenderNodeForView(child, firstTags, true));
            Assert.IsFalse(view.ShouldRenderNodeForView(child, thirdTags, true));
            Assert.IsTrue(view.ShouldRenderNodeForView(child, bothTags, true));
            Assert.IsTrue(view.ShouldRenderNodeForView(free, Array.Empty<string>(), true));
            var renderer = always.GetComponent<Renderer>();
            Assert.IsFalse(view.ShouldRenderPrimitiveForView(renderer, 0, firstTags, true));
            Assert.IsTrue(view.ShouldRenderPrimitiveForView(renderer, 0, thirdTags, true));
            Assert.IsFalse(view.ShouldRenderPrimitiveForView(renderer, 1, thirdTags, true));
            Assert.IsTrue(view.ShouldRenderPrimitiveForView(renderer, 1, bothTags, true));
            var otherAlways = SandboxTestUtil.FindDeep(second.Current.transform, "AlwaysChild");
            Assert.IsTrue(other.ShouldRenderNodeForView(otherAlways, thirdTags, true));
            Assert.IsFalse(view.ShouldRenderNodeForView(always, firstTags, true),
                "another instance's visible result cannot leak into this instance/view");
            Assert.IsFalse(view.ShouldRenderNodeForView(always, bothTags, false),
                "the independent ancestor-inclusive core visibility term always retains its veto");
        }

        [UnityTest]
        public IEnumerator CustomAncestorFallback_DoesNotBypassAnotherRestriction()
        {
            var load = Load("VH-Ancestors.glb"); yield return load;
            var view = load.Current.GetComponent<ViewContextController>();
            var unknown = SandboxTestUtil.FindDeep(load.Current.transform, "UnknownChild");
            var custom = SandboxTestUtil.FindDeep(load.Current.transform, "CustomChild");
            var always = SandboxTestUtil.FindDeep(load.Current.transform, "AlwaysChild");
            Assert.IsFalse(view.ShouldRenderNodeForView(unknown, Array.Empty<string>(), true));
            view.RenderUnsupportedRoles = true;
            Assert.IsTrue(view.ShouldRenderNodeForView(unknown, Array.Empty<string>(), true));
            var nestedFallback = SandboxTestUtil.FindDeep(load.Current.transform, "FallbackUnderThird");
            Assert.IsFalse(view.ShouldRenderNodeForView(nestedFallback, new[] { "first_person" }, true),
                "rendering an unsupported role locally cannot bypass a supported false ancestor");
            Assert.IsTrue(view.ShouldRenderNodeForView(nestedFallback, new[] { "third_person" }, true));
            Assert.IsFalse(view.ShouldRenderNodeForView(always, new[] { "first_person" }, true));
            Assert.IsFalse(view.ShouldRenderNodeForView(unknown, Array.Empty<string>(), false));
            view.RenderUnsupportedRoles = false;
            view.CustomRoleResolver = new MirrorProfile();
            Assert.IsTrue(view.ShouldRenderNodeForView(custom, new[] { "ACME_mirror" }, true));
            Assert.IsFalse(view.ShouldRenderNodeForView(custom, new[] { "third_person" }, true));
            Assert.IsFalse(view.ShouldRenderNodeForView(unknown, new[] { "ACME_mirror" }, true));
        }

        [UnityTest]
        public IEnumerator ImportedCoreVisibility_AndsAncestorsWithoutDisablingCameraOrAnimation()
        {
            var load = Load("VH-Ancestors.glb"); yield return load;
            var root = load.Current;
            var view = root.GetComponent<ViewContextController>();
            var parent = SandboxTestUtil.FindDeep(root.transform, "CoreParent");
            var child = SandboxTestUtil.FindDeep(root.transform, "CoreChild");
            var camera = SandboxTestUtil.FindDeep(root.transform, "CoreCamera").GetComponent<Camera>();
            var parentState = parent.GetComponent<CoreNodeVisibility>();
            var childState = child.GetComponent<CoreNodeVisibility>();
            Assert.IsNotNull(parentState); Assert.IsNotNull(childState); Assert.IsNotNull(camera);
            Assert.IsFalse(parentState.Visible); Assert.IsTrue(childState.Visible);
            Assert.IsTrue(parent.gameObject.activeSelf); Assert.IsTrue(child.gameObject.activeInHierarchy);
            Assert.IsTrue(camera.gameObject.activeInHierarchy, "core mesh visibility does not deactivate cameras");
            Assert.IsFalse(CoreNodeVisibility.IsVisibleInHierarchy(child, root.transform));
            Assert.IsFalse(view.ShouldRenderNodeForView(child, new[] { "first_person", "third_person" },
                CoreNodeVisibility.IsVisibleInHierarchy(child, root.transform)));
            yield return null;
            Assert.IsFalse(child.GetComponent<Renderer>().enabled, "the global core adapter suppresses mesh drawing");

            var animation = root.GetComponentInChildren<Animation>(true);
            Assert.IsNotNull(animation); Assert.IsTrue(animation.enabled);
            var motion = FindClip(animation, "AncestorMotion");
            var visible = FindClip(animation, "CoreVisibilityMotion");
            var third = SandboxTestUtil.FindDeep(root.transform, "ThirdParent");
            var always = SandboxTestUtil.FindDeep(root.transform, "AlwaysChild");
            Assert.IsFalse(view.ShouldRenderNodeForView(always, new[] { "first_person" }, true));
            motion.SampleAnimation(animation.gameObject, 0);
            Vector3 start = third.localPosition;
            motion.SampleAnimation(animation.gameObject, 0.5f);
            Assert.Greater(Vector3.Distance(start, third.localPosition), 0.2f,
                "an ancestor hint veto cannot freeze ordinary animation or transforms");
            Assert.IsTrue(third.gameObject.activeInHierarchy);
            var nestedCamera = SandboxTestUtil.FindDeep(root.transform, "NestedCamera").GetComponent<Camera>();
            Assert.IsNotNull(nestedCamera); Assert.IsTrue(nestedCamera.gameObject.activeInHierarchy);
            bool cameraEnabled = camera.enabled;
            bool nestedEnabled = nestedCamera.enabled;

            visible.SampleAnimation(animation.gameObject, 1);
            Assert.IsTrue(parentState.Visible, "KHR_animation_pointer animates the Boolean core property");
            Assert.IsTrue(CoreNodeVisibility.IsVisibleInHierarchy(child, root.transform));
            yield return null;
            Assert.IsTrue(child.GetComponent<Renderer>().enabled);
            visible.SampleAnimation(animation.gameObject, 0);
            Assert.IsFalse(CoreNodeVisibility.IsVisibleInHierarchy(child, root.transform));
            Assert.IsTrue(childState.Visible, "the child keeps its own authored/current true value");
            yield return null;
            Assert.IsFalse(child.GetComponent<Renderer>().enabled);
            Assert.AreEqual(cameraEnabled, camera.enabled);
            Assert.AreEqual(nestedEnabled, nestedCamera.enabled);
            Assert.IsTrue(animation.enabled);
            Assert.IsTrue(parent.gameObject.activeSelf); Assert.IsTrue(child.gameObject.activeSelf);
        }

        private SandboxTestUtil.SceneLoad Load(string name)
            => SandboxTestUtil.LoadSynthetic(name, _created, CharacterExpressionHostPolicy.Passive);

        private static void AssertValue(ExpressionComposition composition, string property, double[] expected)
        {
            Assert.IsTrue(composition.TryGetRecord(property, out var record), property);
            Assert.AreEqual(expected.Length, record.Value.Count, property);
            for (int i = 0; i < expected.Length; i++) Assert.AreEqual(expected[i], record.Value[i], 1e-6, property);
        }

        private static AnimationClip FindClip(Animation animation, string name)
        {
            foreach (AnimationState state in animation)
                if (state.clip.name.Contains(name)) return state.clip;
            Assert.Fail("Missing ordinary imported animation clip: " + name);
            return null;
        }

        private sealed class MirrorProfile : IVisibilityRoleResolver
        {
            public bool TryResolve(string role, IReadOnlyCollection<string> tags, out bool visible)
            {
                visible = false;
                if (role != "ACME_mirror_only") return false;
                foreach (var tag in tags) visible |= tag == "ACME_mirror";
                return true;
            }
        }
    }
}
