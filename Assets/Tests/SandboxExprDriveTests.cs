using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityGLTF.KhrCharacter;
using Samples.Shared;

namespace KhrCharacterTestbed.Tests
{
    /// <summary>
    /// Expression-drive semantics on a real imported character (01 expr lens, Phase 3-P2). Drives expressions via
    /// ExpressionController and asserts the RESOLVED target output (blendshape weights / material property block), so
    /// the suppressed driver is observed through its effect. ExpressionController drives in LateUpdate (execution
    /// order 100), so each SetWeight is followed by one frame before asserting. References real plugin types, so it
    /// also acts as an anti-hollow gate.
    /// </summary>
    public class SandboxExprDriveTests
    {
        private readonly List<Object> _created = new List<Object>();

        [TearDown]
        public void TearDown()
        {
            foreach (var o in _created)
                if (o != null) Object.DestroyImmediate(o);
            _created.Clear();
        }

        private static int BlendShapeIndex(SkinnedMeshRenderer smr, string name)
        {
            int i = smr.sharedMesh.GetBlendShapeIndex(name);
            return i;
        }

        // SC-Face: "aa" drives blendshape "jawOpen" (linearly to 0.6); "happy" blend-suppresses "aa" (amount 1).
        // Driving happy must scale aa's effective driver toward zero - observed on the shared blendshape's output.
        [UnityTest]
        public IEnumerator Suppression_BlendReducesSuppressedTargetOutput()
        {
            var load = SandboxTestUtil.LoadSynthetic("SC-Face.glb", _created);
            yield return load;
            var hub = load.Current.GetComponent<KhrCharacter>();
            Assert.IsNotNull(hub, "SC-Face should import a KhrCharacter hub.");
            var ec = hub.Expressions;
            Assert.IsNotNull(ec, "SC-Face should have an ExpressionController.");
            var smr = load.Current.GetComponentInChildren<SkinnedMeshRenderer>();
            Assert.IsNotNull(smr, "SC-Face should import a SkinnedMeshRenderer.");
            int jawShape = BlendShapeIndex(smr, "jawOpen");
            Assert.GreaterOrEqual(jawShape, 0, "SC-Face mesh should carry the 'jawOpen' blendshape that 'aa' drives.");

            // Drive ONLY aa: its contribution to the shared blendshape is visible (jawOpen track left at 0).
            ec.ResetAll();
            ec.SetWeight("aa", 1f);
            yield return null;
            float unsuppressed = smr.GetBlendShapeWeight(jawShape);
            Assert.Greater(unsuppressed, 1e-3f, "driving 'aa' alone should raise the shared blendshape weight.");

            // Now also drive happy, which blend-suppresses aa (amount 1): aa's effective output must drop sharply.
            ec.SetWeight("happy", 1f);
            yield return null;
            float suppressed = smr.GetBlendShapeWeight(jawShape);
            Assert.Less(suppressed, unsuppressed * 0.5f,
                $"driving 'happy' (blend-suppresses 'aa') must reduce aa's output (unsuppressed={unsuppressed}, suppressed={suppressed}).");

            ec.ResetAll();
        }

