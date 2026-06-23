using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityGLTF.KhrCharacter;
using Samples.Shared;

namespace KhrCharacterTestbed.Tests
{
    /// <summary>
    /// Phase-2b functional proofs (bounded PlayMode): M2 (texture + joint expressions on SC-FacePlus), M3 (gaze
    /// drives a look expression on SC-Face), M4 (rig switch on SC-Body — reports HumanoidAvailable, does not
    /// hard-require it since the humanoid build is best-effort). All reference real plugin types, so they also act
    /// as anti-hollow gates.
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
        public IEnumerator M2_TextureAndJoint_FacePlusDrivesBothDomains()
        {
            string path = CharacterLoader.SyntheticPath("SC-FacePlus.glb");
            Assert.IsTrue(File.Exists(path),
                $"SC-FacePlus.glb not found at '{path}'. Run Generate Sample Characters first.");

            var task = CharacterLoader.LoadAsync(path, null);
            yield return WaitFor(task, 30f);
            var scene = ResolveScene(task);

            var hub = scene.GetComponent<KhrCharacter>();
            Assert.IsNotNull(hub, "SC-FacePlus should import a KhrCharacter hub.");
            var ec = hub.Expressions;
            Assert.IsNotNull(ec, "KhrCharacter should have an ExpressionController.");

            string jointName = FindByDomain(ec, ExpressionDomain.Joint);
            string textureName = FindByDomain(ec, ExpressionDomain.Texture);
            Assert.IsNotNull(jointName, "SC-FacePlus should carry a joint-domain expression.");
            Assert.IsNotNull(textureName, "SC-FacePlus should carry a texture-domain expression.");

            Assert.DoesNotThrow(() =>
            {
                ec.SetWeight(jointName, 1f);
                ec.SetWeight(textureName, 1f);
            });
            yield return null;

            Assert.AreEqual(1f, ec.GetWeight(jointName), 1e-4f, "joint expression weight should read back.");
            Assert.AreEqual(1f, ec.GetWeight(textureName), 1e-4f, "texture expression weight should read back.");
            ec.ResetAll();
        }

        [UnityTest]
        public IEnumerator M3_Gaze_WorldTargetDrivesLookWeight()
        {
            string path = CharacterLoader.SyntheticPath("SC-Face.glb");
            Assert.IsTrue(File.Exists(path),
                $"SC-Face.glb not found at '{path}'. Run Generate Sample Characters first.");

            var task = CharacterLoader.LoadAsync(path, null);
            yield return WaitFor(task, 30f);
            var scene = ResolveScene(task);

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
        public IEnumerator M4_RigSwitch_BodySwitchesGracefully()
        {
            string path = CharacterLoader.SyntheticPath("SC-Body.glb");
            Assert.IsTrue(File.Exists(path),
                $"SC-Body.glb not found at '{path}'. Run Generate Sample Characters first.");

            var task = CharacterLoader.LoadAsync(path, null);
            yield return WaitFor(task, 30f);
            var scene = ResolveScene(task);

            var hub = scene.GetComponent<KhrCharacter>();
            Assert.IsNotNull(hub, "SC-Body should import a KhrCharacter hub.");
            var skeleton = hub.Skeleton;
            Assert.IsNotNull(skeleton, "SC-Body should have a SkeletonMap after import.");

            bool humanoid = false;
            Assert.DoesNotThrow(() => humanoid = skeleton.SwitchRigMode(RigImportMode.Humanoid));
            // Humanoid is best-effort: report, do not require.
            Debug.Log($"[M4] SwitchRigMode(Humanoid)={humanoid}, HumanoidAvailable={skeleton.HumanoidAvailable}, Direction={skeleton.DetectedDirection}");

            bool generic = false;
            Assert.DoesNotThrow(() => generic = skeleton.SwitchRigMode(RigImportMode.Generic));
            Assert.IsTrue(generic, "SwitchRigMode(Generic) should always succeed.");
            yield return null;
        }

        // ── helpers ──────────────────────────────────────────────────────────

        private GameObject ResolveScene(Task<GameObject> task)
        {
            Assert.IsTrue(task.IsCompleted, "glTF import did not complete within the timeout.");
            if (task.Exception != null) throw task.Exception;
            var scene = task.Result;
            Assert.IsNotNull(scene, "Imported scene root is null.");
            _created.Add(scene);
            return scene;
        }

        private static IEnumerator WaitFor(Task task, float timeoutSeconds)
        {
            float deadline = Time.realtimeSinceStartup + timeoutSeconds;
            while (!task.IsCompleted && Time.realtimeSinceStartup < deadline)
                yield return null;
        }

        private static string FindByDomain(ExpressionController controller, ExpressionDomain domain)
        {
            foreach (var handle in controller.Expressions)
                if ((handle.Domains & domain) != 0 && !string.IsNullOrEmpty(handle.Name))
                    return handle.Name;
            return null;
        }
    }
}
