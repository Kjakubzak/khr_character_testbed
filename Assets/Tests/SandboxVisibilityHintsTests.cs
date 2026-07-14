using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityGLTF.VisibilityHints;

namespace KhrCharacterTestbed.Tests
{
    /// <summary>
    /// Behavioral smoke test for the VisibilityHints demo scene: it boots with a <see cref="ViewContextController"/>
    /// and both hint-set components, and flipping the view context actually toggles a hinted renderer. Anti-hollow —
    /// asserts via the real plugin types and observed renderer state, not just "a scene with a camera". Cleanup runs
    /// in [UnityTearDown] so a failed assert never leaks the additively-loaded scene.
    /// </summary>
    public class SandboxVisibilityHintsTests
    {
        private Scene _loaded;

        [UnityTearDown]
        public IEnumerator Cleanup()
        {
            if (_loaded.IsValid() && _loaded.isLoaded)
                yield return SceneManager.UnloadSceneAsync(_loaded);
            _loaded = default;
        }

        [UnityTest]
        public IEnumerator VisibilityHintsScene_TogglesRendererByViewContext()
        {
            yield return SceneManager.LoadSceneAsync("VisibilityHints", LoadSceneMode.Additive);
            var scene = SceneManager.GetSceneByName("VisibilityHints");
            _loaded = scene; // tracked for [UnityTearDown] before any assert that could throw
            Assert.IsTrue(scene.IsValid() && scene.isLoaded, "the VisibilityHints demo scene should load additively.");

            // The controller builds the figure in Start(); wait until the plugin components exist (capped).
            ViewContextController view = null;
            NodeVisibilityHintSet nodeSet = null;
            PrimitiveVisibilityHintSet primSet = null;
            float deadline = Time.realtimeSinceStartup + 4f;
            while (Time.realtimeSinceStartup < deadline)
            {
                FindComponents(scene, out view, out nodeSet, out primSet);
                if (view != null && nodeSet != null && primSet != null) break;
                yield return null;
            }

            Assert.IsNotNull(view, "the demo should add a ViewContextController.");
            Assert.IsNotNull(nodeSet, "the demo should add a NodeVisibilityHintSet.");
            Assert.IsNotNull(primSet, "the demo should add a PrimitiveVisibilityHintSet.");

            // Head is hinted third_person: visible in the default ThirdPerson context, hidden in FirstPerson.
            var head = FindDescendant(view.transform, "Head");
            Assert.IsNotNull(head, "the demo figure should contain a 'Head' part.");
            var headRenderer = head.GetComponent<Renderer>();
            Assert.IsNotNull(headRenderer, "the 'Head' part should have a Renderer.");

            Assert.AreEqual(ViewContextController.ViewContext.ThirdPerson, view.Mode, "demo should start in ThirdPerson.");
            Assert.IsTrue(headRenderer.enabled, "third_person head should be visible in ThirdPerson.");

            view.Mode = ViewContextController.ViewContext.FirstPerson;
            yield return null;
            Assert.IsFalse(headRenderer.enabled, "third_person head should hide in FirstPerson.");

            view.Mode = ViewContextController.ViewContext.ThirdPerson;
            yield return null;
            Assert.IsTrue(headRenderer.enabled, "third_person head should restore in ThirdPerson.");
        }

        private static void FindComponents(Scene scene, out ViewContextController view,
            out NodeVisibilityHintSet nodeSet, out PrimitiveVisibilityHintSet primSet)
        {
            view = null; nodeSet = null; primSet = null;
            foreach (var root in scene.GetRootGameObjects())
            {
                if (view == null) view = root.GetComponentInChildren<ViewContextController>(true);
                if (nodeSet == null) nodeSet = root.GetComponentInChildren<NodeVisibilityHintSet>(true);
                if (primSet == null) primSet = root.GetComponentInChildren<PrimitiveVisibilityHintSet>(true);
            }
        }

        private static Transform FindDescendant(Transform root, string name)
        {
            foreach (var t in root.GetComponentsInChildren<Transform>(true))
                if (t.name == name) return t;
            return null;
        }
    }
}
