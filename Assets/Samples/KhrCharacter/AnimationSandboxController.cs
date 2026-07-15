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
    public class AnimationSandboxController : DemoControllerBase
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
        private bool _loading;

        // Snapshot of the loaded character's imported (bind) local TRS, restored before Bind/Play so a bad
        // pose left by a previous clip can't persist (a clip only drives the bones it targets).
        private struct BoneSnapshot { public Transform T; public Vector3 Pos; public Quaternion Rot; public Vector3 Scale; }
        private readonly List<BoneSnapshot> _bindPose = new List<BoneSnapshot>();

        private const string AgnosticProceduralLabel = "Procedural";
        private const string AdaptiveProceduralLabel = "Procedural (character-adaptive)";

        private void Start()
        {
            _ui = CreatePanel("Animation Sandbox");
            _ui.AddLabel("Any character + any rig mode + any clip. All three dropdowns are registry-backed.");
            _ui.AddLabel("Note: procedural + character-embedded clips use TRANSFORM curves — pair them with Generic mode. Humanoid mode is for muscle-format clips (Mixamo FBX etc.); pairing generic clips with Humanoid produces a mangled pose.");

            var content = new GameObject("LoadedContent");
            content.transform.SetParent(transform, false);
            _contentRoot = content.transform;

            _characterDropdown = _ui.AddDropdown("Character", BuildCharacterOptions(), _ => { }, 0);
            _ui.AddButton("Load character", async () => await LoadCurrentCharacter());
            _loaded = _ui.AddLabel("(no character loaded)");

            // Default to Generic and re-filter clips when the mode changes: the shipped clips are all
            // transform/expression curves that only play correctly in Generic; Humanoid needs muscle-format
            // clips. Filtering by mode removes the "generic clip in Humanoid = mangled pose" footgun.
            _modeDropdown = _ui.AddDropdown("Rig mode", BuildModeOptions(), _ => RefreshClipDropdown(), DefaultModeIndex());
            _ui.AddButton("Bind rig", BindCurrentMode);

            _clipDropdown = _ui.AddDropdown("Clip", BuildClipOptions(), _ => { }, 0);
            _ui.AddButton("Play clip", PlayCurrentClip);
            _ui.AddButton("Refresh clip list", RefreshClipDropdown);

            _status = _ui.AddLabel(string.Empty);

            Caveats.Render(_ui, Caveat.Draft, Caveat.CubicSplineToLinear);
        }

        // ── Dropdown builders ────────────────────────────────────────────

        private List<string> BuildCharacterOptions()
        {
            var options = new List<string>();
            _characterPaths.Clear();

            foreach (var (label, path) in CharacterLoader.EnumerateHeroFiles())
            {
                options.Add(label);
                _characterPaths.Add(path);
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

            var mode = SelectedMode();
            // Hide the character-agnostic "Procedural" source once a character-adaptive one exists (a KHR
            // character is loaded): its vocab bone paths don't target the loaded rig, so it would no-op. Detect it
            // from the Sources LABELS (not a full EnumerateAll pass) so we don't build/enumerate every clip twice
            // per dropdown rebuild (the adaptive builds are cached per character in HumanoidClipFactory).
            bool hasAdaptive = false;
            foreach (var s in AnimationClipCatalog.Sources)
                if (s.Label == AdaptiveProceduralLabel) { hasAdaptive = true; break; }

            foreach (var (source, clip) in AnimationClipCatalog.EnumerateAll())
            {
                if (clip == null) continue;
                if (hasAdaptive && source.Label == AgnosticProceduralLabel) continue;
                if (!ClipPlaysInMode(clip, mode)) continue; // only offer clips that can actually play in this mode
                options.Add($"{source.Label}: {clip.name}");
                _clips.Add(clip);
            }
            if (options.Count == 0)
                options.Add(mode == RigMode.Humanoid
                    ? "(no muscle-format clips — Humanoid needs Mixamo-style clips; use Generic for the shipped ones)"
                    : "(no clips discovered — check AnimationClipCatalog)");
            return options;
        }

        // Coherent (mode × legacy × humanMotion) filter so no dropdown entry is offered that would silently no-op:
        //   Legacy   → legacy clips only (driven by the Animation component)
        //   Humanoid → muscle-format (humanMotion) clips only (retargeted through the Mecanim Avatar)
        //   Generic  → transform-curve clips only (neither legacy nor humanMotion)
        private static bool ClipPlaysInMode(AnimationClip clip, RigMode mode)
        {
            switch (mode)
            {
                case RigMode.Legacy: return clip.legacy;
                case RigMode.Humanoid: return clip.humanMotion;
                default: return !clip.legacy && !clip.humanMotion; // Generic
            }
        }

        private RigMode SelectedMode()
        {
            int i = _modeDropdown != null ? _modeDropdown.value : -1;
            return (i >= 0 && i < _modeValues.Count) ? _modeValues[i] : RigMode.Generic;
        }

        private int DefaultModeIndex()
        {
            int i = _modeValues.IndexOf(RigMode.Generic);
            return i >= 0 ? i : 0;
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
            if (_loading) return;
            _loading = true;
            try
            {
                int idx = _characterDropdown.value;
                if (idx < 0 || idx >= _characterPaths.Count)
                {
                    _status.text = "No character selection.";
                    return;
                }
                string path = _characterPaths[idx];
                string problem = CharacterLoader.DescribeUnloadable(path);
                if (problem != null)
                {
                    _status.text = problem;
                    return;
                }

                // Tear down previous character before loading the next.
                if (_currentCharacter != null)
                {
                    // Also drop the per-character clip sources we registered last time (if any).
                    AnimationClipCatalog.Remove("Character");
                    AnimationClipCatalog.Remove("Procedural (character-adaptive)");
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
                if (this == null) return; // scene changed / object destroyed mid-import
                if (_currentCharacter == null) { _status.text = "Load failed (no scene)."; return; }

                CaptureBindPose(_currentCharacter);
                FrameScene(_currentCharacter);
                _loaded.text = $"Loaded: {System.IO.Path.GetFileName(path)}";
                _status.text = "Character loaded. Rig mode defaults to Generic (right for the shipped clips). Bind + Play.";

                // Register a per-character clip source so its embedded animations flow into the clip
                // dropdown. Removed on the next character load (see above).
                var captured = _currentCharacter;
                AnimationClipCatalog.TryRegister("Character",
                    () => AnimationBinder.EnumerateCharacterClips(captured), autoDetected: false);

                // Register a per-character PROCEDURAL source that resolves paths from the KHR
                // skeleton_mapping — so procedural clips actually drive the loaded character's bones
                // regardless of its naming convention (VRoid, Mixamo, PascalCase, etc.). Removed on
                // next load. The character-agnostic "Procedural" source stays in the catalog too;
                // this per-character one adds character-adaptive variants of the same clips.
                // Adaptive clips are cached per-character in HumanoidClipFactory and first built during the
                // RefreshClipDropdown below — i.e. at the bind pose just captured above — so later dropdown
                // rebuilds reuse them and never re-bake bind offsets against a displaced (mid-clip) pose.
                AnimationClipCatalog.TryRegister("Procedural (character-adaptive)",
                    () => HumanoidClipFactory.AllForCharacter(captured), autoDetected: false);

                RefreshModeDropdown();
                if (_modeDropdown != null) _modeDropdown.SetValueWithoutNotify(DefaultModeIndex()); // prefer Generic
                RefreshClipDropdown();
            }
            finally { _loading = false; }
        }

        private void BindCurrentMode()
        {
            if (_currentCharacter == null) { _status.text = "Load a character first."; return; }
            ResetToBindPose(); // clear any pose left by a previous clip so binding starts clean
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
            ResetToBindPose(); // start each clip from the bind pose so undriven bones don't keep a stale pose

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

        private void CaptureBindPose(GameObject character)
        {
            _bindPose.Clear();
            if (character == null) return;
            foreach (var t in character.GetComponentsInChildren<Transform>(true))
                _bindPose.Add(new BoneSnapshot { T = t, Pos = t.localPosition, Rot = t.localRotation, Scale = t.localScale });
        }

        private void ResetToBindPose()
        {
            foreach (var s in _bindPose)
                if (s.T != null) { s.T.localPosition = s.Pos; s.T.localRotation = s.Rot; s.T.localScale = s.Scale; }
        }

        // Drop the per-character clip sources we registered on load so their Func closures over
        // _currentCharacter don't keep enumerating from a dead GameObject after the scene
        // unloads. Idempotent — Remove no-ops if the source isn't registered.
        private void OnDestroy()
        {
            AnimationClipCatalog.Remove("Character");
            AnimationClipCatalog.Remove("Procedural (character-adaptive)");
        }
    }
}
