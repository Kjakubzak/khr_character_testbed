using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using NUnit.Framework;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.TestTools;
using UnityGLTF.KhrCharacter;
using UnityGLTF.VisibilityHints;
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
    /// Two checks per corpus entry: (1) it imports to a scene root with its visibility hints (and, for the hero
    /// variants, the <see cref="KhrCharacter"/> hub — the base hero is a KHR_character); (2) the source wire declares
    /// the expected visibility-hint extension(s) with exactly the expected role tokens. The wire check re-reads the
    /// .glb JSON directly (authoritative "what did we author?") so it holds regardless of which import plugins the
    /// test context has enabled.
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
            public string Path;              // absolute path
            public bool ExpectKhrHub;        // KHR_character marker reconstructs on import (hero variants only)
            public string[] NodeRoles;       // exact KHR_node_visibility_hint roles on the wire, or empty
            public string[] PrimitiveRoles;  // exact KHR_mesh_primitive_visibility_hint roles on the wire, or empty
        }

        private static readonly string[] None = new string[0];

        // Resolve a corpus key (a bare .glb file name) to its path + expectations. Keys are unique file names, so a
        // simple switch keeps the mapping in one place and the [ValueSource] parameter a plain (playmode-serializable)
        // string rather than a struct.
        private static Expectation Describe(string key)
        {
            switch (key)
            {
                case "VH-Node.glb":
                    return new Expectation { Path = CharacterLoader.SyntheticPath(key), NodeRoles = new[] { "third_person" }, PrimitiveRoles = None };
                case "VH-Primitive.glb":
                    return new Expectation { Path = CharacterLoader.SyntheticPath(key), NodeRoles = None, PrimitiveRoles = new[] { "first_person" } };
                case "VH-ViewContext.glb":
                    return new Expectation { Path = CharacterLoader.SyntheticPath(key), NodeRoles = new[] { "third_person" }, PrimitiveRoles = new[] { "first_person" } };
                case "khr-character-example-always.glb":
                    return new Expectation { Path = HeroVariantPath(key), ExpectKhrHub = true, NodeRoles = new[] { "always" }, PrimitiveRoles = None };
                case "khr-character-example-first-person.glb":
                    return new Expectation { Path = HeroVariantPath(key), ExpectKhrHub = true, NodeRoles = new[] { "first_person" }, PrimitiveRoles = None };
                case "khr-character-example-third-person.glb":
                    return new Expectation { Path = HeroVariantPath(key), ExpectKhrHub = true, NodeRoles = new[] { "always" }, PrimitiveRoles = new[] { "third_person" } };
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
            // D7: a hero variant pending re-export logs the importer's rejection of its expression data (all or
            // nothing), while everything else still imports. Expect that error instead of skipping.
            bool pendingReexport = fx.ExpectKhrHub && CharacterLoader.NeedsReexport(fx.Path);
            if (pendingReexport)
                LogAssert.Expect(LogType.Error, new Regex(@"^\[KHR_character\] Expression response data is invalid: "));

            var load = SandboxTestUtil.LoadFromAbsolutePath(fx.Path, _created);
            yield return load;
            Assert.IsNotNull(load.Current, $"{key} failed to import.");
            if (fx.ExpectKhrHub)
            {
                var hub = load.Current.GetComponent<KhrCharacter>();
                Assert.IsNotNull(hub, $"{key} imported without a KhrCharacter hub — the base hero's " +
                                      "KHR_character marker didn't reconstruct on import.");
                Assert.IsNotNull(hub.Skeleton, $"{key} should still import its skeleton mapping.");
                Assert.IsNotNull(hub.CameraHints, $"{key} should still import its camera hints.");
                if (pendingReexport)
                {
                    Assert.IsNull(hub.ExpressionResponses, $"{key}: invalid expression data binds no response set.");
                    Assert.IsNull(hub.Expressions, $"{key}: invalid expression data creates no controller.");
                }
            }

            var nodeHints = load.Current.GetComponent<NodeVisibilityHintSet>();
            CollectionAssert.AreEquivalent(fx.NodeRoles, ImportedRoles(nodeHints?.Entries, e => e.Role),
                $"{key}: imported node visibility roles.");
            var primitiveHints = load.Current.GetComponent<PrimitiveVisibilityHintSet>();
            CollectionAssert.AreEquivalent(fx.PrimitiveRoles, ImportedRoles(primitiveHints?.Entries, e => e.Role),
                $"{key}: imported primitive visibility roles.");
        }

        [Test]
        public void Fixture_DeclaresExpectedVisibilityHintsOnWire([ValueSource(nameof(Corpus))] string key)
        {
            var fx = Describe(key);
            Assert.IsTrue(File.Exists(fx.Path), $"{key}: missing at '{fx.Path}'.");
            var json = CharacterLoader.ExtractGltfJson(File.ReadAllBytes(fx.Path));
            Assert.IsNotNull(json, $"{key} has no JSON chunk (corrupt?).");
            var root = JObject.Parse(json);

            // Roles compare exactly: a differently spelled or cased token (for example "third_person_only" or
            // "First_Person") is a custom role that a consumer hides by default, not a standard one.
            var nodeRoles = new List<string>();
            foreach (var node in (root["nodes"] as JArray) ?? new JArray())
                if (node?["extensions"]?["KHR_node_visibility_hint"]?["role"] is JValue role) nodeRoles.Add((string)role);
            var primitiveRoles = new List<string>();
            foreach (var mesh in (root["meshes"] as JArray) ?? new JArray())
                foreach (var primitive in (mesh?["primitives"] as JArray) ?? new JArray())
                    if (primitive?["extensions"]?["KHR_mesh_primitive_visibility_hint"]?["role"] is JValue role)
                        primitiveRoles.Add((string)role);

            CollectionAssert.AreEquivalent(fx.NodeRoles, Distinct(nodeRoles), $"{key}: KHR_node_visibility_hint roles.");
            CollectionAssert.AreEquivalent(fx.PrimitiveRoles, Distinct(primitiveRoles),
                $"{key}: KHR_mesh_primitive_visibility_hint roles.");
            var used = root["extensionsUsed"]?.ToObject<string[]>() ?? new string[0];
            if (fx.NodeRoles.Length > 0) CollectionAssert.Contains(used, "KHR_node_visibility_hint");
            if (fx.PrimitiveRoles.Length > 0) CollectionAssert.Contains(used, "KHR_mesh_primitive_visibility_hint");
        }

        private static List<string> ImportedRoles<T>(IReadOnlyList<T> entries, System.Func<T, string> role)
        {
            var roles = new List<string>();
            if (entries != null)
                foreach (var entry in entries)
                    if (entry != null) roles.Add(role(entry));
            return Distinct(roles);
        }

        private static List<string> Distinct(List<string> values)
        {
            var distinct = new List<string>();
            foreach (var value in values)
                if (!distinct.Contains(value)) distinct.Add(value);
            return distinct;
        }
    }
}
