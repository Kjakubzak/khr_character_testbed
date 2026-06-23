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
    /// M1 functional proof: importing the synthetic SC-Face.glb through the shared runtime loader yields a
    /// KhrCharacter hub carrying at least one expression. Because it references the real plugin types, it also
    /// fails to compile if the dependency ever goes hollow (anti-hollow gate, like the smoke test).
    /// </summary>
    public class SandboxM1Tests
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
        public IEnumerator Import_SCFace_ProducesKhrCharacterWithExpressions()
        {
            string path = CharacterLoader.DefaultAbsolutePath;
            Assert.IsTrue(File.Exists(path),
                $"SC-Face.glb not found at '{path}'. Run Assets > UnityGLTF > KHR Character > Generate Sample Characters first.");

            var task = CharacterLoader.LoadAsync(path, null);

            float deadline = Time.realtimeSinceStartup + 30f;
            while (!task.IsCompleted && Time.realtimeSinceStartup < deadline)
                yield return null;

            Assert.IsTrue(task.IsCompleted, "glTF import did not complete within 30s.");
            if (task.Exception != null) throw task.Exception;

            var scene = task.Result;
            Assert.IsNotNull(scene, "Imported scene root is null.");
            _created.Add(scene);

            var hub = scene.GetComponent<KhrCharacter>();
            Assert.IsNotNull(hub,
                "Imported scene has no KhrCharacter hub (KHR import plugin disabled, or the dependency is hollow).");

            var controller = hub.Expressions;
            Assert.IsNotNull(controller, "KhrCharacter has no ExpressionController.");
            Assert.GreaterOrEqual(controller.Expressions.Count, 1, "Expected at least one expression on SC-Face.");
        }
    }
}
