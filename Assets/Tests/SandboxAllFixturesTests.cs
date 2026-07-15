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
    /// Universal-import gate. Iterates every fixture across every registered
    /// <see cref="CharacterLoader.AssetSourceCatalog"/> source (Synthetic + FromBlender + any
    /// user-added folder) and asserts each imports without exception. New fixtures show up
    /// automatically — the discovery mechanism is the same catalog the GlbViewer's preset
    /// dropdown reads, so adding an SC-*/VH-*/user-registered `.glb` makes it flow into this
    /// suite with zero test-file edits.
    ///
    /// Complements the deep-per-fixture assertion suites (<see cref="SandboxSchemaConformanceTests"/>,
    /// <see cref="SandboxFromBlenderTests"/>). Those test specific KHR shapes; this test just
    /// asserts nothing catches fire on load — a crash-safe gate over the full corpus.
    /// </summary>
    public class SandboxAllFixturesTests
    {
        private readonly List<Object> _created = new List<Object>();

        [TearDown]
        public void TearDown()
        {
            foreach (var o in _created)
                if (o != null) Object.DestroyImmediate(o);
            _created.Clear();
        }

        // ValueSource pulls the fixture list from the catalog at test-collection time. If the
        // catalog is empty (no fixtures generated yet), NUnit reports "no cases" for this test
        // rather than failing — no fixtures = nothing to gate.
        [UnityTest]
        public IEnumerator Load_EveryCatalogFixture_Succeeds(
            [ValueSource(typeof(SandboxTestUtil), nameof(SandboxTestUtil.AllCatalogFixturePaths))]
            string absolutePath)
        {
            var load = SandboxTestUtil.LoadFromAbsolutePath(absolutePath, _created);
            yield return load;
            Assert.IsNotNull(load.Current,
                $"{System.IO.Path.GetFileName(absolutePath)}: import returned null. Source path: {absolutePath}");
            // Assertion is deliberately soft: "no crash + scene root non-null." Detailed KHR
            // shape assertions live in the per-fixture suites; this gate is about universal
            // survival across the full corpus. A hub component MAY be absent (e.g. a plain
            // .glb dropped into a user-registered folder that has no KHR content).
        }

        // Anti-hollow floor for the [ValueSource] above: an empty catalog yields "no cases" (NOT a failure), so a
        // separate lower-bound assertion is needed to fail LOUDLY when fixture discovery collapses (deleted fixtures
        // or a mis-detected asset-source dir). Counts discovered PATHS (un-smudged LFS pointers included), so it
        // guards discovery — not smudge state — and therefore holds on a no-LFS checkout too. The committed corpus is
        // Synthetic (>=10) + FromBlender (11) = >=21; 15 sits above any single source so losing a whole source trips it.
        [Test]
        public void Catalog_DiscoversFixtures_AboveLowerBound()
        {
            var paths = SandboxTestUtil.AllCatalogFixturePaths();
            Assert.GreaterOrEqual(paths.Length, 15,
                $"Fixture catalog discovered only {paths.Length} .glb/.gltf file(s) — expected the committed " +
                "Synthetic + FromBlender corpus (>=21). A near-empty catalog means fixtures were deleted or an " +
                "asset-source directory wasn't detected, which would silently hollow out the universal-import gate.");
        }
    }
}
