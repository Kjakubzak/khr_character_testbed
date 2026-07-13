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
    /// Import-side coverage for the FromBlender/ fixture matrix — the ten canonical KHR-Character `.glb`
    /// variations exported by the sibling <c>khr_character_blender</c> addon's
    /// <c>tests/fixtures/regenerate.py</c>. Each fixture isolates a specific extension combination, so this
    /// suite asserts UnityGLTF's KHR Character import path correctly surfaces each combination as a
    /// <see cref="KhrCharacter"/> hub on the loaded scene root — a fresh regression net for wire drift
    /// between the Blender-side exporter and UnityGLTF's importer.
    ///
    /// Fixtures are all-primitive-geometry (procedural UV-sphere head + cylinder torso, no third-party
    /// content) so this suite has no license or asset dependency beyond the checked-in `.glb`s.
    ///
    /// Complements <see cref="SandboxSmokeTests"/> (Unity → glb → import round-trip) with the Blender →
    /// glb → import direction.
    /// </summary>
    public class SandboxFromBlenderTests
    {
        private readonly List<Object> _created = new List<Object>();

        [TearDown]
        public void TearDown()
        {
            foreach (var o in _created)
                if (o != null) Object.DestroyImmediate(o);
            _created.Clear();
        }

        // ── Every fixture with KHR_character attaches the KhrCharacter hub ──
        //
        // node_hints.glb carries KHR_character on the root even without any expressions or skeleton, so
        // it's included. Only minimal.glb is a bare marker (no children), and node_hints.glb has only
        // Empties for the hint / lookat targets — but both still surface a KhrCharacter component.

        [UnityTest]
        public IEnumerator Load_AllFixtures_AttachKhrCharacterHub(
            [Values(
                "minimal.glb",
                "skeleton.glb",
                "skeleton_refpose.glb",
                "expressions_morph.glb",
                "expressions_joint.glb",
                "expressions_mask.glb",
                "expressions_mapping.glb",
                "node_hints.glb",
                "full.glb",
                "starter.glb")] string fixture)
        {
            var load = SandboxTestUtil.LoadFromBlender(fixture, _created);
            yield return load;
            Assert.IsNotNull(load.Current, $"{fixture} failed to import.");
            var hub = load.Current.GetComponent<KhrCharacter>();
            Assert.IsNotNull(hub, $"{fixture} imported without a KhrCharacter component — the addon's " +
                                  "KHR_character root marker didn't reconstruct on UnityGLTF's import path.");
        }

        // ── Expression fixtures reconstruct the expected expression count ──

        [UnityTest]
        public IEnumerator Load_ExpressionFixtures_HaveExpectedExpressionCount(
            [Values(
                "expressions_morph.glb",   // smile + frown = 2
                "expressions_joint.glb",   // nod = 1
                "expressions_mask.glb",    // smile + frown = 2
                "expressions_mapping.glb"  // smile = 1
                )] string fixture)
        {
            var load = SandboxTestUtil.LoadFromBlender(fixture, _created);
            yield return load;
            var hub = load.Current.GetComponent<KhrCharacter>();
            Assert.IsNotNull(hub, $"{fixture} missing KhrCharacter hub.");

            int expected = fixture switch
            {
                "expressions_morph.glb" => 2,
                "expressions_joint.glb" => 1,
                "expressions_mask.glb" => 2,
                "expressions_mapping.glb" => 1,
                _ => -1
            };

            var report = hub.GetHealth();
            Assert.IsNotNull(report, $"{fixture} GetHealth() returned null.");
            Assert.AreEqual(expected, report.ExpressionCount,
                $"{fixture} expected {expected} expressions, got {report.ExpressionCount}.");
        }

        // ── full.glb is the maximum-surface fixture; assert every KHR extension surfaces somewhere ──
        //
        // The extension list is authored by the sibling khr_character_blender addon's build_full() — see
        // tests/fixtures/regenerate.py. If a new KHR Character extension is added to the addon's output,
        // extend both build_full() there AND this assertion here.

        [UnityTest]
        public IEnumerator Full_DeclaresExpectedExtensionsUsed()
        {
            var load = SandboxTestUtil.LoadFromBlender("full.glb", _created);
            yield return load;
            var hub = load.Current.GetComponent<KhrCharacter>();
            Assert.IsNotNull(hub, "full.glb missing KhrCharacter hub.");

            // We assert the WIRE — re-read extensionsUsed off the source .glb JSON — because the import
            // path can collapse capabilities into Active/Degraded/Inert without preserving one-to-one
            // extension presence. The wire is the authoritative "what did the exporter write?" answer.
            var glbBytes = System.IO.File.ReadAllBytes(CharacterLoader.FromBlenderPath("full.glb"));
            var json = CharacterLoader.ExtractGltfJson(glbBytes);
            Assert.IsNotNull(json, "full.glb has no JSON chunk (corrupt?).");

            var expected = new[]
            {
                "KHR_character",
                "KHR_character_expression",
                "KHR_character_expression_morphtarget",
                "KHR_character_expression_joint",
                "KHR_character_expression_mask",
                "KHR_character_expression_mapping",
                "KHR_character_reference_pose",
                "KHR_character_skeleton_mapping",
                "KHR_node_camera_hint",
                "KHR_node_lookat_target",
            };
            foreach (var ext in expected)
                Assert.IsTrue(json.Contains($"\"{ext}\""),
                    $"full.glb missing expected extension token '{ext}' in the source JSON. " +
                    "If the addon's build_full() dropped this extension, update the assertion.");
        }

        // ── Minimal fixture is exactly what its name says — nothing but KHR_character ──

        [UnityTest]
        public IEnumerator Minimal_HasZeroExpressionsAndNoSkeleton()
        {
            var load = SandboxTestUtil.LoadFromBlender("minimal.glb", _created);
            yield return load;
            var hub = load.Current.GetComponent<KhrCharacter>();
            Assert.IsNotNull(hub, "minimal.glb missing KhrCharacter hub.");

            var report = hub.GetHealth();
            Assert.IsNotNull(report, "minimal GetHealth() returned null.");
            Assert.AreEqual(0, report.ExpressionCount,
                "minimal.glb should have 0 expressions — it's the empty-marker fixture.");
        }
    }
}
