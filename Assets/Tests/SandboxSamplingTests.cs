using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using GLTF.Schema;
using UnityGLTF.Extensions;
using UnityGLTF.KhrCharacter;
using Samples.Shared;

namespace KhrCharacterTestbed.Tests
{
    /// <summary>
    /// Sampling fidelity (02 anim lens, Phase 4-P3): assert the committed animation samplers on the wire -
    /// discrete response tracks use STEP interpolation, multi-key UV-transform uses LINEAR, key counts match the authored
    /// keyframes, and the time/input accessor carries min/max. Anti-hollow via real plugin types.
    /// </summary>
    public class SandboxSamplingTests
    {
        private readonly List<Object> _created = new List<Object>();

        [TearDown]
        public void TearDown()
        {
            foreach (var o in _created)
                if (o != null) Object.DestroyImmediate(o);
            _created.Clear();
        }

        private static string PointerPath(AnimationChannel ch)
            => ch.Target != null && ch.Target.Extensions != null
               && ch.Target.Extensions.TryGetValue(KHR_animation_pointer.EXTENSION_NAME, out var ext)
               && ext is KHR_animation_pointer p ? p.path : null;

        private static KHR_character_expression GetExpression(GLTFRoot root)
            => root.Extensions != null
               && root.Extensions.TryGetValue(KHR_character_expression.EXTENSION_NAME, out var e)
                ? e as KHR_character_expression : null;

        [Test]
        public void EveryResponseChannel_HasTwoKeysAndInputMetadata()
        {
            var root = CharacterLoader.ReadGltfRoot(CharacterLoader.SyntheticPath("SC-Face.glb"));
            var expr = GetExpression(root);
            Assert.IsNotNull(expr, "SC-Face should carry KHR_character_expression.");

            // Classifiers are metadata, never channel filters. Check every channel in every referenced response.
            bool foundStep = false;
            foreach (var item in expr.Expressions)
            {
                if (item == null) continue;
                var anim = root.Animations[item.Animation];
                foreach (var ch in anim.Channels)
                {
                    var sampler = anim.Samplers[ch.Sampler.Id];
                    var input = root.Accessors[sampler.Input.Id];
                    Assert.IsNotNull(input.Min, "a sampler's time/input accessor must carry min.");
                    Assert.IsNotNull(input.Max, "a sampler's time/input accessor must carry max.");
                    Assert.GreaterOrEqual(input.Count, 2, "an expression response sampler must have at least two keys.");
                    if (sampler.Interpolation == InterpolationType.STEP) foundStep = true;
                }
            }
            Assert.IsTrue(foundStep, "SC-Face's discrete morph responses must export STEP-interpolated samplers.");
        }

        [Test]
        public void TextureUv_MultiKeyDriver_UsesLinearSampler()
        {
            var root = CharacterLoader.ReadGltfRoot(CharacterLoader.SyntheticPath("SC-FacePlus.glb"));
            var expr = GetExpression(root);
            Assert.IsNotNull(expr);
            var item = expr.Expressions.Find(i => i != null && i.Texture != null);
            Assert.IsNotNull(item, "SC-FacePlus should carry a texture expression.");

            var anim = root.Animations[item.Animation];
            bool foundLinearUv = false;
            foreach (int ci in item.Texture.Channels)
            {
                var ch = anim.Channels[ci];
                var path = PointerPath(ch);
                if (path == null) continue;
                if (path.EndsWith("/extensions/KHR_texture_transform/scale")
                    || path.EndsWith("/extensions/KHR_texture_transform/offset"))
                {
                    var sampler = anim.Samplers[ch.Sampler.Id];
                    var input = root.Accessors[sampler.Input.Id];
                    Assert.AreEqual(2, input.Count, "the authored UV-transform driver has two keyframes (t=0,1).");
                    if (sampler.Interpolation == InterpolationType.LINEAR) foundLinearUv = true;
                }
            }
            Assert.IsTrue(foundLinearUv, "the multi-key UV-transform driver must export a LINEAR-interpolated sampler.");
        }
    }
}
