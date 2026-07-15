using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityGLTF.KhrCharacter;

namespace KhrCharacterTestbed.Tests
{
    /// <summary>
    /// Capability-health tri-state coverage. Locks the observable Active vs Degraded distinction the Health demo
    /// relies on: SC-Body maps to a complete humanoid skeleton (SkeletonMapping Active), while SC-Degraded is
    /// authored with a required humanoid bone missing (see <c>SampleCharacterFactory</c>) so its SkeletonMapping
    /// must report Degraded. Without this, the fixture that exists solely to demonstrate degradation was unasserted.
    /// </summary>
    public class SandboxHealthTests
    {
        private readonly List<Object> _created = new List<Object>();

        [TearDown]
        public void TearDown()
        {
            foreach (var o in _created)
                if (o != null) Object.DestroyImmediate(o);
            _created.Clear();
        }

        [UnityTest]
        public IEnumerator SC_Degraded_ReportsSkeletonMappingDegraded()
        {
            var load = SandboxTestUtil.LoadSynthetic("SC-Degraded.glb", _created);
            yield return load;
            Assert.IsNotNull(load.Current, "SC-Degraded failed to import.");
            var hub = load.Current.GetComponent<KhrCharacter>();
            Assert.IsNotNull(hub, "SC-Degraded should import as a KHR Character hub.");

            Assert.AreEqual(CapabilityStatus.Degraded, StatusOf(hub, CharacterCapability.SkeletonMapping),
                "SC-Degraded is authored with a required humanoid bone missing, so SkeletonMapping must report Degraded.");
        }

        [UnityTest]
        public IEnumerator SC_Body_ReportsSkeletonMappingActive()
        {
            var load = SandboxTestUtil.LoadSynthetic("SC-Body.glb", _created);
            yield return load;
            Assert.IsNotNull(load.Current, "SC-Body failed to import.");
            var hub = load.Current.GetComponent<KhrCharacter>();
            Assert.IsNotNull(hub, "SC-Body should import as a KHR Character hub.");

            Assert.AreEqual(CapabilityStatus.Active, StatusOf(hub, CharacterCapability.SkeletonMapping),
                "SC-Body maps to a complete humanoid skeleton, so SkeletonMapping should report Active (the contrast to SC-Degraded).");
        }

        // Return a capability's status from the health report; fail if the capability isn't present to report on.
        private static CapabilityStatus StatusOf(KhrCharacter hub, CharacterCapability capability)
        {
            var report = hub.GetHealth();
            Assert.IsNotNull(report, "GetHealth() returned null.");
            foreach (var cap in report.Capabilities)
                if (cap.Capability == capability) return cap.Status;
            Assert.Fail($"Health report has no {capability} capability to check.");
            return default;
        }
    }
}
