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
    /// LOOK-01 (test-rig lens): full-disk wiring proof for KHR_node_lookat_target, which previously had zero
    /// coverage (bug B7). Imports the SC-LookAt fixture and asserts the importer rehydrated the authored look-at
    /// targets onto the KhrCharacter hub's GazeSolver, with node identity (by name) and the optional hint
    /// round-tripping (one named "primary" hint + one hint-less empty {} target). References real plugin types,
    /// so it also acts as an anti-hollow gate.
    /// </summary>
    public class SandboxLookAtTests
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
        public IEnumerator LookAt_AuthoredTargets_RoundTripOntoGaze()
        {
            var load = SandboxTestUtil.LoadSynthetic("SC-LookAt.glb", _created);
            yield return load;
            var scene = load.Current;

            var hub = scene.GetComponent<KhrCharacter>();
            Assert.IsNotNull(hub, "SC-LookAt should import a KhrCharacter hub.");

            // The fixture carries KHR_node_lookat_target (no expressions), so the importer must still attach a
            // GazeSolver purely to surface the authored targets.
            var gaze = hub.Gaze;
            Assert.IsNotNull(gaze, "SC-LookAt carries KHR_node_lookat_target, so the importer must attach a GazeSolver.");

            var targets = gaze.AuthoredTargets;
            Assert.IsNotNull(targets, "GazeSolver.AuthoredTargets should be populated from the imported look-at targets.");
            Assert.AreEqual(2, targets.Count, "SC-LookAt authored exactly two look-at target nodes.");

            // Node identity round-trips by name; the optional hint round-trips (one "primary", one hint-less {}).
            var byName = new Dictionary<string, LookAtTarget>();
            foreach (var t in targets)
            {
                Assert.IsNotNull(t.Node, "Each authored look-at target must resolve to a real imported node.");
                byName[t.Node.name] = t;
            }
            Assert.IsTrue(byName.ContainsKey("FocusTarget"), "The 'FocusTarget' look-at node should round-trip by name.");
            Assert.IsTrue(byName.ContainsKey("AuxTarget"), "The 'AuxTarget' look-at node should round-trip by name.");
            Assert.AreEqual("primary", byName["FocusTarget"].Hint, "FocusTarget's authored hint should round-trip.");
            Assert.IsTrue(string.IsNullOrEmpty(byName["AuxTarget"].Hint),
                "AuxTarget was authored hint-less (empty {} target): its hint must round-trip as null/empty.");
        }
    }
}
