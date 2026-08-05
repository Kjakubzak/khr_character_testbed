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
    /// KHR-only testbed export-profile proofs. This is stricter than glTF namespace policy: EXT_ is also reserved
    /// for Khronos multi-vendor extensions, but these fixtures intentionally emit only KHR_ tokens. The profile is
    /// applied to every relevant SC-* root and also acts as an anti-hollow gate.
    /// </summary>
    public class SandboxNeutralityTests
    {
        private static readonly string[] Fixtures =
        {
            "SC-Face.glb", "SC-FacePlus.glb", "SC-Body.glb", "SC-LookAt.glb",
            "SC-Partial.glb", "SC-Degraded.glb", "SC-ExprEdge.glb",
        };

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
        public void KhrOnlyProfile_AcceptsKhr_AndSeparatelyRejectsExtAndVendorPrefixes()
        {
            foreach (var allowed in new[]
            {
                "KHR_character", "KHR_character_expression_morphtarget", "KHR_character_expression_joint",
                "KHR_character_skeleton_mapping", "KHR_character_reference_pose",
                "KHR_node_camera_hint", "KHR_node_lookat_target",
                "KHR_materials_unlit", "KHR_texture_transform", "KHR_animation_pointer", "KHR_xmp_json_ld",
            })
                Assert.IsTrue(SandboxTestUtil.IsAllowedByKhrOnlyProfile(allowed),
                    $"'{allowed}' should match the testbed's KHR-only profile.");

            Assert.IsFalse(SandboxTestUtil.IsAllowedByKhrOnlyProfile("EXT_mesh_gpu_instancing"),
                "EXT_ is Khronos-reserved multi-vendor, but intentionally outside this stricter KHR-only profile.");

            // The old substring caught only "VRM". These non-VRM vendor namespaces are exactly what it MISSED;
            // the positive allow-list must reject every one of them.
            foreach (var vendor in new[]
            {
                "VRM", "VRMC_vrm", "VRMC_springBone",
                "FB_geometry_metadata", "MSFT_lod", "ADOBE_materials_thin_transparency",
                "AGI_articulations", "GODOT_single_root", "CESIUM_primitive_outline",
            })
                Assert.IsFalse(SandboxTestUtil.IsAllowedByKhrOnlyProfile(vendor),
                    $"'{vendor}' must be rejected by the KHR-only profile.");

            Assert.IsFalse(SandboxTestUtil.IsAllowedByKhrOnlyProfile(null));
            Assert.IsFalse(SandboxTestUtil.IsAllowedByKhrOnlyProfile(""));
            Assert.IsFalse(SandboxTestUtil.IsAllowedByKhrOnlyProfile("khr_lowercase"),
                "the project profile's prefix match is case-sensitive.");
        }

        // Full-disk: assert each committed SC-* fixture matches the project profile on both declaration surfaces.
        [Test]
        public void CommittedSyntheticAssets_MatchKhrOnlyExportProfile()
        {
            foreach (var fixture in Fixtures)
            {
                var root = CharacterLoader.ReadGltfRoot(CharacterLoader.SyntheticPath(fixture));

                // Anti-hollow: a real KHR character must actually declare KHR_character, so the profile isn't passing
                // trivially on an empty wire.
                Assert.IsNotNull(root.ExtensionsUsed, $"{fixture} should declare extensionsUsed.");
                CollectionAssert.Contains(root.ExtensionsUsed, KHR_character.EXTENSION_NAME,
                    $"{fixture} should declare the root KHR_character extension.");
                Assert.IsTrue(root.ExtensionsUsed == null || !root.ExtensionsUsed.Contains(KhrCharacterExtensionNames.XmpJsonLd),
                    $"{fixture} has no XMP data and must not declare an unconditional XMP dependency.");

                SandboxTestUtil.AssertExtensionsMatchKhrOnlyProfile(root.ExtensionsUsed, $"{fixture} extensionsUsed");
                SandboxTestUtil.AssertExtensionsMatchKhrOnlyProfile(root.ExtensionsRequired, $"{fixture} extensionsRequired");
                Assert.IsTrue(root.ExtensionsRequired == null || root.ExtensionsRequired.Count == 0,
                    $"{fixture} deliberately keeps every extension optional for fallback consumers.");

                // B1 completeness: every nested KHR_character_expression_* sub-extension actually present on an
                // expression item must ALSO be declared in extensionsUsed (glTF requires every used extension be
                // declared). The pinned plugin (>= 2c5c4f30) fixes this; before that it was a known conformance gap.
                AssertNestedExpressionUsageComplete(root, fixture);
            }
        }

        // Imported passive expressions contain more wire data than the optional Unity controller. Export must abort
        // instead of using that lossy projection as a format-conversion shortcut.
        [UnityTest]
        public IEnumerator PseudoVrm_ImportedExpressionExportIsRejectedAsLossy()
        {
            string path = CharacterLoader.SyntheticPath("SC-PseudoVRM.glb");
            Assert.IsTrue(System.IO.File.Exists(path),
                $"SC-PseudoVRM.glb not found at '{path}'. Run Generate Sample Characters first.");

            // The SOURCE intentionally carries VRMC_* vendor tokens.
            var sourceUsed = CharacterLoader.ReadSourceExtensionsUsed(path);
            CollectionAssert.Contains(sourceUsed, "VRMC_vrm",
                "The pseudo-VRM source must carry the VRMC_vrm vendor token (else the gate proves nothing).");
            Assert.IsTrue(sourceUsed.Exists(value => value == "VRM" || value.StartsWith("VRMC_")),
                "The pseudo-VRM source must carry at least one explicit VRM vendor token.");

            // Import still succeeds with unknown VRMC_* ignored.
            var load = SandboxTestUtil.LoadSynthetic(
                "SC-PseudoVRM.glb",
                _created,
                CharacterExpressionHostPolicy.Passive);
            yield return load;
            var hub = load.Current.GetComponent<KhrCharacter>();
            Assert.IsNotNull(hub, "The pseudo-VRM should import as a KhrCharacter (VRMC_* ignored).");

            var exception = Assert.Catch<System.InvalidOperationException>(
                () => CharacterLoader.ExportToGlb(hub.gameObject, out _));
            StringAssert.Contains("passive ExpressionResponseSet", exception.Message);
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
                    $"{fixture}: this testbed keeps '{token}' optional for fallback consumers; the specification permits requiring it.");
            }
            Require(anyMorph, KHR_character_expression_morphtarget.EXTENSION_NAME);
            Require(anyJoint, KHR_character_expression_joint.EXTENSION_NAME);
            Require(anyTexture, KHR_character_expression_texture.EXTENSION_NAME);
            Require(anyMask, KHR_character_expression_mask.EXTENSION_NAME);

            CollectionAssert.Contains(root.ExtensionsUsed, KHR_character.EXTENSION_NAME,
                $"{fixture}: expression extensions require KHR_character.");
            CollectionAssert.Contains(root.ExtensionsUsed, KHR_character_expression.EXTENSION_NAME,
                $"{fixture}: nested expression extensions require KHR_character_expression.");
            if (anyTexture)
            {
                CollectionAssert.Contains(root.ExtensionsUsed, "KHR_animation_pointer",
                    $"{fixture}: texture expressions require KHR_animation_pointer.");
                CollectionAssert.Contains(root.ExtensionsUsed, "KHR_texture_transform",
                    $"{fixture}: texture expressions require KHR_texture_transform.");
            }
        }
    }
}
