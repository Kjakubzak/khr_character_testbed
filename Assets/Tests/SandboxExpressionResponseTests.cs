using System.Collections;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;
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
                "only channels a supported derived extension lists produce records; jawOpen lists its morph weight " +
                "with _morphtarget and its jaw rotation with _joint");
            Assert.AreEqual(morphBefore, renderer.GetBlendShapeWeight(0));
            Assert.AreEqual(jawBefore, jaw.localRotation);
        }

        [UnityTest]
        public IEnumerator TextureTransformTargets_EmitRecordsAndSetDuration()
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
            Assert.AreEqual(1, response.Records.Count,
                "texFx lists its KHR_texture_transform offset channel with _texture_transform, so it emits a record");
            Assert.AreEqual(ExpressionPropertyKind.TextureOffset, response.Records[0].Target.Kind);
        }

        // Unlisted channels produce no record but still set the expression duration.
        [UnityTest]
        public IEnumerator UnlistedChannel_EmitsNoRecordAndKeepsDuration()
        {
            var original = LoadPatchedFacePlus(null);
            yield return original;
            var unlisted = LoadPatchedFacePlus(root =>
                ((JObject)Entry(root, 0)["extensions"]).Remove("KHR_character_expression_joint"));
            yield return unlisted;
            var bare = LoadPatchedFacePlus(root => ((JObject)Entry(root, 0)).Remove("extensions"));
            yield return bare;

            var full = ResponsesOf(original).Evaluate(0, 1f);
            var morphOnly = ResponsesOf(unlisted).Evaluate(0, 1f);
            var none = ResponsesOf(bare).Evaluate(0, 1f);

            Assert.AreEqual(2, full.Records.Count, "jawOpen lists both of its channels.");
            Assert.AreEqual(1, morphOnly.Records.Count, "the unlisted jaw rotation must produce no record.");
            Assert.AreEqual(ExpressionPropertyKind.NodeWeights, morphOnly.Records[0].Target.Kind);
            Assert.IsEmpty(none.Records, "an entry that lists no channel produces no record.");
            Assert.AreEqual(full.Duration, morphOnly.Duration, "an unlisted channel still contributes to the duration.");
            Assert.AreEqual(full.Duration, none.Duration, "unlisted channels alone still define the duration.");
        }

        // The pre-rename key is an unknown extension, so its texture channel is not animated.
        [UnityTest]
        public IEnumerator LegacyTextureKey_IsUnknownAndEmitsNoTextureRecords()
        {
            var load = LoadPatchedFacePlus(root =>
            {
                int texFx = FindEntryByName(root, "texFx");
                var extensions = (JObject)Entry(root, texFx)["extensions"];
                var listing = extensions["KHR_character_expression_texture_transform"];
                extensions.Remove("KHR_character_expression_texture_transform");
                extensions["KHR_character_expression_texture"] = listing;
                var used = (JArray)root["extensionsUsed"];
                for (int i = 0; i < used.Count; i++)
                    if ((string)used[i] == "KHR_character_expression_texture_transform")
                        used[i] = "KHR_character_expression_texture";
            });
            yield return load;

            var responses = ResponsesOf(load);
            var response = responses.Evaluate(FindExpression(responses, "texFx"), 1f);
            Assert.Greater(response.Duration, 0f, "the channel still contributes to the duration.");
            Assert.IsEmpty(response.Records, "a channel listed only by an unknown extension produces no record.");
            Assert.IsFalse(responses.Validation.IsComplete,
                "an unrecognized entry extension makes validation incomplete rather than invalid.");
        }

        private SandboxTestUtil.BytesLoad LoadPatchedFacePlus(System.Action<JObject> patch)
        {
            byte[] glb = System.IO.File.ReadAllBytes(CharacterLoader.SyntheticPath("SC-FacePlus.glb"));
            if (patch != null) glb = SandboxTestUtil.PatchGlbJson(glb, patch);
            return new SandboxTestUtil.BytesLoad(glb, _created, CharacterExpressionHostPolicy.Passive);
        }

        private static ExpressionResponseSet ResponsesOf(SandboxTestUtil.BytesLoad load)
        {
            var responses = load.Current.GetComponent<KhrCharacter>()?.ExpressionResponses;
            Assert.IsNotNull(responses, "the patched fixture must still import its expression responses.");
            return responses;
        }

        private static JToken Entry(JObject root, int index)
            => root["extensions"]["KHR_character_expression"]["expressions"][index];

        private static int FindEntryByName(JObject root, string name)
        {
            var expressions = (JArray)root["extensions"]["KHR_character_expression"]["expressions"];
            for (int index = 0; index < expressions.Count; index++)
                if ((string)expressions[index]["name"] == name) return index;
            Assert.Fail($"Expression '{name}' is not in the fixture.");
            return -1;
        }

        private static int FindExpression(ExpressionResponseSet set, string name)
        {
            for (int index = 0; index < set.Entries.Count; index++)
                if (set.Entries[index]?.Name == name) return index;
            Assert.Fail($"Expression '{name}' was not imported.");
            return -1;
        }
    }
}
