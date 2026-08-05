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
    /// Explicit Unity host-policy coverage. This sandbox opts into both the optional ExpressionController and clip
    /// auto-play suppression; the extension itself does not mutate clips or choose an animation host. SC-Face is
    /// all-suppressed under that selected policy.
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
            var load = SandboxTestUtil.LoadSynthetic(
                "SC-Face.glb",
                _created,
                CharacterExpressionHostPolicy.LegacyControllerWithSuppression);
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
