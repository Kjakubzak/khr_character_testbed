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
    }
}
