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
    /// Phase-2b functional proofs (bounded PlayMode) upgraded to assert real post-import output:
    /// M2 (morph + joint full-disk golden on SC-FacePlus — exact blendshape weight × frameWeight AND the jaw bone's
    /// rebased-to-rest rotation), M3 (gaze drives a look expression on SC-Face), M4 (rig switch on SC-Body —
    /// REQUIRES the humanoid build and asserts a bone's world position is preserved across the rig switch). All
    /// reference real plugin types, so they also act as anti-hollow gates.
    /// </summary>
    public class SandboxM2M3M4Tests
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
        public IEnumerator M2_MorphAndJoint_DriveExactBlendshapeAndJointRotation()
        {
            string path = CharacterLoader.SyntheticPath("SC-FacePlus.glb");
            Assert.IsTrue(File.Exists(path),
                $"SC-FacePlus.glb not found at '{path}'. Run Generate Sample Characters first.");

            var task = CharacterLoader.LoadAsync(path, null);
            yield return SandboxTestUtil.WaitFor(task, 30f);
            var scene = SandboxTestUtil.ResolveScene(task, _created);

            var hub = scene.GetComponent<KhrCharacter>();
            Assert.IsNotNull(hub, "SC-FacePlus should import a KhrCharacter hub.");
            var ec = hub.Expressions;
            Assert.IsNotNull(ec, "KhrCharacter should have an ExpressionController.");

            var smr = scene.GetComponentInChildren<SkinnedMeshRenderer>();
            Assert.IsNotNull(smr, "SC-FacePlus should import a SkinnedMeshRenderer carrying the morph targets.");
            Assert.IsNotNull(smr.sharedMesh, "Imported SkinnedMeshRenderer should have a mesh.");
            var jaw = SandboxTestUtil.FindDeep(scene.transform, "Jaw");
            Assert.IsNotNull(jaw, "SC-FacePlus should carry a 'Jaw' bone driven by the jawOpen joint sub-driver.");

            // 'jawOpen' is authored as the FIRST track and drives BOTH morph blendshape 0 AND the jaw bone rotation
            // (Domains = Morph | Joint), so it is the natural morph+joint golden. Resolve the blendshape index by
            // name (falling back to the authored index 0) and read the imported frame weight, so the expected
            // output is exact regardless of whether the importer normalized frame weights to 1 or 100.
            const int authoredJawOpenIndex = 0;
            int jawOpenShape = smr.sharedMesh.GetBlendShapeIndex("jawOpen");
            if (jawOpenShape < 0) jawOpenShape = authoredJawOpenIndex;
            float frameWeight = smr.sharedMesh.GetBlendShapeFrameWeight(jawOpenShape, 0);

            // Rest: with all weights cleared the morph is neutral (0) and the jaw sits at its imported rest rotation.
            ec.ResetAll();
            yield return null; yield return null;
            float restShape = smr.GetBlendShapeWeight(jawOpenShape);
            Quaternion restRotation = jaw.localRotation;
            Assert.AreEqual(0f, restShape, 1e-3f, "At rest the jawOpen blendshape weight should be 0.");

            // Drive jawOpen fully. The morph delta saturates to 1.0, so the renderer weight is exactly
            // 1.0 * frameWeight; the jaw bone rotates by the authored ~18 deg delta, measured rebased to its rest
            // rotation (an angle is invariant under the glTF<->Unity handedness conversion, unlike raw coordinates).
            ec.SetWeight("jawOpen", 1f);
            yield return null; yield return null;

            float drivenShape = smr.GetBlendShapeWeight(jawOpenShape);
            // Exact == delta(1.0) * frameWeight; scale the tolerance so it's tight at frameWeight 1 and still robust
            // to round-trip float error at frameWeight 100.
            float shapeTol = Mathf.Max(0.01f, Mathf.Abs(frameWeight) * 0.001f);
            Assert.AreEqual(frameWeight, drivenShape, shapeTol,
                $"Driving jawOpen to 1 should set blendshape {jawOpenShape} to 1.0 x frameWeight ({frameWeight}).");

            float jawDeltaAngle = Quaternion.Angle(restRotation, jaw.localRotation);
            Assert.AreEqual(18f, jawDeltaAngle, 1f,
                "Driving jawOpen should rotate the jaw bone ~18 deg from its rest rotation (the authored joint delta).");

            ec.ResetAll();
        }

        [UnityTest]
        public IEnumerator M3_Gaze_WorldTargetDrivesLookWeight()
        {
            string path = CharacterLoader.SyntheticPath("SC-Face.glb");
            Assert.IsTrue(File.Exists(path),
                $"SC-Face.glb not found at '{path}'. Run Generate Sample Characters first.");

            var task = CharacterLoader.LoadAsync(path, null);
            yield return SandboxTestUtil.WaitFor(task, 30f);
            var scene = SandboxTestUtil.ResolveScene(task, _created);

            var hub = scene.GetComponent<KhrCharacter>();
            Assert.IsNotNull(hub, "SC-Face should import a KhrCharacter hub.");
            var gaze = hub.Gaze;
            Assert.IsNotNull(gaze, "SC-Face should have a GazeSolver after import (it has look expressions).");
            var ec = hub.Expressions;
            Assert.IsNotNull(ec, "KhrCharacter should have an ExpressionController.");

            gaze.Weight = 1f;
            // Off-axis target so at least one look direction is driven regardless of import orientation.
            gaze.SetWorldTarget(new Vector3(1f, 0.5f, 1f));

            // Let GazeSolver (LateUpdate order 50) and ExpressionController (order 100) run.
            yield return null;
            yield return null;

            float lookSum = ec.GetWeight(gaze.LookLeft) + ec.GetWeight(gaze.LookRight)
                          + ec.GetWeight(gaze.LookUp) + ec.GetWeight(gaze.LookDown);
            Assert.Greater(lookSum, 0f, "Gaze toward an off-axis target should drive at least one look expression.");

            gaze.Weight = 0f;
        }

        [UnityTest]
        public IEnumerator M4_RigSwitch_RequiresHumanoidAndPreservesBonePose()
        {
            string path = CharacterLoader.SyntheticPath("SC-Body.glb");
            Assert.IsTrue(File.Exists(path),
                $"SC-Body.glb not found at '{path}'. Run Generate Sample Characters first.");

            var task = CharacterLoader.LoadAsync(path, null);
            yield return SandboxTestUtil.WaitFor(task, 30f);
            var scene = SandboxTestUtil.ResolveScene(task, _created);

            var hub = scene.GetComponent<KhrCharacter>();
            Assert.IsNotNull(hub, "SC-Body should import a KhrCharacter hub.");
            var skeleton = hub.Skeleton;
            Assert.IsNotNull(skeleton, "SC-Body should have a SkeletonMap after import.");

            var head = SandboxTestUtil.FindDeep(scene.transform, "Head");
            Assert.IsNotNull(head, "SC-Body should carry a 'Head' bone.");
            Vector3 headWorldGeneric = head.position;   // captured in the imported (Generic) rig

            // SC-Body carries all 15 Unity-required humanoid bones, so the humanoid build is REQUIRED, not
            // best-effort. If this fails it is a latent fixture/plugin bug or a headless AvatarBuilder limitation —
            // root-cause and report it; do NOT weaken the assert.
            bool built = false;
            Assert.DoesNotThrow(() => built = skeleton.SwitchRigMode(RigImportMode.Humanoid));
            Assert.IsTrue(built,
                "REQUIRE humanoid build on SC-Body: SwitchRigMode(Humanoid) returned false (root-cause, do not weaken).");
            Assert.IsTrue(skeleton.HumanoidAvailable, "SkeletonMap.HumanoidAvailable should be true after a successful humanoid build.");

            // The build adds + assigns a Mecanim humanoid Avatar to the character's Animator — assert it is a real,
            // valid humanoid (not just a non-null switch result).
            var animator = scene.GetComponent<Animator>();
            Assert.IsNotNull(animator, "A successful humanoid build should add an Animator to the character.");
            var avatar = animator.avatar;
            Assert.IsNotNull(avatar, "The humanoid build should assign a non-null Avatar to the Animator.");
            Assert.IsTrue(avatar.isValid, "Built humanoid Avatar must be valid.");
            Assert.IsTrue(avatar.isHuman, "Built Avatar must be a humanoid (isHuman).");
            yield return null;

            // The build applies the reference pose only transiently (restored afterwards), so a bone's
            // world position must be preserved across the Generic -> Humanoid switch.
            Vector3 headWorldHumanoid = head.position;
            Assert.Less((headWorldHumanoid - headWorldGeneric).magnitude, 1e-3f,
                "Head bone world position must be preserved across the Generic -> Humanoid switch.");

            // Switch back to Generic; the world position must still be preserved and the humanoid flag cleared.
            bool generic = false;
            Assert.DoesNotThrow(() => generic = skeleton.SwitchRigMode(RigImportMode.Generic));
            Assert.IsTrue(generic, "SwitchRigMode(Generic) should always succeed.");
            yield return null;

            Vector3 headWorldBack = head.position;
            Assert.Less((headWorldBack - headWorldGeneric).magnitude, 1e-3f,
                "Head bone world position must be preserved across the Humanoid -> Generic switch.");
            Assert.IsFalse(skeleton.HumanoidAvailable, "HumanoidAvailable should be false after switching back to Generic.");
        }
    }
}
