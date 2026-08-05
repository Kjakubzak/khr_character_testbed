using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using Samples.Shared;
using UnityEngine;
using UnityEngine.TestTools;
using UnityGLTF.KhrCharacter;

namespace KhrCharacterTestbed.Tests
{
    public class SandboxExpressionResponseTests
    {
        private readonly List<Object> _created = new List<Object>();

        [TearDown]
        public void TearDown()
        {
            foreach (var value in _created)
                if (value != null) Object.DestroyImmediate(value);
            _created.Clear();
        }

        [UnityTest]
        public IEnumerator PassiveEvaluator_ExposesIndexAddressedResponsesWithoutSynchronousSceneWrites()
        {
            string path = CharacterLoader.SyntheticPath("SC-Face.glb");
            var task = CharacterLoader.LoadAsync(path, null);
            yield return SandboxTestUtil.WaitFor(task, 30f);
            var scene = SandboxTestUtil.ResolveScene(task, _created);
            var hub = scene.GetComponent<KhrCharacter>();
            Assert.IsNotNull(hub);
            Assert.IsNull(hub.Expressions,
                "the shared loader's default passive policy must not create the Unity scene-writing adapter");
            Assert.IsNotNull(hub.ExpressionResponses);
            Assert.AreEqual(8, hub.ExpressionResponses.Count);

            int jawIndex = FindExpression(hub.ExpressionResponses, "jawOpen");
            int aaIndex = FindExpression(hub.ExpressionResponses, "aa");
            Assert.AreEqual(0, jawIndex, "array position, not label lookup, is the authoritative identity");

            var renderer = scene.GetComponentInChildren<SkinnedMeshRenderer>();
            var jaw = SandboxTestUtil.FindDeep(scene.transform, "Jaw");
            Assert.IsNotNull(renderer);
            Assert.IsNotNull(jaw);
            float morphBefore = renderer.GetBlendShapeWeight(0);
            Quaternion jawBefore = jaw.localRotation;

            var empty = hub.ExpressionResponses.Evaluate(aaIndex, 0f);
            var positive = hub.ExpressionResponses.Evaluate(jawIndex, 1f);

            Assert.IsEmpty(empty.Records);
            Assert.AreEqual(2, positive.Records.Count,
                "the base evaluator retains every response channel; typed classifiers do not filter it");
            Assert.AreEqual(morphBefore, renderer.GetBlendShapeWeight(0));
            Assert.AreEqual(jawBefore, jaw.localRotation);
        }

        [UnityTest]
        public IEnumerator UnsupportedOptionalTextureTargets_SetDurationWithoutRecords()
        {
            var load = SandboxTestUtil.LoadSynthetic(
                "SC-FacePlus.glb",
                _created,
                CharacterExpressionHostPolicy.Passive);
            yield return load;

            var responses = load.Current.GetComponent<KhrCharacter>()?.ExpressionResponses;
            Assert.IsNotNull(responses);
            int textureIndex = FindExpression(responses, "texFx");

            var response = responses.Evaluate(textureIndex, 0.5f);

            Assert.Greater(response.Duration, 0f);
            Assert.AreEqual(response.Duration * 0.5f, response.SampleTime, 1e-6f);
            Assert.IsEmpty(response.Records,
                "unsupported optional targets omit records while their samplers still determine common duration");
        }

        private static int FindExpression(ExpressionResponseSet set, string label)
        {
            for (int index = 0; index < set.Entries.Count; index++)
                if (set.Entries[index]?.Label == label) return index;
            Assert.Fail($"Expression '{label}' was not imported.");
            return -1;
        }
    }
}
