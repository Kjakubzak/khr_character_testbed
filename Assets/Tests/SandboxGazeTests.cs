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
    /// Gaze direction depth (03 rig lens, Phase 4-P3). On a real imported SC-Face, an off-axis world target drives the
    /// correct directional look expression and saturates near 90 deg; an extreme/behind target stays clamped in [0,1]
    /// (never flips negative). Targets are placed RELATIVE TO THE GAZE FRAME (gaze.transform) so the assertions are
    /// invariant to the imported world orientation (handedness-safe). Anti-hollow via real plugin types.
    /// </summary>
    public class SandboxGazeTests
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
        public IEnumerator Gaze_DirectionalTarget_DrivesCorrectLookAndSaturates()
        {
            var load = SandboxTestUtil.LoadSynthetic("SC-Face.glb", _created);
            yield return load;
            var hub = load.Current.GetComponent<KhrCharacter>();
            var gaze = hub.Gaze;
            Assert.IsNotNull(gaze, "SC-Face has look expressions, so a GazeSolver must be attached.");
            var ec = hub.Expressions;
            Assert.IsNotNull(ec);

            var frame = gaze.transform;   // SC-Face has no mapped head bone, so the gaze frame is this transform
            gaze.Weight = 1f;

            // Target ~90 deg to the frame's RIGHT -> lookRight saturates, lookLeft ~0.
            gaze.SetWorldTarget(frame.position + frame.right * 5f);
            yield return null;   // GazeSolver (order 50)
            yield return null;   // ExpressionController (order 100)
            float right = ec.GetWeight(gaze.LookRight);
            float left = ec.GetWeight(gaze.LookLeft);
            Assert.Greater(right, 0.8f, "a target 90 deg to the right should saturate lookRight toward 1.");
            Assert.Greater(right, left + 0.5f, "lookRight must dominate lookLeft for a right-side target.");

            // Target ~90 deg ABOVE the frame -> lookUp dominates, lookDown ~0.
            gaze.SetWorldTarget(frame.position + frame.up * 5f);
            yield return null;
            float up = ec.GetWeight(gaze.LookUp);
            float down = ec.GetWeight(gaze.LookDown);
            Assert.Greater(up, 0.8f, "a target 90 deg above should saturate lookUp toward 1.");
            Assert.Greater(up, down + 0.5f, "lookUp must dominate lookDown for an upward target.");

            gaze.Weight = 0f;
        }

        [UnityTest]
        public IEnumerator Gaze_ExtremeTarget_ClampsWeightsToUnitRange()
        {
            var load = SandboxTestUtil.LoadSynthetic("SC-Face.glb", _created);
            yield return load;
            var hub = load.Current.GetComponent<KhrCharacter>();
            var gaze = hub.Gaze;
            Assert.IsNotNull(gaze);
            var ec = hub.Expressions;

            var frame = gaze.transform;
            gaze.Weight = 1f;

            // A behind-and-to-the-side target is degenerate; the solver must clamp (never flip to a negative weight).
            gaze.SetWorldTarget(frame.position - frame.forward * 5f + frame.right * 2f);
            yield return null;
            yield return null;
            foreach (var name in new[] { gaze.LookLeft, gaze.LookRight, gaze.LookUp, gaze.LookDown })
            {
                float w = ec.GetWeight(name);
                Assert.GreaterOrEqual(w, 0f, $"look weight '{name}' must never go negative (a behind target is clamped, not flipped).");
                Assert.LessOrEqual(w, 1f + 1e-4f, $"look weight '{name}' must clamp to <= 1.");
            }

            gaze.Weight = 0f;
        }
    }
}