        // SC-ExprEdge: "edgeB" BLOCK-suppresses "edgeA" (amount 1, threshold 0). Driving edgeB above the threshold must
        // fully zero edgeA's output, while edgeB still drives its own shape - the block counterpart to the blend case.
        [UnityTest]
        public IEnumerator Suppression_BlockFullyBlocksTargetAboveThreshold()
        {
            var load = SandboxTestUtil.LoadSynthetic("SC-ExprEdge.glb", _created);
            yield return load;
            var hub = load.Current.GetComponent<KhrCharacter>();
            Assert.IsNotNull(hub, "SC-ExprEdge should import a KhrCharacter hub.");
            var ec = hub.Expressions;
            Assert.IsNotNull(ec, "SC-ExprEdge should have an ExpressionController.");
            var smr = load.Current.GetComponentInChildren<SkinnedMeshRenderer>();
            int shapeA = BlendShapeIndex(smr, "shapeA");
            int shapeB = BlendShapeIndex(smr, "shapeB");
            Assert.GreaterOrEqual(shapeA, 0, "SC-ExprEdge should carry 'shapeA'.");
            Assert.GreaterOrEqual(shapeB, 0, "SC-ExprEdge should carry 'shapeB'.");

            ec.ResetAll(); ec.SetWeight("edgeA", 1f);
            yield return null;
            float aUnblocked = smr.GetBlendShapeWeight(shapeA);
            Assert.Greater(aUnblocked, 1e-3f, "edgeA alone should drive shapeA.");

            // Drive edgeB: it block-suppresses edgeA above threshold 0, fully zeroing edgeA, while driving its own shapeB.
            ec.SetWeight("edgeB", 1f);
            yield return null;
            float aBlocked = smr.GetBlendShapeWeight(shapeA);
            float bOut = smr.GetBlendShapeWeight(shapeB);
            Assert.Less(aBlocked, aUnblocked * 0.1f, "a block suppression above threshold must fully reduce the target (edgeA -> ~0).");
            Assert.Greater(bOut, 1e-3f, "edgeB should still drive its own shapeB while blocking edgeA.");

            ec.ResetAll();
        }

        // The sole wire mapping operation takes external inputs to native drivers, before suppression.
        [UnityTest]
        public IEnumerator Vocabulary_DrivesMappedExpressionOutput()
        {
            var load = SandboxTestUtil.LoadSynthetic("SC-Face.glb", _created);
            yield return load;
            var ec = load.Current.GetComponent<KhrCharacter>().Expressions;
            Assert.IsNotNull(ec);
            const string vocabulary = "demo_v1";
            CollectionAssert.Contains(ec.InputVocabularySets, vocabulary);
            var smr = load.Current.GetComponentInChildren<SkinnedMeshRenderer>();
            int smileShape = BlendShapeIndex(smr, "smile");
            Assert.GreaterOrEqual(smileShape, 0);
            ec.ResetAll();
            yield return null;
            float rest = smr.GetBlendShapeWeight(smileShape);
            Assert.IsTrue(ec.SelectVocabularyInputSet(vocabulary));
            ec.SetWeightByVocabulary(vocabulary, "Smile", 1f);
            yield return null;
            Assert.Greater(smr.GetBlendShapeWeight(smileShape), rest + 1e-3f);
            ec.UseDirectNativeInputs();
            ec.ResetAll();
        }

        [UnityTest]
        public IEnumerator Vocabulary_MapsNativeSnapshotBeforeSuppression()
        {
            var load = SandboxTestUtil.LoadSynthetic("SC-Face.glb", _created);
            yield return load;
            var ec = load.Current.GetComponent<KhrCharacter>().Expressions;
            Assert.IsNotNull(ec);
            ec.ResetAll();
            Assert.IsTrue(ec.SelectVocabularyInputSet("demo_v1"));
            ec.SetWeightByVocabulary("demo_v1", "Smile", 0.8f);
            yield return null;
            Assert.IsNotNull(ec.Snapshot);
            int happy = FindHandle(ec, "happy");
            int aa = FindHandle(ec, "aa");
            Assert.AreEqual(0.8, ec.Snapshot.U[happy], 1e-6);
            Assert.AreEqual(0.4, ec.Snapshot.U[aa], 1e-6);
            Assert.AreEqual(0.08, ec.Snapshot.V[aa], 1e-6,
                "mapping runs before happy's blend suppression attenuates aa");
            ec.UseDirectNativeInputs();
            ec.ResetAll();
        }

