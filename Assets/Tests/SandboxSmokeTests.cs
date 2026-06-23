// Smoke test for this community example: exports the KHR_character / avatar glTF extensions through UnityGLTF
// over a tiny in-memory character and checks the result. It enables the KHR_character + AnimationPointer export
// plugins on an isolated GLTFSettings, runs a real GLTFSceneExporter, and asserts the document carries the
// KHR_character root extension while keeping a Khronos-neutral wire (no KHR_* extension marked required).
//
// The test lives in this project (instead of relying on the package's `testables`) so it compiles and runs
// against UnityGLTF consumed as a git dependency; referencing the real schema/runtime types also means it will
// not compile if the package is resolved without them.
//
// Namespace note: in KhrCharacterTestbed.Tests we import UnityGLTF and UnityGLTF.KhrCharacter explicitly. Both
// GLTF.Schema and UnityGLTF.KhrCharacter define a `Sampler` (the glTF texture sampler vs. the character
// animation-driver sampler), so the driver type is aliased as KhrSampler to avoid an ambiguous reference.

using System.Collections.Generic;
using GLTF.Schema;
using NUnit.Framework;
using UnityEngine;
using UnityGLTF;
using UnityGLTF.KhrCharacter;
using UnityGLTF.Plugins;
using KhrSampler = UnityGLTF.KhrCharacter.Sampler;

namespace KhrCharacterTestbed.Tests
{
    public class SandboxSmokeTests
    {
        private readonly List<Object> _created = new List<Object>();

        [TearDown]
        public void TearDown()
        {
            foreach (var o in _created)
                if (o != null) Object.DestroyImmediate(o);
            _created.Clear();
        }

        // Isolated default settings with the KHR_character + AnimationPointer export plugins enabled; runs a real
        // synchronous GLTFSceneExporter and returns the populated glTF root.
        private static GLTFRoot ExportToGltfRoot(GameObject root)
        {
            var settings = GLTFSettings.GetDefaultSettings();
            foreach (var plugin in settings.ExportPlugins)
                if (plugin is KhrCharacterExportPlugin || plugin is AnimationPointerExport)
                    plugin.Enabled = true;

            var exporter = new GLTFSceneExporter(new[] { root.transform }, new ExportContext(settings));
            exporter.SaveGLBToByteArray("scene");   // synchronous; runs the AfterSceneExport plugin hooks
            return exporter.GetRoot();
        }

        // A single-key STEP joint rotation: enough for the expression to emit one channel (expressions with zero
        // channels are dropped on export).
        private static JointDriver RotationDriver(Transform target) => new JointDriver
        {
            Target = target, Channel = TrsChannel.Rotation,
            Sampler = new KhrSampler { Times = new[] { 0f }, Interp = Interp.Step, SingleKey = true },
            DeltaQuat = new[] { Quaternion.Euler(10f, 0f, 0f) }, BaseQuat = Quaternion.identity,
        };

        [Test]
        public void Export_MinimalCharacter_EmitsNeutralKhrCharacterWire()
        {
            // Minimal in-memory character: a KhrCharacter hub (the discoverable character root) plus one joint
            // expression on a child.
            var root = new GameObject("char");
            _created.Add(root);
            root.AddComponent<KhrCharacter>();

            var jaw = new GameObject("jaw").transform;
            jaw.SetParent(root.transform, false);

            var set = new CharacterExpressionSet
            {
                Expressions = new[]
                {
                    new ExpressionTrack
                    {
                        Name = "jawOpen",
                        Domains = ExpressionDomain.Joint,
                        JointDrivers = new[] { RotationDriver(jaw) },
                    },
                },
            };
            root.AddComponent<ExpressionController>().Initialize(set);

            var gltf = ExportToGltfRoot(root);

            // (1) The export plugin actually ran: the root KHR_character extension is present.
            Assert.IsTrue(gltf.Extensions != null && gltf.Extensions.ContainsKey(KHR_character.EXTENSION_NAME),
                "root KHR_character extension should be present (proves the KHR_character export plugin ran)");

            // (2) Wire neutrality: no KHR_* extension may be REQUIRED (Khronos extensions stay used-not-required).
            if (gltf.ExtensionsRequired != null)
                foreach (var required in gltf.ExtensionsRequired)
                    StringAssert.DoesNotStartWith("KHR_", required,
                        $"no KHR_ extension may be required (wire neutrality); found required '{required}'");
        }
    }
}
