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

        // SC-Face independently authors both mapping directions for one versioned vocabulary. Forward mapping reads
        // native drivers; input mapping accepts the endpoint command and drives the authored native targets.
        [UnityTest]
        public IEnumerator Vocabulary_DrivesMappedExpressionOutput()
        {
            var load = SandboxTestUtil.LoadSynthetic("SC-Face.glb", _created);
            yield return load;
            var hub = load.Current.GetComponent<KhrCharacter>();
            var ec = hub.Expressions;
            Assert.IsNotNull(ec, "SC-Face should have an ExpressionController.");
            const string vocabulary = "https://example.com/expression-vocabularies/demo/v1";
            CollectionAssert.Contains(ec.VocabularySets, vocabulary, "SC-Face should expose the demo vocabulary set.");
            CollectionAssert.Contains(ec.OutputVocabularySets, vocabulary,
                "the native-to-endpoint operation must be authored explicitly");
            CollectionAssert.Contains(ec.InputVocabularySets, vocabulary,
                "the endpoint-to-native operation must be authored explicitly");
            var smr = load.Current.GetComponentInChildren<SkinnedMeshRenderer>();
            int smileShape = BlendShapeIndex(smr, "smile");
            Assert.GreaterOrEqual(smileShape, 0, "SC-Face mesh should carry the 'smile' blendshape driven by 'happy'.");

            ec.SetWeight("happy", 0.5f);
            var outputs = ec.EvaluateVocabularyOutputs(vocabulary);
            Assert.IsTrue(outputs.TryGetValue("Smile", out float smileOutput));
            Assert.AreEqual(0.5f, smileOutput, 1e-6f,
                "native-to-endpoint evaluation must use only the explicitly authored forward operation");

            ec.ResetAll();
            yield return null;
            float rest = smr.GetBlendShapeWeight(smileShape);

            Assert.IsTrue(ec.SelectVocabularyInputSet(vocabulary),
                "the separately authored endpoint-to-native operation must be selected explicitly");
            ec.SetWeightByVocabulary(vocabulary, "Smile", 1f);
            yield return null;
            float driven = smr.GetBlendShapeWeight(smileShape);
            Assert.Greater(driven, rest + 1e-3f,
                "driving the 'Smile' vocabulary target should raise the mapped 'smile' blendshape output.");

            ec.SetWeightByVocabulary(vocabulary, "Smile", 0f);
            ec.UseDirectNativeInputs();
            ec.ResetAll();
        }

        // Smile = 1.0 * happy + 0.5 * aa. "happy" blend-suppresses "aa", but the forward mapping reads the requested
        // drivers u, never the suppressed vector v: 0.5 + 0.5 * 0.6 = 0.8, not 0.5 + 0.5 * (0.6 * (1 - 0.5)) = 0.65.
        [UnityTest]
        public IEnumerator Vocabulary_OutputReadsPreSuppressionDrivers()
        {
            var load = SandboxTestUtil.LoadSynthetic("SC-Face.glb", _created);
            yield return load;
            var ec = load.Current.GetComponent<KhrCharacter>().Expressions;
            Assert.IsNotNull(ec, "SC-Face should have an ExpressionController.");
            const string vocabulary = "https://example.com/expression-vocabularies/demo/v1";

            ec.ResetAll();
            ec.SetWeight("happy", 0.5f);
            ec.SetWeight("aa", 0.6f);
            yield return null;
            Assert.IsTrue(ec.EvaluateVocabularyOutputs(vocabulary).TryGetValue("Smile", out float smile));
            Assert.AreEqual(0.8f, smile, 1e-6f, "endpoint outputs must come from the pre-suppression drivers u.");
            Assert.IsNotNull(ec.Snapshot, "the controller should have built a driver snapshot.");
            Assert.AreEqual(0.3, ec.Snapshot.V[FindHandle(ec, "aa")], 1e-6,
                "the suppression still applies to the response driver v.");

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
