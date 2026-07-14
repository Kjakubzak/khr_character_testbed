using System.Collections;
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityGLTF.KhrCharacter;
using Samples.Shared;

namespace KhrCharacterTestbed.Tests
{
    /// <summary>
    /// Import-side coverage for the per-role hero visibility-hint variants under
    /// <c>Assets/SampleAssets/</c> — <c>khr-character-example-{always,first-person,third-person}.glb</c>,
    /// built from the pristine hero by <c>tools/make_hero_variants.py</c>. Each variant isolates a single
    /// <c>KHR_node_visibility_hint</c> role; the third_person one additionally carries a
    /// <c>KHR_mesh_primitive_visibility_hint</c> example.
    ///
    /// Two checks per variant: (1) it imports and reconstructs the <see cref="KhrCharacter"/> hub (the base
    /// hero is a KHR_character), and (2) the source wire declares the extension + the expected role token.
    /// The wire check re-reads the .glb JSON directly (authoritative "what did we author?") rather than
    /// depending on the VisibilityHints runtime components, so it holds regardless of which import plugins
    /// the test context has enabled. Un-smudged Git-LFS pointers are skipped, mirroring
    /// <see cref="CharacterLoader.HeroIsRealGlb"/>.
    /// </summary>
    public class SandboxHeroVariantsTests
    {
        private readonly List<Object> _created = new List<Object>();

        [TearDown]
        public void TearDown()
        {
            foreach (var o in _created)
                if (o != null) Object.DestroyImmediate(o);
            _created.Clear();
        }

        private static readonly string[] Variants =
        {
            "khr-character-example-always.glb",
            "khr-character-example-first-person.glb",
            "khr-character-example-third-person.glb",
        };

        private static string VariantPath(string file) =>
            Path.Combine(Application.dataPath, "SampleAssets", "VRM_KHR_Examples", file);

        // First four bytes spell "glTF" (0x67 0x6C 0x54 0x46). An un-smudged LFS pointer is ASCII text and fails this.
        private static bool IsRealGlb(byte[] bytes) =>
            bytes.Length >= 4 && bytes[0] == 0x67 && bytes[1] == 0x6C && bytes[2] == 0x54 && bytes[3] == 0x46;

        [UnityTest]
        public IEnumerator Variant_ImportsAndAttachesKhrCharacterHub(
            [ValueSource(nameof(Variants))] string file)
        {
            string path = VariantPath(file);
            if (!File.Exists(path)) { Assert.Ignore($"{file} not present."); yield break; }
            if (!IsRealGlb(File.ReadAllBytes(path))) { Assert.Ignore($"{file} is an un-smudged LFS pointer."); yield break; }

            var load = SandboxTestUtil.LoadFromAbsolutePath(path, _created);
            yield return load;
            Assert.IsNotNull(load.Current, $"{file} failed to import.");
            var hub = load.Current.GetComponent<KhrCharacter>();
            Assert.IsNotNull(hub, $"{file} imported without a KhrCharacter hub — the base hero's " +
                                  "KHR_character root marker didn't reconstruct on import.");
        }

        [Test]
        public void Variant_DeclaresExpectedVisibilityRoleOnWire(
            [ValueSource(nameof(Variants))] string file)
        {
            string path = VariantPath(file);
            if (!File.Exists(path)) Assert.Ignore($"{file} not present.");
            var bytes = File.ReadAllBytes(path);
            if (!IsRealGlb(bytes)) Assert.Ignore($"{file} is an un-smudged LFS pointer.");

            var json = CharacterLoader.ExtractGltfJson(bytes);
            Assert.IsNotNull(json, $"{file} has no JSON chunk (corrupt?).");
            Assert.IsTrue(json.Contains("\"KHR_node_visibility_hint\""),
                $"{file} missing KHR_node_visibility_hint on the wire.");

            string expectedRole = file switch
            {
                "khr-character-example-always.glb" => "always",
                "khr-character-example-first-person.glb" => "first_person",
                "khr-character-example-third-person.glb" => "third_person",
                _ => null
            };
            Assert.IsNotNull(expectedRole, $"unmapped variant {file}.");
            Assert.IsTrue(json.Contains($"\"role\":\"{expectedRole}\"") || json.Contains($"\"role\": \"{expectedRole}\""),
                $"{file} missing expected role token '{expectedRole}' on the wire.");

            if (file == "khr-character-example-third-person.glb")
                Assert.IsTrue(json.Contains("\"KHR_mesh_primitive_visibility_hint\""),
                    "third-person variant should also carry a KHR_mesh_primitive_visibility_hint example.");
        }
    }
}
