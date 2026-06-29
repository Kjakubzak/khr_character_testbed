using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Samples.Shared;

namespace KhrCharacterTestbed.Tests
{
    /// <summary>
    /// PlayMode coverage for <see cref="AssetVisualizer"/> on real imported fixtures: toggling
    /// <see cref="AssetVisualizer.ShowBounds"/> creates/destroys a bounds <see cref="LineRenderer"/> (SC-Face has
    /// renderers), toggling <see cref="AssetVisualizer.ShowSkeleton"/> creates/destroys skeleton lines (SC-Body has
    /// a mapped skeleton), and <see cref="AssetVisualizer.Wireframe"/> flips its flag without throwing. The
    /// LineRenderers draw under both pipelines; only wireframe is pipeline-sensitive (exercised flag-only here).
    /// </summary>
    public class AssetVisualizerTests
    {
        private readonly List<Object> _created = new List<Object>();

        [TearDown]
        public void TearDown()
        {
            GL.wireframe = false; // never leak the global flag between cases
            foreach (var o in _created)
                if (o != null) Object.DestroyImmediate(o);
            _created.Clear();
        }

        private AssetVisualizer NewVisualizer()
        {
            var go = new GameObject("AssetVisualizerUnderTest", typeof(AssetVisualizer));
            _created.Add(go);
            return go.GetComponent<AssetVisualizer>();
        }

        [UnityTest]
        public IEnumerator Wireframe_FlipsFlag_WithoutThrowing()
        {
            var viz = NewVisualizer();
            yield return null;

            viz.Wireframe = true;
            Assert.IsTrue(viz.Wireframe, "Wireframe should report enabled.");
            yield return null;

            viz.Wireframe = false;
            Assert.IsFalse(viz.Wireframe, "Wireframe should report disabled.");
        }

        [UnityTest]
        public IEnumerator ShowBounds_CreatesAndDestroysLine()
        {
            var load = SandboxTestUtil.LoadSynthetic("SC-Face.glb", _created);
            yield return load;

            var viz = NewVisualizer();
            yield return null; // viz.Update binds the character root from the loaded KhrCharacter

            viz.ShowBounds = true;
            yield return null; // LateUpdate builds the bounds box

            Assert.IsNotNull(viz.GetComponentInChildren<LineRenderer>(), "ShowBounds should create a bounds LineRenderer.");

            viz.ShowBounds = false;
            yield return null; // Destroy() resolves at end of frame

            Assert.IsNull(viz.GetComponentInChildren<LineRenderer>(), "clearing ShowBounds should destroy the bounds line.");
        }

        [UnityTest]
        public IEnumerator ShowSkeleton_CreatesAndDestroysLines()
        {
            var load = SandboxTestUtil.LoadSynthetic("SC-Body.glb", _created);
            yield return load;

            var viz = NewVisualizer();
            yield return null; // viz.Update binds the loaded character (and its SkeletonMap)

            viz.ShowSkeleton = true;
            yield return null;

            Assert.Greater(viz.GetComponentsInChildren<LineRenderer>().Length, 0,
                "ShowSkeleton should create skeleton LineRenderers for a mapped rig.");

            viz.ShowSkeleton = false;
            yield return null;

            Assert.AreEqual(0, viz.GetComponentsInChildren<LineRenderer>().Length,
                "clearing ShowSkeleton should destroy the skeleton lines.");
        }
    }
}
