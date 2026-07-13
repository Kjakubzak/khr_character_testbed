using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using UnityGLTF.KhrCharacter;
using Samples.Shared;

namespace Samples.Characters
{
    /// <summary>
    /// AnimationSandbox demo (Tier 4). The fully-generalized "hook up any target mapping + any
    /// clip to any character" scene. Three dropdowns:
    ///
    ///   1. Character   — every glb across every <see cref="CharacterLoader.AssetSourceCatalog"/>
    ///                    source (Synthetic + FromBlender + user-added).
    ///   2. Rig mode    — every <see cref="RigMode"/> value (Humanoid / Generic / Legacy),
    ///                    filtered by <see cref="AnimationBinder.IsSupported"/> for the loaded
    ///                    character.
    ///   3. Clip        — every clip across every <see cref="AnimationClipCatalog"/> source
    ///                    (Procedural + Resources + character-embedded clips discovered from
    ///                    the currently-loaded glb).
    ///
    /// Load button rebuilds the scene from the current character selection. Bind button
    /// re-binds the animator to the current rig mode. Play button plays the current clip. This
    /// exists specifically so a developer can try any combination without recompiling; every
    /// data-source is registry-backed so adding a new character / mapping / clip is a
    /// runtime-only change from the corresponding catalog UI, no code touch here.
    /// </summary>
    public class AnimationSandboxController : MonoBehaviour
    {
        private DemoUiBuilder _ui;
        private Text _status;
        private Text _loaded;
        private Dropdown _characterDropdown;
        private Dropdown _modeDropdown;
        private Dropdown _clipDropdown;

        private readonly List<string> _characterPaths = new List<string>();
        private readonly List<RigMode> _modeValues = new List<RigMode>();
        private readonly List<AnimationClip> _clips = new List<AnimationClip>();

        private Transform _contentRoot;
        private GameObject _currentCharacter;
        private Animator _currentAnimator;

        private void Start()
        {
            _ui = DemoUiBuilder.Create("Animation Sandbox");
            _ui.AddLabel("Any character + any rig mode + any clip. All three dropdowns are registry-backed.");

            var content = new GameObject("LoadedContent");
            content.transform.SetParent(transform, false);
            _contentRoot = content.transform;

            _characterDropdown = _ui.AddDropdown("Character", BuildCharacterOptions(), _ => { }, 0);
            _ui.AddButton("Load character", async () => await LoadCurrentCharacter());
            _loaded = _ui.AddLabel("(no character loaded)");

            _modeDropdown = _ui.AddDropdown("Rig mode", BuildModeOptions(), _ => { }, 0);
            _ui.AddButton("Bind rig", BindCurrentMode);

            _clipDropdown = _ui.AddDropdown("Clip", BuildClipOptions(), _ => { }, 0);
            _ui.AddButton("Play clip", PlayCurrentClip);
            _ui.AddButton("Refresh clip list", RefreshClipDropdown);

            _status = _ui.AddLabel(string.Empty);

            var back = gameObject.AddComponent<BackToHubButton>();
            _ui.AddButton("Back to Hub", back.GoToHub);
        }

        // ── Dropdown builders ────────────────────────────────────────────

        private List<string> BuildCharacterOptions()
        {
            var options = new List<string>();
            _characterPaths.Clear();

            if (CharacterLoader.HeroExists)
            {
                options.Add("Hero: khr-character-example");
                _characterPaths.Add(CharacterLoader.HeroAbsolutePath);
            }
            foreach (var (source, path) in CharacterLoader.AssetSourceCatalog.EnumerateAll())
            {
                options.Add($"{source.Label}: {System.IO.Path.GetFileNameWithoutExtension(path)}");
                _characterPaths.Add(path);
            }
            if (options.Count == 0)
                options.Add("(no characters discovered — add a source folder in GlbViewer)");
            return options;
        }

        private List<string> BuildModeOptions()
        {
            var options = new List<string>();
            _modeValues.Clear();
            foreach (RigMode mode in System.Enum.GetValues(typeof(RigMode)))
            {
                bool supported = _currentCharacter == null || AnimationBinder.IsSupported(_currentCharacter, mode);
                options.Add(supported ? mode.ToString() : $"{mode} (unsupported)");
                _modeValues.Add(mode);
            }
            return options;
        }

        private List<string> BuildClipOptions()
        {
            var options = new List<string>();
            _clips.Clear();
            foreach (var (source, clip) in AnimationClipCatalog.EnumerateAll())
            {
                options.Add($"{source.Label}: {clip.name}");
                _clips.Add(clip);
            }
            if (options.Count == 0)
                options.Add("(no clips discovered — check AnimationClipCatalog)");
            return options;
        }

        private void RefreshClipDropdown()
        {
            if (_clipDropdown == null) return;
            _clipDropdown.ClearOptions();
            _clipDropdown.AddOptions(BuildClipOptions());
        }

