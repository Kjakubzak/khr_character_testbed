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
    /// ExpressionController and asserts the RESOLVED target output (blendshape weights / material property block),
    /// because there is no public masked-weight getter - the masked value is computed in LateUpdate and consumed
    /// internally. ExpressionController drives in LateUpdate (execution order 100), so each SetWeight is followed by
    /// one frame before asserting. References real plugin types, so it also acts as an anti-hollow gate.
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

        // SC-Face: "aa" drives blendshape "jawOpen" (linearly to 0.6); "happy" blend-masks "aa" (amount 1). Driving
        // happy must scale aa's effective contribution toward zero - observed on the shared blendshape's output.
        [UnityTest]
        public IEnumerator Mask_BlendReducesMaskedTargetOutput()
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
            float unmasked = smr.GetBlendShapeWeight(jawShape);
            Assert.Greater(unmasked, 1e-3f, "driving 'aa' alone should raise the shared blendshape weight.");

            // Now also drive happy, which blend-masks aa (amount 1): aa's effective output must drop sharply.
            ec.SetWeight("happy", 1f);
            yield return null;
            float masked = smr.GetBlendShapeWeight(jawShape);
            Assert.Less(masked, unmasked * 0.5f,
                $"driving 'happy' (blend-masks 'aa') must reduce aa's output (unmasked={unmasked}, masked={masked}).");

            ec.ResetAll();
        }

        // SC-Face declares a "demoVocabulary" set whose "Smile" target maps to happy(1.0) + aa(0.5). Driving the
        // vocabulary target must drive the mapped expressions' output (the "smile" blendshape from happy).
        [UnityTest]
        public IEnumerator Vocabulary_DrivesMappedExpressionOutput()
        {
            var load = SandboxTestUtil.LoadSynthetic("SC-Face.glb", _created);
            yield return load;
            var hub = load.Current.GetComponent<KhrCharacter>();
            var ec = hub.Expressions;
            Assert.IsNotNull(ec, "SC-Face should have an ExpressionController.");
            CollectionAssert.Contains(ec.VocabularySets, "demoVocabulary", "SC-Face should expose the demoVocabulary set.");
            var smr = load.Current.GetComponentInChildren<SkinnedMeshRenderer>();
            int smileShape = BlendShapeIndex(smr, "smile");
            Assert.GreaterOrEqual(smileShape, 0, "SC-Face mesh should carry the 'smile' blendshape driven by 'happy'.");

            ec.ResetAll();
            yield return null;
            float rest = smr.GetBlendShapeWeight(smileShape);

            ec.SetWeightByVocabulary("demoVocabulary", "Smile", 1f);
            yield return null;
            float driven = smr.GetBlendShapeWeight(smileShape);
            Assert.Greater(driven, rest + 1e-3f,
                "driving the 'Smile' vocabulary target should raise the mapped 'smile' blendshape output.");

            ec.ResetAll();
        }

        // SC-FacePlus carries a texture expression (UV-transform + index-swap). Driving it must change the renderer's
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
                    if (d != null && d.Kind == TexKind.UvTransform) { uv = d; texExpr = track.Name; break; }
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

        // SC-FacePlus exercises all five expression sub-domains; the hub must report each as a capability.
        [UnityTest]
        public IEnumerator CapabilityDetection_FacePlusReportsAllExpressionSubDomains()
        {
            var load = SandboxTestUtil.LoadSynthetic("SC-FacePlus.glb", _created);
            yield return load;
            var hub = load.Current.GetComponent<KhrCharacter>();
            Assert.IsNotNull(hub, "SC-FacePlus should import a KhrCharacter hub.");
            var caps = hub.Capabilities;
            Assert.IsNotNull(caps);
            foreach (var c in new[]
            {
                CharacterCapability.Morphtarget, CharacterCapability.Joint, CharacterCapability.Texture,
                CharacterCapability.Mask, CharacterCapability.Mapping,
            })
                CollectionAssert.Contains(caps, c, $"SC-FacePlus should report the {c} expression sub-domain capability.");
        }
    }
}
