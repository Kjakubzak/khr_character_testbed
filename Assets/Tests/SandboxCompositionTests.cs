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
    /// Expression composition + clamp saturation on a real import (01 expr lens, Phase 4-P3). SC-Face's "aa"
    /// (delta -> 0.6) and "jawOpen" (delta -> 1.0) both drive the same blendshape additively; driving both must
    /// clamp the accumulated input at the ceiling (it cannot exceed jawOpen alone, which already saturates). Asserts
    /// the resolved blendshape OUTPUT (there is no public masked/clamped-weight getter). LateUpdate-timed. Anti-hollow.
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
        public IEnumerator AdditiveComposition_SaturatesAtClampCeiling()
        {
            var load = SandboxTestUtil.LoadSynthetic("SC-Face.glb", _created);
            yield return load;
            var hub = load.Current.GetComponent<KhrCharacter>();
            Assert.IsNotNull(hub, "SC-Face should import a KhrCharacter hub.");
            var ec = hub.Expressions;
            Assert.IsNotNull(ec, "SC-Face should have an ExpressionController.");
            var smr = load.Current.GetComponentInChildren<SkinnedMeshRenderer>();
            int jaw = smr.sharedMesh.GetBlendShapeIndex("jawOpen");
            Assert.GreaterOrEqual(jaw, 0, "SC-Face should carry the shared 'jawOpen' blendshape.");

            ec.ResetAll(); ec.SetWeight("aa", 1f);
            yield return null;
            float aaOnly = smr.GetBlendShapeWeight(jaw);

            ec.ResetAll(); ec.SetWeight("jawOpen", 1f);
            yield return null;
            float jawOnly = smr.GetBlendShapeWeight(jaw);

            ec.ResetAll(); ec.SetWeight("aa", 1f); ec.SetWeight("jawOpen", 1f);
            yield return null;
            float both = smr.GetBlendShapeWeight(jaw);

            // Distinct contributors: jawOpen (delta 1.0) drives the blendshape harder than aa (delta 0.6).
            Assert.Greater(jawOnly, aaOnly + 1e-3f, "jawOpen should drive the shared blendshape harder than aa.");
            // Clamp: jawOpen alone already saturates the accumulated input (clamp01(1.0)); adding aa (clamp01(1.6))
            // must NOT exceed that ceiling.
            float tol = Mathf.Max(0.02f, jawOnly * 0.01f);
            Assert.AreEqual(jawOnly, both, tol,
                $"additive accumulation must clamp at the ceiling (aaOnly={aaOnly}, jawOnly={jawOnly}, both={both}).");
            Assert.LessOrEqual(both, jawOnly + tol, "the clamped sum must not overshoot the ceiling.");

            ec.ResetAll();
        }
    }
}
