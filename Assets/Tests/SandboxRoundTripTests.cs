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
    /// Structural round-trip (04 neutral lens, Phase 2-A): for each committed fixture, import (A), re-export through
    /// the KHR plugin, re-import the bytes (B), and assert B preserves A's CAPABILITY SET and per-capability COUNTS
    /// (expressions, skeleton bones, look-at targets, camera hints). Upgrades the count-only M5 to per-capability
    /// structural survival: a capability/driver that silently stops round-tripping turns this red. State-only (no
    /// pixels), so it is render-pipeline-agnostic. References real plugin types, so it also acts as an anti-hollow gate.
    /// </summary>
    public class SandboxRoundTripTests
    {
        private readonly List<Object> _created = new List<Object>();

        [TearDown]
        public void TearDown()
        {
            foreach (var o in _created)
                if (o != null) Object.DestroyImmediate(o);
            _created.Clear();
        }

        private struct Shape
        {
            public HashSet<CharacterCapability> Caps;
            public int Expr, Bones, Look, Cam;
        }

        private static Shape Capture(KhrCharacter hub)
        {
            return new Shape
            {
                Caps = hub.Capabilities != null
                    ? new HashSet<CharacterCapability>(hub.Capabilities)
                    : new HashSet<CharacterCapability>(),
                Expr = hub.Expressions != null ? hub.Expressions.Count : 0,
                Bones = hub.Skeleton != null && hub.Skeleton.Result != null && hub.Skeleton.Result.Bones != null
                    ? hub.Skeleton.Result.Bones.Count : 0,
                Look = hub.Gaze != null ? hub.Gaze.AuthoredTargets.Count : 0,
                Cam = hub.CameraHints != null && hub.CameraHints.Hints != null ? hub.CameraHints.Hints.Count : 0,
            };
        }

        // SC-PseudoVRM is intentionally excluded (its vendor tokens are covered by the neutralization gate; its KHR
        // capability subset is the same as SC-Partial, so it adds no structural coverage here).
        [UnityTest]
        public IEnumerator RoundTrip_PreservesCapabilitiesAndCounts(
            [Values("SC-Face.glb", "SC-FacePlus.glb", "SC-Body.glb", "SC-LookAt.glb", "SC-Partial.glb")] string fixture)
        {
            // A: import the committed fixture.
            var loadA = SandboxTestUtil.LoadSynthetic(fixture, _created);
            yield return loadA;
            var hubA = loadA.Current.GetComponent<KhrCharacter>();
            Assert.IsNotNull(hubA, $"{fixture} should import a KhrCharacter hub (A).");
            var a = Capture(hubA);
            Assert.Greater(a.Caps.Count, 0, $"{fixture} (A) should report at least one capability.");

            // Export A -> bytes, then re-import as B (self-contained GLB, no external data loader).
            byte[] glb = CharacterLoader.ExportToGlb(hubA.gameObject, out _);
            Assert.IsNotNull(glb);
            Assert.Greater(glb.Length, 0, $"{fixture} should re-export a non-empty GLB.");

            var taskB = CharacterLoader.LoadFromBytesAsync(glb, null);
            yield return SandboxTestUtil.WaitFor(taskB, 30f);
            var sceneB = SandboxTestUtil.ResolveScene(taskB, _created);
            var hubB = sceneB.GetComponent<KhrCharacter>();
            Assert.IsNotNull(hubB, $"{fixture} should re-import a KhrCharacter hub (B).");
            var b = Capture(hubB);

            // Capability set + per-capability counts must survive A -> B with no silent loss.
            string capsA = string.Join(",", a.Caps);
            string capsB = string.Join(",", b.Caps);
            CollectionAssert.AreEquivalent(a.Caps, b.Caps,
                $"{fixture}: the capability set must survive round-trip (A=[{capsA}] B=[{capsB}]).");
            Assert.AreEqual(a.Expr, b.Expr, $"{fixture}: expression count must survive round-trip.");
            Assert.AreEqual(a.Bones, b.Bones, $"{fixture}: skeleton bone count must survive round-trip.");
            Assert.AreEqual(a.Look, b.Look, $"{fixture}: look-at target count must survive round-trip.");
            Assert.AreEqual(a.Cam, b.Cam, $"{fixture}: camera-hint count must survive round-trip.");
        }
    }
}
