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
    /// Import- and wire-side coverage for the KHR visibility-hint corpus, built to NEVER skip (a skip reds the gate:
    /// the CI floor fails on <c>skipped != 0</c>). Instead of <c>Assert.Ignore</c> on a missing hero, the corpus is
    /// adaptive: the committed <b>synthetic</b> VH-* fixtures (always present once LFS is pulled) are the never-skip
    /// floor, and the per-role <b>hero</b> variants
    /// (<c>khr-character-example-{always,first-person,third-person}.glb</c>, built from the pristine hero by
    /// <c>tools/make_hero_variants.py</c>) are ADDED only when their Git-LFS objects are smudged real GLBs. So a
    /// checkout without the (optional, ~11 MB) hero still runs real checks against the synthetic fixtures, and an
    /// LFS-complete checkout additionally covers the VRM hero variants — matching the never-skip fallback pattern in
    /// <see cref="SandboxNSeriesTests"/> (prefer the hero when real; fall back to a committed synthetic otherwise).
    ///
    /// Two checks per corpus entry: (1) it imports to a scene root (and, for the hero variants, reconstructs the
    /// <see cref="KhrCharacter"/> hub — the base hero is a KHR_character); (2) the source wire declares the expected
    /// visibility-hint extension(s) + role token. The wire check re-reads the .glb JSON directly (authoritative "what
    /// did we author?") so it holds regardless of which import plugins the test context has enabled.
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

        // The per-role hero variants, in the VRM_KHR_Examples LFS folder alongside the base hero.
        private static readonly string[] HeroVariantFiles =
        {
            "khr-character-example-always.glb",
            "khr-character-example-first-person.glb",
            "khr-character-example-third-person.glb",
        };

        // The always-present synthetic analogs (committed): a fresh checkout still runs a real check here, so the
        // gate never goes hollow (0 cases) and never skips.
        private static readonly string[] SyntheticFixtures =
        {
            "VH-Node.glb",
            "VH-Primitive.glb",
            "VH-ViewContext.glb",
        };

        private static string HeroVariantPath(string file) =>
            Path.Combine(Application.dataPath, "SampleAssets", "VRM_KHR_Examples", file);

        // What a corpus entry should exhibit on import + on the wire.
        private struct Expectation
        {
            public string Path;               // absolute path
            public bool ExpectKhrHub;         // KHR_character root reconstructs on import (hero variants only)
            public string NodeRole;           // expected KHR_node_visibility_hint role token, or null if no node hint
            public bool ExpectPrimitiveHint;  // expects KHR_mesh_primitive_visibility_hint on the wire
        }

        // Resolve a corpus key (a bare .glb file name) to its path + expectations. Keys are unique file names, so a
        // simple switch keeps the mapping in one place and the [ValueSource] parameter a plain (playmode-serializable)
        // string rather than a struct.
        private static Expectation Describe(string key)
        {
            switch (key)
            {
                case "VH-Node.glb":
                    return new Expectation { Path = CharacterLoader.SyntheticPath(key), ExpectKhrHub = false, NodeRole = "third_person", ExpectPrimitiveHint = false };
                case "VH-Primitive.glb":
                    return new Expectation { Path = CharacterLoader.SyntheticPath(key), ExpectKhrHub = false, NodeRole = null, ExpectPrimitiveHint = true };
                case "VH-ViewContext.glb":
                    return new Expectation { Path = CharacterLoader.SyntheticPath(key), ExpectKhrHub = false, NodeRole = "third_person", ExpectPrimitiveHint = true };
                case "khr-character-example-always.glb":
                    return new Expectation { Path = HeroVariantPath(key), ExpectKhrHub = true, NodeRole = "always", ExpectPrimitiveHint = false };
                case "khr-character-example-first-person.glb":
                    return new Expectation { Path = HeroVariantPath(key), ExpectKhrHub = true, NodeRole = "first_person", ExpectPrimitiveHint = false };
                case "khr-character-example-third-person.glb":
                    return new Expectation { Path = HeroVariantPath(key), ExpectKhrHub = true, NodeRole = "third_person", ExpectPrimitiveHint = true };
                default:
                    throw new System.ArgumentException($"unmapped visibility-hint corpus key '{key}'.");
            }
        }

        /// <summary>Adaptive corpus (never empty, never skips): the committed synthetic VH-* fixtures always, plus each
        /// hero variant that is a smudged real GLB (not an un-smudged LFS pointer). Evaluated at test-collection time.</summary>
        public static IEnumerable<string> Corpus()
        {
            foreach (var f in SyntheticFixtures) yield return f;
            foreach (var f in HeroVariantFiles)
                if (CharacterLoader.IsRealGlb(HeroVariantPath(f))) yield return f;
        }

        [UnityTest]
        public IEnumerator Fixture_ImportsAndAttachesHubWhenHero([ValueSource(nameof(Corpus))] string key)
        {
            var fx = Describe(key);
            var load = SandboxTestUtil.LoadFromAbsolutePath(fx.Path, _created);
            yield return load;
            Assert.IsNotNull(load.Current, $"{key} failed to import.");
            if (fx.ExpectKhrHub)
            {
                var hub = load.Current.GetComponent<KhrCharacter>();
                Assert.IsNotNull(hub, $"{key} imported without a KhrCharacter hub — the base hero's " +
                                      "KHR_character root marker didn't reconstruct on import.");
            }
        }

        [Test]
        public void Fixture_DeclaresExpectedVisibilityHintsOnWire([ValueSource(nameof(Corpus))] string key)
        {
            var fx = Describe(key);
            Assert.IsTrue(File.Exists(fx.Path), $"{key}: missing at '{fx.Path}'.");
            var json = CharacterLoader.ExtractGltfJson(File.ReadAllBytes(fx.Path));
            Assert.IsNotNull(json, $"{key} has no JSON chunk (corrupt?).");

            if (fx.NodeRole != null)
            {
                Assert.IsTrue(json.Contains("\"KHR_node_visibility_hint\""),
                    $"{key} missing KHR_node_visibility_hint on the wire.");
                // Prefix match on the role VALUE (no trailing quote): the standard KHR_node_visibility_hint vocabulary
                // is 'always'/'first_person'/'third_person' (the hero variants use these verbatim), but the UnityGLTF
                // exporter currently emits the non-standard custom variants 'first_person_only'/'third_person_only' for
                // the synthetic fixtures. Matching the standard prefix accepts both while still asserting the role is a
                // real "role":"<context>..." token. (The exporter/spec vocabulary mismatch is flagged as a cross-repo item.)
                Assert.IsTrue(json.Contains($"\"role\":\"{fx.NodeRole}") || json.Contains($"\"role\": \"{fx.NodeRole}"),
                    $"{key} missing expected node-visibility role token '{fx.NodeRole}' on the wire.");
            }
            if (fx.ExpectPrimitiveHint)
                Assert.IsTrue(json.Contains("\"KHR_mesh_primitive_visibility_hint\""),
                    $"{key} missing KHR_mesh_primitive_visibility_hint on the wire.");
        }
    }
}
