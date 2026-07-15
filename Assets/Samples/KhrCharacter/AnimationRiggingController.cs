using UnityEngine;
using UnityEngine.Animations.Rigging;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using UnityGLTF.KhrCharacter;
using Samples.Shared;

namespace Samples.Characters
{
    /// <summary>
    /// AnimationRigging demo (Tier 3). Stacks Unity Animation Rigging constraints ON TOP of a
    /// procedural humanoid clip: an <see cref="AnimationBinder"/>-driven idle loop provides the
    /// base motion, and a <see cref="MultiAimConstraint"/> makes the head bone track a movable
    /// target. Layered animation — the character breathes / walks in place while its head follows
    /// where you point. The KHR gaze target (if the character has one) is used as the aim source
    /// so this pairs naturally with the GazeAndCamera demo's affordances.
    ///
    /// Requires <c>com.unity.animation.rigging</c> (added to Packages/manifest.json). Falls back
    /// gracefully to base playback (no aim) when the character has no head bone the constraint
    /// can bind to.
    /// </summary>
    public class AnimationRiggingController : DemoControllerBase
    {
        public string BodyGlbPath;

        private DemoUiBuilder _ui;
        private Text _status;
        private Animator _animator;
        private GameObject _character;
        private Transform _aimTarget;
        private RigBuilder _rigBuilder;

        private async void Start()
        {
            bool usingHero = string.IsNullOrEmpty(BodyGlbPath) && CharacterLoader.WouldLoadHero;
            string sceneName = SceneManager.GetActiveScene().name;
            string fallbackFile = DemoCatalog.FallbackFor(sceneName, "SC-Body.glb");
            string fallbackDisplay = DemoCatalog.FallbackDisplayFor(sceneName, "SC-Body");

            _ui = CreatePanel("Animation Rigging");
            _ui.AddLabel("Layer a MultiAimConstraint (head look-at) on top of a base humanoid clip.");
            _ui.AddLabel(CharacterLoader.DemoCharacterBlurb(usingHero, fallbackDisplay));
            _status = _ui.AddLabel("Loading ...");
            Caveats.Render(_ui, Caveat.Draft);

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
            if (scene == null) { _status.text = "Load failed."; return; }

            _character = scene;
            FrameScene(scene);

            // Bind Generic — same reasoning as HumanoidAnimationController: our procedural
            // idle clip is transform-based, and playing it on a Humanoid Avatar produces a
            // mangled pose. MultiAimConstraint (below) works fine on the Generic rig — it
            // constrains transforms directly, no Avatar dependency.
            _animator = AnimationBinder.Bind(_character, RigMode.Generic);
            if (_animator == null)
            {
                _status.text = "No animator could be bound. Check character load.";
                return;
            }

            // The aim target — an Empty the user can move around via a runtime widget. The widget
            // moves the GameObject it's attached to, so the target IS the widget's transform.
            var targetGo = new GameObject("AimTarget");
            targetGo.transform.SetParent(transform, false);
            targetGo.transform.position = new Vector3(0.5f, 1.6f, 1.5f);
            _aimTarget = targetGo.transform;
            targetGo.AddComponent<RuntimeMoveWidget>();

            SetupRig();

            // Start the idle so the aim constraint has something to layer on top of. Uses the
            // per-character clip so paths resolve from the KHR skeleton_mapping (works on VRoid /
            // Mixamo / custom rigs, not just SC-Body's PascalCase convention).
            AnimationBinder.Play(_animator, HumanoidClipFactory.BuildForCharacter("IdleSway", _character));
            _status.text = _rigBuilder != null
                ? "Idle base + head aim active. Move the yellow target."
                : "Idle base playing; head aim disabled (no head bone found).";

            BuildClipButtons();
        }

        // Wire a MultiAimConstraint on the character's head bone. The rig builder / rig / constraint
        // trio is Unity Animation Rigging's runtime setup pattern.
        private void SetupRig()
        {
            var headBone = FindHeadBone(_character);
            if (headBone == null) return; // graceful skip; base clip still plays

            _rigBuilder = _character.GetComponent<RigBuilder>() ?? _character.AddComponent<RigBuilder>();

            var rigGo = new GameObject("AimRig");
            rigGo.transform.SetParent(_character.transform, false);
            var rig = rigGo.AddComponent<Rig>();

            var constraintGo = new GameObject("HeadAim");
            constraintGo.transform.SetParent(rigGo.transform, false);
            var aim = constraintGo.AddComponent<MultiAimConstraint>();
            var data = aim.data;
            data.constrainedObject = headBone;
            var sources = new WeightedTransformArray(0);
            sources.Add(new WeightedTransform(_aimTarget, 1f));
            data.sourceObjects = sources;
            data.aimAxis = MultiAimConstraintData.Axis.Z;   // most humanoid faces point +Z
            data.upAxis = MultiAimConstraintData.Axis.Y;
            data.worldUpType = MultiAimConstraintData.WorldUpType.SceneUp;
            // Modest range so the head can turn but doesn't spin like an owl on aim targets that
            // stray far behind the character.
            data.limits = new Vector2(-70f, 70f);
            data.constrainedXAxis = true;
            data.constrainedYAxis = true;
            data.constrainedZAxis = false;
            aim.data = data;
            aim.weight = 1f;

            _rigBuilder.layers.Add(new RigLayer(rig, true));
            _rigBuilder.Build();
        }

        // Resolve the head bone. Prefer the KHR skeleton_mapping (vocab key "head" → the source rig's head
        // transform, e.g. VRoid's J_Bip_C_Head) — the SAME resolution the clip path uses — so the aim constraint
        // binds on the hero rig; fall back to a case-insensitive name search for non-KHR rigs.
        private static Transform FindHeadBone(GameObject character)
        {
            var skeleton = character.GetComponent<KhrCharacter>()?.Skeleton;
            if (skeleton != null && skeleton.TryGetBone("head", out var mapped) && mapped != null)
                return mapped;

            var candidates = new[] { "head", "Head", "HeadTop_End" };
            foreach (var name in candidates)
            {
                var t = FindDeep(character.transform, name);
                if (t != null) return t;
            }
            return null;
        }

        private static Transform FindDeep(Transform parent, string name)
        {
            for (int i = 0; i < parent.childCount; i++)
            {
                var child = parent.GetChild(i);
                if (string.Equals(child.name, name, System.StringComparison.OrdinalIgnoreCase))
                    return child;
                var deep = FindDeep(child, name);
                if (deep != null) return deep;
            }
            return null;
        }

        private void BuildClipButtons()
        {
            _ui.AddLabel("── Base clip (aim layered on top) ──");
            foreach (var clip in HumanoidClipFactory.AllForCharacter(_character))
            {
                var captured = clip;
                _ui.AddButton(clip.name.Contains("@") ? clip.name.Split('@')[0] : clip.name, () =>
                {
                    if (_animator == null || captured == null) return;
                    AnimationBinder.Play(_animator, captured);
                    if (_status != null) _status.text = $"Base: {captured.name} (aim: {(_rigBuilder != null ? "on" : "n/a")})";
                });
            }
        }
    }
}
