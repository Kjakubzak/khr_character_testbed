using System.Collections;
using System.Collections.Generic;
using GLTF.Schema;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityGLTF.KhrCharacter;
using Samples.Shared;

namespace KhrCharacterTestbed.Tests
{
    /// <summary>
    /// Wire-neutrality proofs. The public-clean invariant is enforced as a POSITIVE ^KHR_ allow-list rather than a
    /// "VRM" substring blocklist, applied to every exported SC-* root. Both reference real plugin types, so they
    /// also act as anti-hollow gates.
    /// </summary>
    public class SandboxNeutralityTests
    {
        private static readonly string[] Fixtures = { "SC-Face.glb", "SC-FacePlus.glb", "SC-Body.glb" };

        private readonly List<Object> _created = new List<Object>();

        [TearDown]
        public void TearDown()
        {
            foreach (var o in _created)
                if (o != null) Object.DestroyImmediate(o);
            _created.Clear();
        }

        // Pure (no Unity import): pins the allow-list semantics. It proves the check accepts ^KHR_ tokens and
        // rejects EVERY vendor namespace, including the non-VRM ones the old "VRM" substring silently passed.
        [Test]
        public void AllowList_AcceptsKhr_RejectsEveryVendorNamespace()
        {
            foreach (var neutral in new[]
            {
                "KHR_character", "KHR_character_expression_morphtarget", "KHR_character_expression_joint",
                "KHR_character_skeleton_mapping", "KHR_character_reference_pose",
                "KHR_node_camera_hint", "KHR_node_lookat_target",
                "KHR_materials_unlit", "KHR_texture_transform", "KHR_animation_pointer",
            })
                Assert.IsTrue(SandboxTestUtil.IsNeutralExtension(neutral),
                    $"'{neutral}' should be Khronos-neutral (^KHR_ allow-list).");

            // The old substring caught only "VRM". These non-VRM vendor namespaces are exactly what it MISSED;
            // the positive allow-list must reject every one of them.
            foreach (var vendor in new[]
            {
                "VRM", "VRMC_vrm", "VRMC_springBone",
                "FB_geometry_metadata", "MSFT_lod", "ADOBE_materials_thin_transparency",
                "AGI_articulations", "GODOT_single_root", "CESIUM_primitive_outline", "EXT_mesh_gpu_instancing",
            })
                Assert.IsFalse(SandboxTestUtil.IsNeutralExtension(vendor),
                    $"'{vendor}' must be flagged non-neutral by the ^KHR_ allow-list.");

            // Defensive edge cases: null/empty are not neutral, and the prefix match is case-sensitive.
            Assert.IsFalse(SandboxTestUtil.IsNeutralExtension(null), "null is not a neutral extension.");
            Assert.IsFalse(SandboxTestUtil.IsNeutralExtension(""), "empty is not a neutral extension.");
            Assert.IsFalse(SandboxTestUtil.IsNeutralExtension("khr_lowercase"),
                "neutrality is case-sensitive: 'khr_' is not the Khronos prefix 'KHR_'.");
        }

        // Full-disk: import each committed SC-* fixture, re-export it through the KHR_character export plugin, and
        // assert every declared extension on the exported wire is on the ^KHR_ allow-list — on BOTH surfaces.
        [UnityTest]
        public IEnumerator SCExports_AreKhrAllowListNeutral()
        {
            foreach (var fixture in Fixtures)
            {
                var load = SandboxTestUtil.LoadSynthetic(fixture, _created);
                yield return load;
                var scene = load.Current;

                var hub = scene.GetComponent<KhrCharacter>();
                Assert.IsNotNull(hub, $"{fixture} should import a KhrCharacter hub.");

                byte[] glb = CharacterLoader.ExportToGlb(hub.gameObject, out var root);
                Assert.IsNotNull(glb);
                Assert.Greater(glb.Length, 0, $"{fixture} should re-export a non-empty GLB.");
                Assert.IsNotNull(root, $"{fixture} exporter should expose its GLTFRoot.");

                // Anti-hollow: a real KHR character must actually declare KHR_character, so neutrality isn't passing
                // trivially on an empty wire.
                Assert.IsNotNull(root.ExtensionsUsed, $"{fixture} should declare extensionsUsed.");
                CollectionAssert.Contains(root.ExtensionsUsed, KHR_character.EXTENSION_NAME,
                    $"{fixture} re-export should declare the root KHR_character extension (export plugin ran).");

                // Neutral iff EVERY token on each surface is ^KHR_ (or the short core allow-list).
                SandboxTestUtil.AssertExtensionsNeutral(root.ExtensionsUsed, $"{fixture} extensionsUsed");
                SandboxTestUtil.AssertExtensionsNeutral(root.ExtensionsRequired, $"{fixture} extensionsRequired");

                // B1 completeness: every nested KHR_character_expression_* sub-extension actually present on an
                // expression item must ALSO be declared in extensionsUsed (glTF requires every used extension be
                // declared). The pinned plugin (>= 2c5c4f30) fixes this; before that it was a known conformance gap.
                AssertNestedExpressionUsageComplete(root, fixture);
            }
        }

