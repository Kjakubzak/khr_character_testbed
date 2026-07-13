using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using UnityGLTF.KhrCharacter;
using Samples.Shared;

namespace Samples.Characters
{
    /// <summary>
    /// HumanoidAnimation demo (Tier 2 of the "animate KHR character glbs" plan). Loads a body
    /// character, switches its rig to Humanoid via <see cref="AnimationBinder"/>, and plays one
    /// of the procedural clips from <see cref="HumanoidClipFactory"/>. Proves the KHR skeleton
    /// mapping → Unity Avatar → Playables clip path end-to-end without any third-party asset.
    ///
    /// Falls back gracefully:
    /// * Humanoid rejected (bones invalid / missing) → drops to Generic mode + a clear status label.
    /// * No character loaded → tells the user which fallback fixture to generate.
    /// </summary>
    public class HumanoidAnimationController : MonoBehaviour
    {
        public string BodyGlbPath;

        private DemoUiBuilder _ui;
        private Text _status;
        private Animator _animator;
        private GameObject _character;

        private async void Start()
        {
            bool usingHero = string.IsNullOrEmpty(BodyGlbPath) && CharacterLoader.HeroExists;
            string sceneName = SceneManager.GetActiveScene().name;
            string fallbackFile = DemoCatalog.FallbackFor(sceneName, "SC-Body.glb");
            string fallbackDisplay = DemoCatalog.FallbackDisplayFor(sceneName, "SC-Body");

            _ui = DemoUiBuilder.Create("Humanoid Animation");
            _ui.AddLabel("Load a character, switch to Humanoid, play a procedural clip. Proves the KHR → Avatar → Playables path.");
            _ui.AddLabel(CharacterLoader.DemoCharacterBlurb(usingHero, fallbackDisplay));
            _status = _ui.AddLabel("Loading ...");

            var root = new GameObject("CharacterRoot");
            root.transform.SetParent(transform, false);

            GameObject scene;
            try
            {
                scene = string.IsNullOrEmpty(BodyGlbPath)
                    ? await CharacterLoader.LoadDemoCharacterAsync(root.transform, fallbackFile)
                    : await CharacterLoader.LoadAsync(BodyGlbPath, root.transform);
            }
            catch (System.Exception e) { Debug.LogException(e); _status.text = "Load failed: " + e.Message; return; }
            if (scene == null) { _status.text = "Load failed (no scene)."; return; }

            _character = scene;
            FrameScene(scene);

            // Bind Humanoid; if Unity rejects the rig, fall back to Generic (which always works —
            // it just doesn't get muscle-based retargeting).
            _animator = AnimationBinder.Bind(_character, RigMode.Humanoid);
            if (_animator == null)
            {
                _animator = AnimationBinder.Bind(_character, RigMode.Generic);
                _status.text = "Humanoid rig not available; using Generic mode. Procedural clips still play.";
            }
            else
            {
                _status.text = "Humanoid Avatar bound. Ready to play.";
            }

            BuildClipButtons();

            var back = gameObject.AddComponent<BackToHubButton>();
            _ui.AddButton("Back to Hub", back.GoToHub);
        }

        private void BuildClipButtons()
        {
            _ui.AddLabel("── Procedural clips ──");
            foreach (var clip in HumanoidClipFactory.All())
            {
                var captured = clip; // capture for closure
                _ui.AddButton(clip.name, () =>
                {
                    if (_animator == null || captured == null) return;
                    AnimationBinder.Play(_animator, captured);
                    if (_status != null) _status.text = $"Playing: {captured.name}";
                });
            }
        }

        private void FrameScene(GameObject scene)
        {
            var rig = Object.FindFirstObjectByType<OrbitCameraRig>();
            if (rig == null || scene == null) return;
            if (!SceneBoundsUtil.TryAggregate(scene, out var bounds)) return;
            rig.FrameAndFace(bounds, scene.transform);
        }
    }
}