        // SC-FacePlus carries a UV-transform texture expression. Driving it must change the renderer's
        // per-slot MaterialPropertyBlock _ST vector (the runtime applier writes an MPB, NOT renderer.material).
        [UnityTest]
        public IEnumerator Texture_UvTransform_DrivesMaterialPropertyBlockST()
        {
            var load = SandboxTestUtil.LoadSynthetic("SC-FacePlus.glb", _created);
            yield return load;
            var hub = load.Current.GetComponent<KhrCharacter>();
            var ec = hub.Expressions;
            Assert.IsNotNull(ec, "SC-FacePlus should have an ExpressionController.");

            // Find the imported UV-transform driver + the expression that owns it.
            TextureDriver uv = null; string texExpr = null;
            var set = ec.Set ?? ec.BakedSet;
            Assert.IsNotNull(set, "the imported expression set should be available.");
            foreach (var track in set.Expressions)
            {
                if (track?.TextureDrivers == null) continue;
                foreach (var d in track.TextureDrivers)
                    if (d != null) { uv = d; texExpr = track.Name; break; }
                if (uv != null) break;
            }
            Assert.IsNotNull(uv, "SC-FacePlus should import a UV-transform texture driver.");
            Assert.IsNotNull(uv.Renderer, "the imported texture driver should resolve its target renderer.");

            var mpb = new MaterialPropertyBlock();
            ec.ResetAll();
            yield return null;
            uv.Renderer.GetPropertyBlock(mpb, uv.SubmeshSlot);
            Vector4 stRest = mpb.GetVector(uv.PropertyId);

            ec.SetWeight(texExpr, 1f);
            yield return null;
            uv.Renderer.GetPropertyBlock(mpb, uv.SubmeshSlot);
            Vector4 stDriven = mpb.GetVector(uv.PropertyId);

            Assert.Greater((stDriven - stRest).magnitude, 1e-3f,
                $"driving '{texExpr}' must change the renderer's _ST property block (rest={stRest}, driven={stDriven}).");

            ec.ResetAll();
        }

        private static readonly CharacterCapability[] ExpressionSubDomains =
        {
            CharacterCapability.Morphtarget, CharacterCapability.Joint, CharacterCapability.TextureTransform,
            CharacterCapability.Suppression, CharacterCapability.Mapping,
        };

        // SC-FacePlus exercises all five expression sub-domains; the hub must report each as an active capability.
        [UnityTest]
        public IEnumerator CapabilityDetection_FacePlusReportsAllExpressionSubDomains()
        {
            var load = SandboxTestUtil.LoadSynthetic("SC-FacePlus.glb", _created);
            yield return load;
            var hub = load.Current.GetComponent<KhrCharacter>();
            Assert.IsNotNull(hub, "SC-FacePlus should import a KhrCharacter hub.");
            var caps = hub.Capabilities;
            Assert.IsNotNull(caps);
            foreach (var c in ExpressionSubDomains)
            {
                CollectionAssert.Contains(caps, c, $"SC-FacePlus should report the {c} expression sub-domain capability.");
                Assert.AreEqual(CapabilityStatus.Active, StatusOf(hub, c), $"{c} should be active.");
            }
        }

        // The passive response set implements every expression extension, so the sub-domains are active without the
        // optional Unity controller; capabilities come from the asset's declarations.
        [UnityTest]
        public IEnumerator CapabilityDetection_PassiveImportReportsActiveSubDomainsWithoutController()
        {
            var load = SandboxTestUtil.LoadSynthetic("SC-FacePlus.glb", _created, CharacterExpressionHostPolicy.Passive);
            yield return load;
            var hub = load.Current.GetComponent<KhrCharacter>();
            Assert.IsNotNull(hub, "SC-FacePlus should import a KhrCharacter hub.");
            Assert.IsNull(hub.Expressions, "the passive policy creates no controller.");
            Assert.IsNotNull(hub.ExpressionResponses);
            foreach (var c in ExpressionSubDomains)
                Assert.AreEqual(CapabilityStatus.Active, StatusOf(hub, c),
                    $"{c} should be active through the passive response set alone.");
        }

        private static CapabilityStatus StatusOf(KhrCharacter hub, CharacterCapability capability)
        {
            foreach (var health in hub.GetHealth().Capabilities)
                if (health.Capability == capability) return health.Status;
            Assert.Fail($"Health report has no {capability} capability to check.");
            return default;
        }

        private static int FindHandle(ExpressionController controller, string name)
        {
            for (int index = 0; index < controller.Count; index++)
                if (controller.Expressions[index].Name == name) return index;
            Assert.Fail($"Expression '{name}' is not on the controller.");
            return -1;
        }
    }
}
