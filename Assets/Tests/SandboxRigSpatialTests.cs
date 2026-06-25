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
    /// Spatial / geometry consume-side tests on a real imported character (03 rig lens, Phase 3-P2): reference-pose
    /// apply (perturb -> apply -> snap back to the stored local TRS) and camera-hint apply (place + orient a Camera at
    /// the hint node looking at the head). State/transform-only, so render-pipeline-agnostic. Anti-hollow via real types.
    /// </summary>
    public class SandboxRigSpatialTests
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
        public IEnumerator ReferencePose_PerturbApplySnapsBack()
        {
            var load = SandboxTestUtil.LoadSynthetic("SC-Body.glb", _created);
            yield return load;
            var hub = load.Current.GetComponent<KhrCharacter>();
            Assert.IsNotNull(hub, "SC-Body should import a KhrCharacter hub.");
            var skeleton = hub.Skeleton;
            Assert.IsNotNull(skeleton, "SC-Body should have a SkeletonMap.");
            var pose = skeleton.Result != null ? skeleton.Result.ReferencePose : null;
            Assert.IsNotNull(pose, "SC-Body should import a reference pose.");

            Assert.IsTrue(skeleton.TryGetBone("head", out var head), "SC-Body should map the 'head' joint.");
            int idx = System.Array.IndexOf(pose.Bones, head);
            Assert.GreaterOrEqual(idx, 0, "the head bone should be part of the reference pose's bone list.");
            Quaternion refRot = pose.LocalRotations[idx];
            Vector3 refPos = pose.LocalPositions[idx];

            // Perturb the bone off its reference local TRS.
            head.localRotation = refRot * Quaternion.Euler(40f, 15f, 0f);
            head.localPosition = refPos + new Vector3(0.1f, 0.05f, 0f);
            yield return null;
            Assert.Greater(Quaternion.Angle(head.localRotation, refRot), 1f, "the perturbation should move the bone off the reference pose.");

            // Apply the reference pose: the bone must snap back to the stored local TRS.
            skeleton.ApplyReferencePose();
            Assert.Less(Quaternion.Angle(head.localRotation, refRot), 0.05f,
                "ApplyReferencePose should restore the head's reference local rotation.");
            Assert.Less(Vector3.Distance(head.localPosition, refPos), 1e-4f,
                "ApplyReferencePose should restore the head's reference local position.");
        }

        [UnityTest]
        public IEnumerator CameraHint_AppliesPlacementAndLooksAtHead()
        {
            var load = SandboxTestUtil.LoadSynthetic("SC-Body.glb", _created);
            yield return load;
            var hub = load.Current.GetComponent<KhrCharacter>();
            var chs = hub.CameraHints;
            Assert.IsNotNull(chs, "SC-Body should have a CameraHintSet.");
            Assert.IsTrue(chs.TryGetByRole("portrait", out var hint), "SC-Body should carry a 'portrait' camera hint.");
            Assert.IsNotNull(hint.Node, "the portrait hint should resolve a placement node.");
            Assert.IsNotNull(hint.Target, "the portrait hint should resolve its target (the head).");

            var camGo = new GameObject("TestCam", typeof(Camera));
            _created.Add(camGo);
            var cam = camGo.GetComponent<Camera>();

            chs.Apply(hint, cam);

            Assert.Less(Vector3.Distance(cam.transform.position, hint.Node.position), 1e-3f,
                "Apply should place the camera at the hint's placement node.");
            Vector3 toTarget = (hint.Target.position - hint.Node.position).normalized;
            Assert.Greater(Vector3.Dot(cam.transform.forward, toTarget), 0.99f,
                "Apply should orient the camera to look at the hint target (head).");
        }
    }
}
