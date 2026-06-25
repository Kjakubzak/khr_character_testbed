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
        // The committed demo scenes (must match EditorBuildSettings / SceneBuilder output).
        private static readonly string[] SceneNames =
        {
            "SampleHub", "GlbViewer", "Expressions", "GazeAndCamera", "RigAndPose", "RoundTrip", "Health",
        };

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
