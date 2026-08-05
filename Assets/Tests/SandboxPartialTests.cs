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
    /// Graceful-degradation proof: a KHR character that declares only a SUBSET of capabilities (SC-Partial: a single
    /// morph expression, nothing else) must import cleanly, surface exactly the present capabilities, and leave the
    /// absent ones (skeleton, reference pose, camera hint, look-at, and the other expression sub-domains) cleanly
    /// absent - no throw, no half-state. References real plugin types, so it also acts as an anti-hollow gate.
    /// </summary>
    public class SandboxPartialTests
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
        public IEnumerator Partial_SurfacesOnlyDeclaredCapabilities()
        {
            var load = SandboxTestUtil.LoadSynthetic(
                "SC-Partial.glb",
                _created,
                CharacterExpressionHostPolicy.LegacyControllerWithSuppression);
            yield return load;
            var scene = load.Current;

            var hub = scene.GetComponent<KhrCharacter>();
            Assert.IsNotNull(hub, "SC-Partial should import a KhrCharacter hub.");

            // Present: the hub + passive response data, with the optional controller explicitly selected for this
            // capability-surface test.
            Assert.IsNotNull(hub.ExpressionResponses,
                "SC-Partial declares a morph expression, so passive response data must be present.");
            Assert.IsNotNull(hub.Expressions,
                "the selected Unity host policy should expose the optional controller adapter.");
            var caps = hub.Capabilities;
            Assert.IsNotNull(caps, "hub.Capabilities should be populated.");
            CollectionAssert.Contains(caps, CharacterCapability.Character, "Character capability must be present.");
            CollectionAssert.Contains(caps, CharacterCapability.Expression, "Expression capability must be present.");
            CollectionAssert.Contains(caps, CharacterCapability.Morphtarget, "Morphtarget capability must be present.");

            // Absent: every capability the fixture did NOT author must be reported absent.
            foreach (var absent in new[]
            {
                CharacterCapability.Joint, CharacterCapability.Texture, CharacterCapability.Mask,
                CharacterCapability.Mapping, CharacterCapability.SkeletonMapping, CharacterCapability.ReferencePose,
                CharacterCapability.CameraHint, CharacterCapability.LookAtTarget,
            })
                CollectionAssert.DoesNotContain(caps, absent, $"SC-Partial must NOT report the {absent} capability.");

            // Absent capabilities degrade to null hub accessors (no throw, no half-state).
            Assert.IsNull(hub.Skeleton, "SC-Partial has no skeleton mapping; hub.Skeleton must be null.");
            Assert.IsNull(hub.CameraHints, "SC-Partial has no camera hint; hub.CameraHints must be null.");
            Assert.IsNull(hub.LookAtTargets,
                "SC-Partial authored no look-at targets, so passive look-at metadata must be absent.");
            Assert.IsNull(hub.GetComponent<GazeSolver>(),
                "expression labels and passive look-at metadata do not create gaze behavior.");
        }
    }
}
