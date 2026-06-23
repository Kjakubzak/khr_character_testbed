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
    /// Phase-4b functional proofs (bounded PlayMode) for the N-series demos: N1 (additive vs override), N2 (mask +
    /// vocabulary), N4 (view-mode visibility), N8 (eye-aim), N9 (extract/resolve round-trip). All reference real
    /// plugin types, so they also act as anti-hollow gates.
    /// </summary>
    public class SandboxNSeriesTests
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
        public IEnumerator N1_OverrideChangesComposedWeightVsAdditive()
        {
            var scene = LoadScene("SC-FacePlus.glb");
            yield return scene;
            var go = scene.Current;
            var controller = go.GetComponent<KhrCharacter>().Expressions;
            var smr = go.GetComponentInChildren<SkinnedMeshRenderer>();
            Assert.IsNotNull(controller); Assert.IsNotNull(smr);

            // "aa" + "jawOpen" both drive blendshape 0. Additive sums (and saturates); Override takes the winner.
            SetPairBlendMode(controller, ExpressionBlendMode.Additive);
            controller.SetWeight("jawOpen", 1f);
            controller.SetWeight("aa", 1f);
            yield return null; yield return null;
            float additive = smr.GetBlendShapeWeight(0);

            SetPairBlendMode(controller, ExpressionBlendMode.Override);
            yield return null; yield return null;
            float overridden = smr.GetBlendShapeWeight(0);

            Assert.Greater(additive, overridden,
                $"Additive ({additive}) should exceed Override ({overridden}) when both drive blendshape 0.");
            controller.ResetAll();
        }

        [UnityTest]
        public IEnumerator N2_MaskAttenuates_AndVocabularyDrivesSources()
        {
            var scene = LoadScene("SC-FacePlus.glb");
            yield return scene;
            var go = scene.Current;
            var controller = go.GetComponent<KhrCharacter>().Expressions;
            var smr = go.GetComponentInChildren<SkinnedMeshRenderer>();
            Assert.IsNotNull(controller); Assert.IsNotNull(smr);

            // Mask: "happy" blend-masks "aa". With happy active, aa's contribution to blendshape 0 collapses.
            controller.ResetAll();
            controller.SetWeight("aa", 1f);
            yield return null; yield return null;
            float aaAlone = smr.GetBlendShapeWeight(0);

            controller.SetWeight("happy", 1f);
            yield return null; yield return null;
            float aaMasked = smr.GetBlendShapeWeight(0);
            Assert.Greater(aaAlone, aaMasked, "'happy' should blend-mask (attenuate) 'aa' on blendshape 0.");

            // Vocabulary: the "Smile" target maps to 2 sources and, when driven, activates at least one of them.
            controller.ResetAll();
            CollectionAssert.Contains((ICollection)controller.VocabularySets, "demoVocabulary");
            CollectionAssert.Contains((ICollection)controller.VocabularyExpressions("demoVocabulary"), "Smile");
            Assert.AreEqual(2, SmileContributionCount(controller), "'Smile' should map to two source expressions.");

            controller.SetWeightByVocabulary("demoVocabulary", "Smile", 1f);
            yield return null; yield return null;
            float smile = smr.GetBlendShapeWeight(6); // 'happy' drives the smile blendshape (index 6)
            Assert.Greater(smile, 0f, "Driving the 'Smile' vocabulary target should activate its 'happy' source.");
            controller.ResetAll();
        }

        [UnityTest]
        public IEnumerator N4_ViewMode_TogglesRendererEnabled()
        {
            var scene = LoadScene("SC-Face.glb");
            yield return scene;
            var go = scene.Current;
            var smr = go.GetComponentInChildren<SkinnedMeshRenderer>();
            Assert.IsNotNull(smr);

            // The importer auto-attaches a ViewModeController ([DisallowMultipleComponent]); reuse it rather than
            // AddComponent (which returns null on an already-present, disallow-multiple component).
            var hub = go.GetComponent<KhrCharacter>();
            var viewMode = hub != null ? hub.View : null;
            if (viewMode == null) viewMode = go.GetComponent<ViewModeController>();
            if (viewMode == null) viewMode = go.AddComponent<ViewModeController>();
            Assert.IsNotNull(viewMode, "Expected a ViewModeController on the imported character.");

            viewMode.Mode = ViewModeController.ViewMode.ThirdPerson; // known starting state
            viewMode.RegisterRenderer(smr, ViewModeController.RenderView.ThirdPersonOnly);
            Assert.IsTrue(smr.enabled, "Third-person (default) should show the head renderer.");

            viewMode.Mode = ViewModeController.ViewMode.FirstPerson;
            Assert.IsFalse(smr.enabled, "First-person should hide the third-person-only head renderer.");

            viewMode.Mode = ViewModeController.ViewMode.ThirdPerson;
            Assert.IsTrue(smr.enabled, "Returning to third-person should show the head renderer again.");
            yield return null;
        }

        [UnityTest]
        public IEnumerator N8_EyeAimConstraint_RotatesEyeBoneTowardTarget()
        {
            var scene = LoadScene("SC-Face.glb");
            yield return scene;
            var go = scene.Current;

            var leftEye = FindDeep(go.transform, "LeftEye");
            Assert.IsNotNull(leftEye, "SC-Face should carry a 'LeftEye' bone for the eye-aim demo.");
            var rest = leftEye.localRotation;

            var target = new GameObject("EyeAimTarget");
            _created.Add(target);
            target.transform.position = go.transform.position + new Vector3(2f, 0f, 2f); // off to the side + front

            var eyeAim = go.AddComponent<EyeAimConstraint>();
            eyeAim.SetEyeBones(leftEye, FindDeep(go.transform, "RightEye"), go.transform);
            eyeAim.Target = target.transform;
            eyeAim.Mode = EyeAimConstraint.LookAtMode.CustomTarget;
            eyeAim.Weight = 1f;

            yield return null; yield return null; // EyeAimConstraint runs in LateUpdate

            Assert.Greater(Quaternion.Angle(leftEye.localRotation, rest), 1f,
                "The eye bone should rotate toward an off-axis target.");
        }

        [UnityTest]
        public IEnumerator N9_ExtractResolve_PreservesExpressionCount()
        {
            var scene = LoadScene("SC-Face.glb");
            yield return scene;
            var go = scene.Current;
            var controller = go.GetComponent<KhrCharacter>().Expressions;
            Assert.IsNotNull(controller);

            var baked = controller.Set;
            Assert.IsNotNull(baked); Assert.IsNotNull(baked.Expressions);
            int original = baked.Expressions.Length;
            Assert.Greater(original, 0);

            var root = controller.transform;
            var bindings = CharacterExpressionSetAsset.Extract(baked, root);
            var resolved = CharacterExpressionSetAsset.Resolve(bindings, root);

            Assert.IsNotNull(resolved.Expressions);
            Assert.AreEqual(original, resolved.Expressions.Length,
                "Extract -> Resolve should preserve the expression count.");
        }

        [UnityTest]
        public IEnumerator Neutralize_VrmOriginCharacter_ReexportsVendorNeutral()
        {
            string path = Path.Combine(Application.dataPath, "SampleAssets/khr-character-example.glb");
            if (!File.Exists(path))
                Assert.Ignore("VRM-origin sample not present (git-ignored, bring-your-own); skipping.");

            var task = CharacterLoader.LoadAsync(path, null);
            float deadline = Time.realtimeSinceStartup + 60f; // complex asset -> longer budget
            while (!task.IsCompleted && Time.realtimeSinceStartup < deadline) yield return null;
            Assert.IsTrue(task.IsCompleted, "VRM-origin import did not complete within 60s.");
            if (task.Exception != null) throw task.Exception;

            var scene = task.Result;
            Assert.IsNotNull(scene, "Imported scene root is null.");
            _created.Add(scene);

            var hub = scene.GetComponent<KhrCharacter>();
            Assert.IsNotNull(hub, "The VRM-origin character should import with KHR_character data (VRMC_* ignored).");
            var ec = hub.Expressions;
            Assert.IsNotNull(ec);
            Assert.GreaterOrEqual(ec.Count, 1, "Expected at least one KHR expression on the VRM-origin character.");

            byte[] glb = CharacterLoader.ExportToGlb(hub.gameObject, out var root);
            Assert.IsNotNull(glb); Assert.Greater(glb.Length, 0); Assert.IsNotNull(root);

            // Vendor-neutral guarantee: UnityGLTF has no VRMC_* importer, so they cannot survive into the re-export.
            // (extensionsRequired may legitimately carry a RATIFIED KHR_ extension - e.g. KHR_materials_unlit from a
            // VRoid MToon/unlit material - which is still vendor-neutral; we assert no-vendor and log the rest.)
            if (root.ExtensionsUsed != null)
                foreach (var e in root.ExtensionsUsed)
                    Assert.IsFalse(IsVendor(e), $"Re-export extensionsUsed must be vendor-neutral; found '{e}'.");
            if (root.ExtensionsRequired != null)
            {
                foreach (var e in root.ExtensionsRequired)
                    Assert.IsFalse(IsVendor(e), $"Re-export extensionsRequired must be vendor-neutral; found '{e}'.");
                if (root.ExtensionsRequired.Count > 0)
                    Debug.Log("[Neutralize] re-export extensionsRequired (ratified/non-vendor): " +
                        string.Join(", ", root.ExtensionsRequired));
            }
        }

        [UnityTest]
        public IEnumerator Showcase_LoadsAndDrivesCharacter()
        {
            // Mirror the showcase's path logic: hero if present, else the committed synthetic fallback.
            string heroPath = Path.Combine(Application.dataPath, "SampleAssets/khr-character-example.glb");
            bool hero = File.Exists(heroPath);
            string path = hero ? heroPath : CharacterLoader.SyntheticPath("SC-FacePlus.glb");
            Assert.IsTrue(File.Exists(path), $"Showcase fallback asset missing at '{path}'. Run Generate Sample Characters first.");

            var task = CharacterLoader.LoadAsync(path, null);
            float deadline = Time.realtimeSinceStartup + (hero ? 60f : 30f);
            while (!task.IsCompleted && Time.realtimeSinceStartup < deadline) yield return null;
            Assert.IsTrue(task.IsCompleted, "Showcase character import did not complete in time.");
            if (task.Exception != null) throw task.Exception;

            var scene = task.Result;
            Assert.IsNotNull(scene, "Imported scene root is null.");
            _created.Add(scene);

            var hub = scene.GetComponent<KhrCharacter>();
            Assert.IsNotNull(hub, "Showcase character should import with KHR_character data.");
            var ec = hub.Expressions;
            Assert.IsNotNull(ec);
            Assert.GreaterOrEqual(ec.Count, 1, "Showcase character should expose at least one expression.");

            // Hero-only: the VRoid character also carries skeleton mapping + camera hints (SkeletonMapping + CameraHint
            // capabilities). The fallback (SC-FacePlus) legitimately has neither, so a fresh public clone still passes.
            if (hero)
            {
                Assert.IsNotNull(hub.Skeleton, "Hero should carry a skeleton mapping (SkeletonMapping capability).");
                Assert.IsNotNull(hub.CameraHints, "Hero should carry camera hints (CameraHint capability).");
                Assert.Greater(hub.CameraHints.Hints.Count, 0, "Hero should expose at least one camera-hint role.");
            }
        }

        // ── helpers ──────────────────────────────────────────────────────────

        private static bool IsVendor(string extension) =>
            extension != null && extension.IndexOf("VRM", System.StringComparison.OrdinalIgnoreCase) >= 0;

        private static void SetPairBlendMode(ExpressionController controller, ExpressionBlendMode mode)
        {
            var set = controller.Set;
            if (set?.Expressions == null) return;
            foreach (var track in set.Expressions)
                if (track != null && (track.Name == "aa" || track.Name == "jawOpen"))
                    track.BlendMode = mode;
        }

        private static int SmileContributionCount(ExpressionController controller)
        {
            var sets = controller.Set?.MappingSets;
            if (sets == null) return 0;
            foreach (var ms in sets)
            {
                if (ms == null || ms.SetName != "demoVocabulary" || ms.Targets == null) continue;
                foreach (var target in ms.Targets)
                    if (target != null && target.TargetName == "Smile")
                        return target.Contributions?.Length ?? 0;
            }
            return 0;
        }

        private static Transform FindDeep(Transform root, string name)
        {
            foreach (var t in root.GetComponentsInChildren<Transform>(true))
                if (t != null && t.name == name) return t;
            return null;
        }

        // Loads a synthetic fixture and yields until ready; exposes the scene root via .Current.
        private SceneLoad LoadScene(string fileName) => new SceneLoad(this, fileName);

        private sealed class SceneLoad : CustomYieldInstruction
        {
            private readonly SandboxNSeriesTests _owner;
            private readonly Task<GameObject> _task;
            private readonly float _deadline;
            public GameObject Current { get; private set; }

            public SceneLoad(SandboxNSeriesTests owner, string fileName)
            {
                _owner = owner;
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
                    _owner._created.Add(Current);
                    return false;
                }
            }
        }
    }
}