        // Always-on neutralization gate: a synthetic VRM-origin asset (SC-PseudoVRM carries injected VRMC_* vendor
        // tokens) must re-export through the KHR plugin as a fully Khronos-neutral wire (every VRMC_* dropped).
        // Unlike the hero-dependent path, this fixture is always committed, so the gate never degrades to a skip.
        [UnityTest]
        public IEnumerator PseudoVrm_VendorSource_ReExportsKhrNeutral()
        {
            string path = CharacterLoader.SyntheticPath("SC-PseudoVRM.glb");
            Assert.IsTrue(System.IO.File.Exists(path),
                $"SC-PseudoVRM.glb not found at '{path}'. Run Generate Sample Characters first.");

            // The SOURCE is intentionally non-neutral: it carries VRMC_* vendor tokens.
            var sourceUsed = CharacterLoader.ReadSourceExtensionsUsed(path);
            CollectionAssert.Contains(sourceUsed, "VRMC_vrm",
                "The pseudo-VRM source must carry the VRMC_vrm vendor token (else the gate proves nothing).");
            Assert.IsTrue(sourceUsed.Exists(SandboxTestUtil.IsVendorExtension),
                "The pseudo-VRM source must carry at least one non-neutral vendor token.");

            // Import (VRMC_* ignored as unknown) and re-export through the KHR plugin.
            var load = SandboxTestUtil.LoadSynthetic("SC-PseudoVRM.glb", _created);
            yield return load;
            var hub = load.Current.GetComponent<KhrCharacter>();
            Assert.IsNotNull(hub, "The pseudo-VRM should import as a KhrCharacter (VRMC_* ignored).");

            byte[] glb = CharacterLoader.ExportToGlb(hub.gameObject, out var root);
            Assert.IsNotNull(glb);
            Assert.IsNotNull(root.ExtensionsUsed, "the re-export should declare extensionsUsed.");

            // The re-export must be Khronos-neutral on both surfaces: every VRMC_* (and any vendor token) is gone.
            SandboxTestUtil.AssertExtensionsNeutral(root.ExtensionsUsed, "pseudo-VRM re-export extensionsUsed");
            SandboxTestUtil.AssertExtensionsNeutral(root.ExtensionsRequired, "pseudo-VRM re-export extensionsRequired");
            foreach (var token in root.ExtensionsUsed)
                Assert.IsFalse(token.StartsWith("VRMC_", System.StringComparison.Ordinal),
                    $"the KHR re-export must not carry the vendor token '{token}'.");
        }

        // Walks the exported KHR_character_expression items and asserts each nested sub-extension that is actually
        // present is declared in extensionsUsed (and never in extensionsRequired): the full-disk consumer proof of
        // bug B1's fix. Fixture-agnostic: only the sub-extensions a fixture actually uses are required.
        private static void AssertNestedExpressionUsageComplete(GLTFRoot root, string fixture)
        {
            if (root.Extensions == null || !root.Extensions.ContainsKey(KHR_character_expression.EXTENSION_NAME))
                return;
            var expr = root.Extensions[KHR_character_expression.EXTENSION_NAME] as KHR_character_expression;
            if (expr?.Expressions == null) return;

            bool anyMorph = false, anyJoint = false, anyTexture = false, anyMask = false;
            foreach (var item in expr.Expressions)
            {
                if (item == null) continue;
                anyMorph |= item.Morphtarget != null;
                anyJoint |= item.Joint != null;
                anyTexture |= item.Texture != null;
                anyMask |= item.Mask != null;
            }

            void Require(bool present, string token)
            {
                if (!present) return;
                CollectionAssert.Contains(root.ExtensionsUsed, token,
                    $"{fixture}: '{token}' is used on an expression item but missing from extensionsUsed (B1).");
                Assert.IsTrue(root.ExtensionsRequired == null || !root.ExtensionsRequired.Contains(token),
                    $"{fixture}: '{token}' must NOT be in extensionsRequired (non-required, like the parent).");
            }
            Require(anyMorph, KHR_character_expression_morphtarget.EXTENSION_NAME);
            Require(anyJoint, KHR_character_expression_joint.EXTENSION_NAME);
            Require(anyTexture, KHR_character_expression_texture.EXTENSION_NAME);
            Require(anyMask, KHR_character_expression_mask.EXTENSION_NAME);
        }
    }
}
