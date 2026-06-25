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
    /// Clip auto-play suppression on a real imported scene (02 anim lens, Phase 3-P2, P-I1). The imported expression
    /// clips must NOT auto-play (they would otherwise double-drive the runtime ExpressionController). SC-Face is
    /// all-suppressed (every animation is an expression clip), so the legacy Animation host must not be playing and
    /// every registered clip carries wrapMode Once (not the Loop default). Anti-hollow via real plugin types.
    /// </summary>
    public class SandboxSuppressionTests
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
        public IEnumerator ExpressionClips_DoNotAutoPlayAfterImport()
        {
            var load = SandboxTestUtil.LoadSynthetic("SC-Face.glb", _created);
            yield return load;
            var scene = load.Current;
            var hub = scene.GetComponent<KhrCharacter>();
            Assert.IsNotNull(hub, "SC-Face should import a KhrCharacter hub.");

            var animation = scene.GetComponentInChildren<Animation>(true);
            Assert.IsNotNull(animation,
                "SC-Face has expression animations, so the runtime import should create a legacy Animation host.");

            // Let a live frame pass so any auto-play would have started by now.
            if (!scene.activeInHierarchy) scene.SetActive(true);
            yield return null;

            Assert.IsFalse(animation.isPlaying, "imported expression clips must NOT auto-play (P-I1 suppression).");

            // SC-Face is all-suppressed: every registered expression clip must be wrapMode Once, not the Loop default.
            int clipCount = 0;
            foreach (AnimationState state in animation)
            {
                clipCount++;
                Assert.AreEqual(WrapMode.Once, state.clip.wrapMode,
                    $"suppressed expression clip '{state.clip.name}' must be wrapMode Once, not the Loop default.");
            }
            Assert.Greater(clipCount, 0, "SC-Face should register its expression clips on the Animation host.");
        }
    }
}
