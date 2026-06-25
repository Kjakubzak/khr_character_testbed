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
    /// Animation-transport wire (02 anim lens, Phase 2-P1): import a fixture, re-export through the KHR plugin, and
    /// lock the channel shapes on the wire. Morph + texture drivers route through KHR_animation_pointer with exact
    /// JSON-pointer paths; joint drivers and the reference pose stay NATIVE TRS even though the AnimationPointer
    /// export plugin is enabled (the G2 native-vs-pointer guard). Pointer detection goes through the channel
    /// target's KHR_animation_pointer extension (Target.Path is just the literal "pointer"). Anti-hollow via real types.
    /// </summary>
    public class SandboxAnimTests
    {
        private readonly List<Object> _created = new List<Object>();

        [TearDown]
        public void TearDown()
        {
            foreach (var o in _created)
                if (o != null) Object.DestroyImmediate(o);
            _created.Clear();
        }

        // Reach a channel's KHR_animation_pointer JSON-pointer path (null if it is a native TRS channel).
        private static string PointerPath(AnimationChannel ch)
            => ch.Target != null && ch.Target.Extensions != null
               && ch.Target.Extensions.TryGetValue(KHR_animation_pointer.EXTENSION_NAME, out var ext)
               && ext is KHR_animation_pointer p ? p.path : null;

        private static KHR_character_expression GetExpression(GLTFRoot root)
            => root.Extensions != null
               && root.Extensions.TryGetValue(KHR_character_expression.EXTENSION_NAME, out var e)
                ? e as KHR_character_expression : null;

        private static readonly string[] TrsPaths = { "rotation", "translation", "scale" };

        [UnityTest]
        public IEnumerator Morph_ExportsAsWeightsPointerChannel()
        {
            var load = SandboxTestUtil.LoadSynthetic("SC-Face.glb", _created);
            yield return load;
            var hub = load.Current.GetComponent<KhrCharacter>();
            Assert.IsNotNull(hub, "SC-Face should import a KhrCharacter hub.");

            CharacterLoader.ExportToGlb(hub.gameObject, out var root);
            var expr = GetExpression(root);
            Assert.IsNotNull(expr, "SC-Face re-export should carry KHR_character_expression.");
            var item = expr.Expressions.Find(i => i != null && i.Morphtarget != null);
            Assert.IsNotNull(item, "SC-Face has morph expressions; an item must carry KHR_character_expression_morphtarget.");

            var anim = root.Animations[item.Animation];
            bool foundWeightsPointer = false;
            foreach (int ci in item.Morphtarget.Channels)
            {
                var ch = anim.Channels[ci];
                Assert.AreEqual("pointer", ch.Target.Path, "morph weights export via KHR_animation_pointer.");
                var path = PointerPath(ch);
                Assert.IsNotNull(path, "the morph channel must carry a KHR_animation_pointer target extension.");
                var parts = path.Split('/');   // ["", "nodes", "{n}", "weights", "{j}"]
                if (parts.Length == 5 && parts[1] == "nodes" && parts[3] == "weights") foundWeightsPointer = true;
            }
            Assert.IsTrue(foundWeightsPointer, "at least one morph channel must be a '/nodes/{n}/weights/{j}' pointer.");
        }

        [UnityTest]
        public IEnumerator Joint_StaysNativeTRS_EvenWithPointerEnabled()
        {
            var load = SandboxTestUtil.LoadSynthetic("SC-Face.glb", _created);
            yield return load;
            var hub = load.Current.GetComponent<KhrCharacter>();
            Assert.IsNotNull(hub, "SC-Face should import a KhrCharacter hub.");

            // ExportToGlb ENABLES the AnimationPointer export plugin (it routes morph/texture through pointers); the
            // joint channels must nevertheless stay native TRS - that is the whole point of the G2 guard.
            CharacterLoader.ExportToGlb(hub.gameObject, out var root);
            var expr = GetExpression(root);
            Assert.IsNotNull(expr);
            var item = expr.Expressions.Find(i => i != null && i.Joint != null);
            Assert.IsNotNull(item, "SC-Face's jawOpen drives the jaw bone; an item must carry KHR_character_expression_joint.");

            var anim = root.Animations[item.Animation];
            Assert.Greater(item.Joint.Channels.Length, 0, "the joint sub-extension must reference at least one channel.");
            foreach (int ci in item.Joint.Channels)
            {
                var ch = anim.Channels[ci];
                Assert.IsNotNull(ch.Target.Node, "joint channel must be a NATIVE TRS channel (target.Node set), not a pointer.");
                Assert.AreNotEqual("pointer", ch.Target.Path, "joint channel must not be routed through KHR_animation_pointer.");
                CollectionAssert.Contains(TrsPaths, ch.Target.Path, "native joint channel path must be a TRS path.");
                Assert.IsNull(PointerPath(ch), "a native joint channel must not carry a KHR_animation_pointer extension.");
            }
        }

        [UnityTest]
        public IEnumerator Texture_ExportsAsTextureTransformPointerChannels()
        {
            var load = SandboxTestUtil.LoadSynthetic("SC-FacePlus.glb", _created);
            yield return load;
            var hub = load.Current.GetComponent<KhrCharacter>();
            Assert.IsNotNull(hub, "SC-FacePlus should import a KhrCharacter hub.");

            CharacterLoader.ExportToGlb(hub.gameObject, out var root);
            var expr = GetExpression(root);
            Assert.IsNotNull(expr);
            var item = expr.Expressions.Find(i => i != null && i.Texture != null);
            Assert.IsNotNull(item, "SC-FacePlus has a texture expression; an item must carry KHR_character_expression_texture.");

            var anim = root.Animations[item.Animation];
            bool uvTransform = false;
            foreach (int ci in item.Texture.Channels)
            {
                var ch = anim.Channels[ci];
                Assert.AreEqual("pointer", ch.Target.Path, "texture channels export via KHR_animation_pointer.");
                var path = PointerPath(ch);
                Assert.IsNotNull(path, "the texture channel must carry a KHR_animation_pointer target extension.");
                StringAssert.StartsWith("/materials/", path);
                if (path.EndsWith("/extensions/KHR_texture_transform/scale")
                    || path.EndsWith("/extensions/KHR_texture_transform/offset")) uvTransform = true;
            }
            Assert.IsTrue(uvTransform, "the UV-transform driver must emit KHR_texture_transform scale/offset pointer channels.");

            // NOTE (G3): the 2-texture index-swap driver does NOT survive the full-disk import -> re-export round-trip
            // today (texture round-trip is a documented gap). The index-swap WIRE shape (.../baseColorTexture/index,
            // STEP) is covered by the plugin's in-memory export test; closing the full-disk index-swap round-trip is
            // tracked for Phase 4 (02 P3, G3). This test therefore locks only the UV-transform round-trip here.
        }

        [UnityTest]
        public IEnumerator ReferencePose_ExportsAsNativeTRS()
        {
            var load = SandboxTestUtil.LoadSynthetic("SC-Body.glb", _created);
            yield return load;
            var hub = load.Current.GetComponent<KhrCharacter>();
            Assert.IsNotNull(hub, "SC-Body should import a KhrCharacter hub.");

            CharacterLoader.ExportToGlb(hub.gameObject, out var root);

            // The reference pose has no expression item: it is the animation tagged with KHR_character_reference_pose.
            GLTFAnimation refAnim = null;
            if (root.Animations != null)
                foreach (var a in root.Animations)
                    if (a.Extensions != null && a.Extensions.ContainsKey(KHR_character_reference_pose.EXTENSION_NAME))
                    { refAnim = a; break; }
            Assert.IsNotNull(refAnim,
                "SC-Body re-export must tag its reference-pose animation with KHR_character_reference_pose (full-disk round-trip).");
            Assert.Greater(refAnim.Channels.Count, 0, "the reference-pose animation must carry TRS channels.");

            foreach (var ch in refAnim.Channels)
            {
                Assert.IsNotNull(ch.Target.Node, "reference-pose channel must be NATIVE TRS (target.Node set), not a pointer.");
                Assert.AreNotEqual("pointer", ch.Target.Path, "reference-pose channel must not be routed through KHR_animation_pointer.");
                CollectionAssert.Contains(TrsPaths, ch.Target.Path, "reference-pose channel path must be a TRS path.");
                Assert.IsNull(PointerPath(ch), "a native reference-pose channel must not carry a KHR_animation_pointer extension.");
            }
        }
    }
}
