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
    /// Coverage for normative additive composition through the optional Unity ExpressionController. Two entries that
    /// drive one morph weight add their deltas over the authored base (y = b + sum(s - n)) with no clamp, and the full
    /// weight vector is written, so untouched shapes keep their base. LateUpdate-timed and asserted at the resolved
    /// blendshape output.
    /// </summary>
    public class SandboxCompositionTests
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
        public IEnumerator AdditiveComposition_SumsWithoutClamp()
        {
            var load = SandboxTestUtil.LoadSynthetic("SC-Face.glb", _created);
            yield return load;
            var hub = load.Current.GetComponent<KhrCharacter>();
            Assert.IsNotNull(hub, "SC-Face should import a KhrCharacter hub.");
            var ec = hub.Expressions;
            Assert.IsNotNull(ec, "SC-Face should have an ExpressionController.");
            var smr = load.Current.GetComponentInChildren<SkinnedMeshRenderer>();
            var mesh = smr.sharedMesh;
            int jaw = mesh.GetBlendShapeIndex("jawOpen");
            Assert.GreaterOrEqual(jaw, 0, "SC-Face should carry the shared 'jawOpen' blendshape.");

            ec.ResetAll();
            yield return null;
            var rest = new float[mesh.blendShapeCount];
            for (int shape = 0; shape < rest.Length; shape++) rest[shape] = smr.GetBlendShapeWeight(shape);

            ec.SetWeight("aa", 1f);
            yield return null;
            float aaOnly = smr.GetBlendShapeWeight(jaw);
            Assert.Greater(aaOnly, rest[jaw] + 1e-3f, "'aa' must actually drive the shared blendshape.");

            ec.ResetAll(); ec.SetWeight("jawOpen", 1f);
            yield return null;
            float jawOnly = smr.GetBlendShapeWeight(jaw);
            Assert.Greater(jawOnly, aaOnly + 1e-3f, "jawOpen (delta 1.0) should drive the shared blendshape harder than aa (0.6).");

            ec.ResetAll(); ec.SetWeight("aa", 1f); ec.SetWeight("jawOpen", 1f);
            yield return null;
            float both = smr.GetBlendShapeWeight(jaw);

            float frameWeight = mesh.GetBlendShapeFrameWeight(jaw, 0);
            float tol = 1e-3f * frameWeight;
            Assert.AreEqual(jawOnly + aaOnly - rest[jaw], both, tol,
                $"the contributions must add over the base without a clamp (aaOnly={aaOnly}, jawOnly={jawOnly}, both={both}).");
            Assert.Greater(both, frameWeight, "the unclamped sum exceeds a full weight of 1.");
            for (int shape = 0; shape < rest.Length; shape++)
                if (shape != jaw)
                    Assert.AreEqual(rest[shape], smr.GetBlendShapeWeight(shape), tol,
                        $"untouched blendshape {shape} ('{mesh.GetBlendShapeName(shape)}') must keep its base weight.");

            ec.ResetAll();
        }
    }
}
