using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityGLTF.KhrCharacter;
using Samples.Shared;

namespace KhrCharacterTestbed.Tests
{
    /// <summary>
    /// Phase 5 (optional polish): demo-scene smoke tests. Additively loads each built demo scene with the hero forced
    /// off (CharacterLoader.ForceSyntheticForTests) so boot is fast + deterministic, lets it settle, then asserts a
    /// clean boot: the scene loaded with root objects + a Camera, and every static renderer has a real shader (never
    /// the magenta "Hidden/InternalErrorShader"). Catches scene-wiring / shader regressions a wire test can't. Cleanup
    /// runs in [UnityTearDown] (which executes even on a failed assert), so a failure never leaks the additively-loaded
    /// scene into the next [ValueSource] case. Anti-hollow via real plugin types.
    /// </summary>
    public class SandboxSceneSmokeTests
    {
        // Every scene declared in DemoCatalog.All. Catalog-driven — adding a new demo entry auto-
        // enrols it in this smoke suite via NUnit's [ValueSource] test-collection discovery.
        // (Old behaviour: hardcoded 9-scene string array duplicating what SceneBuilder + the hub
        // registry already knew. Removed as part of the DemoCatalog generalization.)
        public static string[] SceneNames()
        {
            var names = new System.Collections.Generic.List<string>();
            foreach (var d in DemoCatalog.All) names.Add(d.SceneName);
            return names.ToArray();
        }

        // Anti-hollow floor for the [ValueSource] above: an empty DemoCatalog yields "no cases" (NOT a failure), so a
        // separate lower-bound assertion fails LOUDLY if the demo catalog collapses. DemoCatalog.All defines 12 scenes.
        [Test]
        public void Catalog_DeclaresScenes_AboveLowerBound()
        {
            var scenes = SceneNames();
            Assert.GreaterOrEqual(scenes.Length, 10,
                $"DemoCatalog declared only {scenes.Length} scene(s); expected the full demo catalog (>=12). A near-" +
                "empty catalog would silently hollow out the demo-scene boot smoke gate.");
        }

        private Scene _loaded;

        // Runs even when the test body throws (unlike code placed after a yield-bearing try), guaranteeing the
        // additively-loaded scene is unloaded and the global flag is reset between cases.
        [UnityTearDown]
        public IEnumerator Cleanup()
        {
            CharacterLoader.ForceSyntheticForTests = false;
            if (_loaded.IsValid() && _loaded.isLoaded)
                yield return SceneManager.UnloadSceneAsync(_loaded);
            _loaded = default;
        }

        private static bool HasKhrCharacter(Scene scene)
        {
            foreach (var root in scene.GetRootGameObjects())
                if (root.GetComponentInChildren<KhrCharacter>(true) != null) return true;
            return false;
        }

        [UnityTest]
        public IEnumerator DemoScene_BootsCleanly([ValueSource(nameof(SceneNames))] string sceneName)
        {
            CharacterLoader.ForceSyntheticForTests = true;

            yield return SceneManager.LoadSceneAsync(sceneName, LoadSceneMode.Additive);
            var scene = SceneManager.GetSceneByName(sceneName);
            _loaded = scene;   // tracked for [UnityTearDown] before any assert that could throw
            Assert.IsTrue(scene.IsValid() && scene.isLoaded, $"demo scene '{sceneName}' should load additively.");

            // Boot, and let any forced-synthetic character load settle so unload is clean (capped; scenes without a
            // character just hit the cap, which is fine).
            float deadline = Time.realtimeSinceStartup + 4f;
            while (Time.realtimeSinceStartup < deadline && !HasKhrCharacter(scene)) yield return null;
            yield return null;
            yield return null;

            var roots = scene.GetRootGameObjects();
            Assert.Greater(roots.Length, 0, $"'{sceneName}' should contain root objects after boot.");

            bool hasCamera = false;
            foreach (var root in roots)
                if (root.GetComponentInChildren<Camera>(true) != null) { hasCamera = true; break; }
            Assert.IsTrue(hasCamera, $"'{sceneName}' should contain a Camera.");

            // Shader-clean: no static renderer may fall back to the magenta error shader.
            foreach (var root in roots)
                foreach (var r in root.GetComponentsInChildren<Renderer>(true))
                    foreach (var m in r.sharedMaterials)
                    {
                        if (m == null) continue;   // an unassigned slot is not an error-shader failure
                        Assert.IsNotNull(m.shader, $"'{sceneName}': material on '{r.name}' has a null shader.");
                        Assert.AreNotEqual("Hidden/InternalErrorShader", m.shader.name,
                            $"'{sceneName}': renderer '{r.name}' uses the magenta error shader (broken material).");
                    }
        }
    }
}
