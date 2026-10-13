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
    /// Spatial / geometry consume-side tests on a real imported character (03 rig lens, Phase 3-P2): the resolved
    /// reference pose (every node, targeted or not, takes its authored or sampled local value; never its live one) and
    /// camera-hint apply (the camera takes the hint node's position and authored rotation R; a target node only
    /// supplies a subject point). State/transform-only, so render-pipeline-agnostic. Anti-hollow via real types.
    /// </summary>
    public class SandboxRigSpatialTests
    {
        // SC-Body: node 0 is the untargeted glTF root, node 5 the Head, node 20 the untargeted PortraitCamera.
        private const int RootNode = 0;
        private const int HeadNode = 5;
        private const int PortraitCameraNode = 20;
        private const int NodeCount = 21;

        private readonly List<Object> _created = new List<Object>();

        [TearDown]
        public void TearDown()
        {
            foreach (var o in _created)
                if (o != null) Object.DestroyImmediate(o);
            _created.Clear();
        }

        [UnityTest]
        public IEnumerator ReferencePose_ExposesTheTaggedAnimationWithResolvedValues()
        {
            var load = SandboxTestUtil.LoadSynthetic("SC-Body.glb", _created);
            yield return load;
            var skeleton = load.Current.GetComponent<KhrCharacter>()?.Skeleton;
            Assert.IsNotNull(skeleton, "SC-Body should have a SkeletonMap.");

            Assert.AreEqual(1, skeleton.ReferencePoses.Count, "SC-Body tags exactly one reference-pose animation.");
            var pose = skeleton.ReferencePoses[0];
            Assert.IsTrue(pose.IsValid, string.Join(" | ", pose.Errors ?? new string[0]));
            Assert.AreEqual(ReferencePoseLabels.TPose, pose.PoseType, "SC-Body's resolved poseType is t_pose.");
            Assert.AreEqual("t_pose", pose.PoseType);
            Assert.AreEqual(NodeCount, pose.ResolvedNodeIndices.Length, "every node has a resolved reference-pose value.");
            Assert.AreEqual(NodeCount, pose.ResolvedLocalPositions.Length);
            Assert.AreEqual(NodeCount, pose.ResolvedLocalRotations.Length);
            Assert.AreEqual(NodeCount, pose.ResolvedLocalScales.Length);
            for (int i = 0; i < NodeCount; i++)
                Assert.IsNotNull(pose.ResolvedNodes[i], $"resolved node {pose.ResolvedNodeIndices[i]} should be instantiated.");

            // The global reference pose composes the resolved locals: Hips 1.0 + Spine 0.12 + Chest 0.12 + Neck 0.18 +
            // Head 0.1 above the untransformed root.
            Assert.IsTrue(skeleton.TryGetGlobalReferencePose(pose.AnimationIndex, HeadNode, out var headGlobal));
            Assert.Less(Vector3.Distance(new Vector3(0f, 1.52f, 0f), (Vector3)headGlobal.GetColumn(3)), 1e-4f);
        }

        [UnityTest]
        public IEnumerator ReferencePose_RestoresAuthoredLocalsOfTargetedAndUntargetedNodes()
        {
            var load = SandboxTestUtil.LoadSynthetic("SC-Body.glb", _created);
            yield return load;
            var hub = load.Current.GetComponent<KhrCharacter>();
            Assert.IsNotNull(hub, "SC-Body should import a KhrCharacter hub.");
            var skeleton = hub.Skeleton;
            Assert.IsNotNull(skeleton, "SC-Body should have a SkeletonMap.");
            var pose = skeleton.ReferencePoses[0];
            var root = ResolvedNode(pose, RootNode);
            var portraitCamera = ResolvedNode(pose, PortraitCameraNode);
            Assert.IsTrue(skeleton.TryGetBone("head", out var head), "SC-Body should map the 'head' joint.");
            Assert.AreSame(ResolvedNode(pose, HeadNode), head);

            // Perturb a targeted bone and two nodes the pose does not target.
            head.localRotation = Quaternion.Euler(40f, 15f, 0f);
            head.localPosition += new Vector3(0.1f, 0.05f, 0f);
            root.localPosition = new Vector3(0.3f, 0f, 0.2f);
            root.localRotation = Quaternion.Euler(0f, 25f, 0f);
            portraitCamera.localPosition += new Vector3(0.2f, 0f, 0f);
            portraitCamera.localScale = new Vector3(2f, 2f, 2f);
            yield return null;

            skeleton.ApplyReferencePose();

            // Authored glTF values (X negated into Unity space): the targeted Head's sampled pose, and the authored
            // locals of the untargeted root (core defaults) and PortraitCamera, never their live values.
            AssertLocal(head, new Vector3(0f, 0.1f, 0f), "Head");
            AssertLocal(root, Vector3.zero, "the untargeted glTF root");
            AssertLocal(portraitCamera, new Vector3(0f, 1.5f, 1.2f), "the untargeted PortraitCamera");
        }

        [UnityTest]
        public IEnumerator CameraHint_AppliesAuthoredPose()
        {
            var load = SandboxTestUtil.LoadSynthetic("SC-Body.glb", _created);
            yield return load;
            var hub = load.Current.GetComponent<KhrCharacter>();
            var chs = hub.CameraHints;
            Assert.IsNotNull(chs, "SC-Body should have a CameraHintSet.");
            Assert.IsTrue(chs.TryGetByRole("portrait", out var hint), "SC-Body should carry a 'portrait' camera hint.");
            Assert.IsNotNull(hint.Node, "the portrait hint should resolve its node.");
            Assert.IsNotNull(hint.Target, "the portrait hint should resolve its target node (the head).");
            Assert.IsTrue(CameraHintSet.TryGetSubjectPoint(hint, out var subject), "the target node supplies a subject point.");
            Assert.Less(Vector3.Distance(subject, hint.Target.position), 1e-5f);

            var camGo = new GameObject("TestCam", typeof(Camera));
            _created.Add(camGo);
            var cam = camGo.GetComponent<Camera>();

            // The authored path has no rotation, so R is the identity: the camera looks along the hint's -Z, +Y up.
            chs.Apply(hint, cam);
            AssertPose(cam, hint.Node.position, Vector3.back, "the authored pose");

            // Yawing the hint node turns R; the camera follows it rather than aiming at the head.
            hint.Node.localRotation = Quaternion.Euler(0f, 20f, 0f);
            chs.Apply(hint, cam);
            AssertPose(cam, hint.Node.position, Quaternion.Euler(0f, 20f, 0f) * Vector3.back, "a yawed hint node");
            Vector3 toHead = (hint.Target.position - hint.Node.position).normalized;
            Assert.Greater(Vector3.Angle(cam.transform.forward, toHead), 15f, "the target node must not aim the camera.");

            // A hint without a target node applies the same way; it simply has no subject point.
            hint.Node.localRotation = Quaternion.identity;
            var untargeted = new CameraHint { Role = hint.Role, Label = hint.Label, Node = hint.Node };
            Assert.IsFalse(CameraHintSet.TryGetSubjectPoint(untargeted, out _));
            chs.Apply(untargeted, cam);
            AssertPose(cam, hint.Node.position, Vector3.back, "a hint without targetNode");

            // Host placement: rotating the imported scene root turns the camera pose with it.
            load.Current.transform.rotation = Quaternion.Euler(0f, 30f, 0f);
            chs.Apply(hint, cam);
            AssertPose(cam, hint.Node.position, Quaternion.Euler(0f, 30f, 0f) * Vector3.back, "a rotated scene root");
        }

        private static Transform ResolvedNode(ReferencePose pose, int nodeIndex)
        {
            int entry = System.Array.IndexOf(pose.ResolvedNodeIndices, nodeIndex);
            Assert.GreaterOrEqual(entry, 0, $"node {nodeIndex} should have a resolved reference-pose value.");
            Assert.IsNotNull(pose.ResolvedNodes[entry], $"node {nodeIndex} should be instantiated.");
            return pose.ResolvedNodes[entry];
        }

        private static void AssertLocal(Transform node, Vector3 position, string label)
        {
            Assert.Less(Vector3.Distance(node.localPosition, position), 1e-4f, $"{label}: local position.");
            Assert.Less(Quaternion.Angle(node.localRotation, Quaternion.identity), 0.05f, $"{label}: local rotation.");
            Assert.Less(Vector3.Distance(node.localScale, Vector3.one), 1e-4f, $"{label}: local scale.");
        }

        private static void AssertPose(Camera cam, Vector3 position, Vector3 forward, string label)
        {
            Assert.Less(Vector3.Distance(cam.transform.position, position), 1e-4f, $"{label}: camera position.");
            Assert.Less(Vector3.Angle(cam.transform.forward, forward), 0.05f, $"{label}: camera forward.");
            Assert.Less(Vector3.Angle(cam.transform.up, Vector3.up), 0.05f, $"{label}: camera up.");
        }
    }
}
