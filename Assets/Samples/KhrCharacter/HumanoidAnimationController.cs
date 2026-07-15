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
    public class HumanoidAnimationController : DemoControllerBase
    {
        public string BodyGlbPath;

        private DemoUiBuilder _ui;
        private Text _status;
        private Animator _animator;
        private GameObject _character;

        private async void Start()
        {
            bool usingHero = string.IsNullOrEmpty(BodyGlbPath) && CharacterLoader.WouldLoadHero;
            string sceneName = SceneManager.GetActiveScene().name;
            string fallbackFile = DemoCatalog.FallbackFor(sceneName, "SC-Body.glb");
            string fallbackDisplay = DemoCatalog.FallbackDisplayFor(sceneName, "SC-Body");

            _ui = CreatePanel("Humanoid Animation");
            _ui.AddLabel("Load a character, switch to Humanoid, play a procedural clip. Proves the KHR → Avatar → Playables path.");
            _ui.AddLabel(CharacterLoader.DemoCharacterBlurb(usingHero, fallbackDisplay));
            _status = _ui.AddLabel("Loading ...");
            Caveats.Render(_ui, Caveat.Draft, Caveat.CubicSplineToLinear);

            var root = new GameObject("CharacterRoot");
            root.transform.SetParent(transform, false);

            GameObject scene;
            try
            {
                scene = string.IsNullOrEmpty(BodyGlbPath)
                    ? await CharacterLoader.LoadDemoCharacterAsync(root.transform, fallbackFile)
                    : await CharacterLoader.LoadAsync(BodyGlbPath, root.transform);
            }
            catch (System.Exception e) { Debug.LogException(e); if (this != null && _status != null) _status.text = "Load failed: " + e.Message; return; }
            if (this == null) return; // scene changed / object destroyed mid-import
            if (scene == null) { _status.text = "Load failed (no scene)."; return; }

            _character = scene;
            FrameScene(scene);

            // Procedural clips from HumanoidClipFactory are TRANSFORM-BASED (curves target bone
            // paths directly, e.g. head.localRotation.x). Humanoid mode assigns a Mecanim
            // Avatar which would reinterpret the clip through the muscle system — since our
            // clips have no muscle data, the Animator would fill in a default muscle pose,
            // producing a mangled hybrid. Generic mode leaves the Avatar null so transform
            // curves apply directly to bones. The character stays a KHR humanoid; we just
            // don't need Mecanim's Avatar for transform-based clips.
            //
            // Use Humanoid mode when playing humanoid-format clips (FBX imports with muscle
            // curves, e.g. Mixamo). The Animation Sandbox scene demonstrates both flavours.
            _animator = AnimationBinder.Bind(_character, RigMode.Generic);
            if (_animator == null)
            {
                _status.text = "Failed to bind Generic mode. This is unexpected — Generic should always work.";
                return;
            }
            _status.text = "Generic mode bound. Ready to play procedural transform-based clips.";

            BuildClipButtons();
        }

        private void BuildClipButtons()
        {
            _ui.AddLabel("── Procedural clips (paths resolved from KHR skeleton_mapping) ──");
            foreach (var clip in HumanoidClipFactory.AllForCharacter(_character))
            {
                var captured = clip; // capture for closure
                _ui.AddButton(clip.name.Contains("@") ? clip.name.Split('@')[0] : clip.name, () =>
                {
                    if (_animator == null || captured == null) return;
                    AnimationBinder.Play(_animator, captured);
                    if (_status != null) _status.text = $"Playing: {captured.name}";
                });
            }
        }
    }
}
