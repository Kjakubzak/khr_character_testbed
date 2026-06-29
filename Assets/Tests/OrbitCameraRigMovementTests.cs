using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using Samples.Shared;

namespace KhrCharacterTestbed.Tests
{
    /// <summary>
    /// Unit coverage for the input-agnostic movement API the keyboard + camera panel drive on
    /// <see cref="OrbitCameraRig"/>: Pan moves the pivot, Dolly scales the distance (and clamps), OrbitBy clamps
    /// pitch, ApplyPreset sets the named yaw/pitch, and Fov clamps to Min/Max. State-only, so it needs no rendering.
    /// </summary>
    public class OrbitCameraRigMovementTests
    {
        private readonly List<Object> _created = new List<Object>();

        private OrbitCameraRig NewRig()
        {
            var go = new GameObject("RigUnderTest", typeof(Camera), typeof(OrbitCameraRig));
            _created.Add(go);
            return go.GetComponent<OrbitCameraRig>();
        }

        [TearDown]
        public void TearDown()
        {
            foreach (var o in _created)
                if (o != null) Object.DestroyImmediate(o);
            _created.Clear();
        }

        [Test]
        public void Pan_MovesPivot()
        {
            var rig = NewRig();
            rig.Pivot = Vector3.zero;
            rig.Distance = 3f;

            rig.Pan(new Vector2(0.5f, 0f));

            Assert.Greater(rig.Pivot.magnitude, 0f, "Pan should translate the pivot.");
        }

        [Test]
        public void Dolly_ChangesDistance_AndClamps()
        {
            var rig = NewRig();
            rig.Distance = 5f;

            rig.Dolly(1f);
            Assert.Less(rig.Distance, 5f, "a positive dolly step should zoom in (shrink distance).");

            float afterIn = rig.Distance;
            rig.Dolly(-2f);
            Assert.Greater(rig.Distance, afterIn, "a negative dolly step should zoom out (grow distance).");

            rig.Distance = rig.MinDistance;
            rig.Dolly(100f);
            Assert.GreaterOrEqual(rig.Distance, rig.MinDistance, "dolly must clamp to MinDistance.");
        }

        [Test]
        public void OrbitBy_ClampsPitch()
        {
            var rig = NewRig();
            rig.Pitch = 80f;

            rig.OrbitBy(0f, 100f);
            Assert.AreEqual(rig.MaxPitch, rig.Pitch, 1e-3f, "pitch must clamp to MaxPitch.");

            rig.OrbitBy(0f, -1000f);
            Assert.AreEqual(rig.MinPitch, rig.Pitch, 1e-3f, "pitch must clamp to MinPitch.");
        }

        [Test]
        public void ApplyPreset_SetsYawAndPitch()
        {
            var rig = NewRig();

            rig.ApplyPreset(CameraPreset.Side);
            Assert.AreEqual(90f, rig.Yaw, 1e-3f);
            Assert.AreEqual(5f, rig.Pitch, 1e-3f);

            rig.ApplyPreset(CameraPreset.Back);
            Assert.AreEqual(180f, rig.Yaw, 1e-3f);

            rig.ApplyPreset(CameraPreset.Top);
            Assert.AreEqual(0f, rig.Yaw, 1e-3f);
            Assert.AreEqual(80f, rig.Pitch, 1e-3f, "Top preset pitch (80) is within the default clamp (85).");
        }

        [Test]
        public void Fov_ClampsToRange()
        {
            var rig = NewRig();
            rig.MinFov = 20f;
            rig.MaxFov = 90f;

            rig.Fov = 1000f;
            Assert.AreEqual(90f, rig.Fov, 1e-3f, "FOV must clamp to MaxFov.");

            rig.Fov = 1f;
            Assert.AreEqual(20f, rig.Fov, 1e-3f, "FOV must clamp to MinFov.");

            rig.Fov = 45f;
            Assert.AreEqual(45f, rig.Fov, 1e-3f, "an in-range FOV passes through.");
        }
    }
}