        private void RefreshModeDropdown()
        {
            if (_modeDropdown == null) return;
            _modeDropdown.ClearOptions();
            _modeDropdown.AddOptions(BuildModeOptions());
        }

        // ── Actions ──────────────────────────────────────────────────────

        private async System.Threading.Tasks.Task LoadCurrentCharacter()
        {
            int idx = _characterDropdown.value;
            if (idx < 0 || idx >= _characterPaths.Count)
            {
                _status.text = "No character selection.";
                return;
            }
            string path = _characterPaths[idx];
            if (!System.IO.File.Exists(path))
            {
                _status.text = $"Missing: {path}";
                return;
            }

            // Tear down previous character before loading the next.
            if (_currentCharacter != null)
            {
                // Also drop the per-character clip source we registered last time (if any).
                AnimationClipCatalog.Remove("Character");
                Destroy(_currentCharacter);
                _currentCharacter = null;
                _currentAnimator = null;
            }

            _status.text = $"Loading {System.IO.Path.GetFileName(path)} ...";
            try
            {
                _currentCharacter = await CharacterLoader.LoadAsync(path, _contentRoot);
            }
            catch (System.Exception e) { Debug.LogException(e); _status.text = "Load failed: " + e.Message; return; }
            if (_currentCharacter == null) { _status.text = "Load failed (no scene)."; return; }

            FrameScene(_currentCharacter);
            _loaded.text = $"Loaded: {System.IO.Path.GetFileName(path)}";
            _status.text = "Character loaded. Pick a rig mode + clip and press Bind + Play.";

            // Register a per-character clip source so its embedded animations flow into the clip
            // dropdown. Removed on the next character load (see above).
            var captured = _currentCharacter;
            AnimationClipCatalog.TryRegister("Character",
                () => AnimationBinder.EnumerateCharacterClips(captured), autoDetected: false);

            RefreshModeDropdown();
            RefreshClipDropdown();
        }

        private void BindCurrentMode()
        {
            if (_currentCharacter == null) { _status.text = "Load a character first."; return; }
            int idx = _modeDropdown.value;
            if (idx < 0 || idx >= _modeValues.Count) return;
            var mode = _modeValues[idx];

            if (mode == RigMode.Legacy)
            {
                // Legacy uses Animation, not Animator. No animator to store.
                var animation = AnimationBinder.GetLegacyAnimation(_currentCharacter);
                _currentAnimator = null;
                _status.text = animation != null
                    ? "Bound: Legacy Animation. Use Play clip; the binder plays via Animation.AddClip."
                    : "Legacy bind returned null (unexpected).";
                return;
            }

            _currentAnimator = AnimationBinder.Bind(_currentCharacter, mode);
            _status.text = _currentAnimator != null
                ? $"Bound: {mode}. Ready to play."
                : $"Bind failed for {mode} (character may not support it).";
        }

        private void PlayCurrentClip()
        {
            if (_currentCharacter == null) { _status.text = "Load a character first."; return; }
            int idx = _clipDropdown.value;
            if (idx < 0 || idx >= _clips.Count) { _status.text = "No clip selection."; return; }
            var clip = _clips[idx];
            if (clip == null) { _status.text = "Clip is null."; return; }

            int modeIdx = _modeDropdown.value;
            var mode = (modeIdx >= 0 && modeIdx < _modeValues.Count) ? _modeValues[modeIdx] : RigMode.Generic;

            if (mode == RigMode.Legacy)
            {
                AnimationBinder.PlayLegacy(_currentCharacter, clip);
                _status.text = $"Playing (Legacy): {clip.name}";
                return;
            }

            if (_currentAnimator == null)
            {
                // Auto-bind if the user hit Play before Bind.
                _currentAnimator = AnimationBinder.Bind(_currentCharacter, mode);
                if (_currentAnimator == null)
                {
                    _status.text = $"Cannot play — mode {mode} not supported by this character.";
                    return;
                }
            }
            AnimationBinder.Play(_currentAnimator, clip);
            _status.text = $"Playing ({mode}): {clip.name}";
        }

        private void FrameScene(GameObject scene)
        {
            var rig = Object.FindFirstObjectByType<OrbitCameraRig>();
            if (rig == null || scene == null) return;
            if (!SceneBoundsUtil.TryAggregate(scene, out var bounds)) return;
            rig.FrameAndFace(bounds, scene.transform);
        }

        // Drop the per-character clip source we registered on load so its Func closure over
        // _currentCharacter doesn't keep enumerating from a dead GameObject after the scene
        // unloads. Idempotent — Remove no-ops if the source isn't registered.
        private void OnDestroy()
        {
            AnimationClipCatalog.Remove("Character");
        }
    }
}
