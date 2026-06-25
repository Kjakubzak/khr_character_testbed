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
    /// Sampling fidelity (02 anim lens, Phase 4-P3): import, re-export, and assert the animation SAMPLERS on the wire -
    /// binary/discrete drivers use STEP interpolation, multi-key UV-transform uses LINEAR, key counts match the authored
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

        [UnityTest]
        public IEnumerator Morph_BinaryDriver_UsesStepSamplerWithMinMaxInput()
        {
            var load = SandboxTestUtil.LoadSynthetic("SC-Face.glb", _created);
            yield return load;
            var hub = load.Current.GetComponent<KhrCharacter>();
            Assert.IsNotNull(hub, "SC-Face should import a KhrCharacter hub.");

            CharacterLoader.ExportToGlb(hub.gameObject, out var root);
            var expr = GetExpression(root);
            Assert.IsNotNull(expr, "SC-Face re-export should carry KHR_character_expression.");

            // SC-Face's binary morphs (e.g. blink/jawOpen) author STEP samplers; at least one morph channel must be STEP.
            bool foundStep = false;
            foreach (var item in expr.Expressions)
            {
                if (item == null || item.Morphtarget == null) continue;
                var anim = root.Animations[item.Animation];
                foreach (int ci in item.Morphtarget.Channels)
                {
                    var ch = anim.Channels[ci];
                    var sampler = anim.Samplers[ch.Sampler.Id];
                    var input = root.Accessors[sampler.Input.Id];
                    Assert.IsNotNull(input.Min, "a sampler's time/input accessor must carry min.");
                    Assert.IsNotNull(input.Max, "a sampler's time/input accessor must carry max.");
                    Assert.GreaterOrEqual(input.Count, 1, "a sampler must have at least one keyframe.");
                    if (sampler.Interpolation == InterpolationType.STEP) foundStep = true;
                }
            }
            Assert.IsTrue(foundStep, "SC-Face's binary morph drivers must export STEP-interpolated samplers.");
        }

        [UnityTest]
        public IEnumerator TextureUv_MultiKeyDriver_UsesLinearSampler()
        {
            var load = SandboxTestUtil.LoadSynthetic("SC-FacePlus.glb", _created);
            yield return load;
            var hub = load.Current.GetComponent<KhrCharacter>();
            Assert.IsNotNull(hub, "SC-FacePlus should import a KhrCharacter hub.");

            CharacterLoader.ExportToGlb(hub.gameObject, out var root);
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
