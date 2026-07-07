using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.UI;
using UnityGLTF.KhrCharacter;
using Samples.Shared;

namespace Samples.Characters
{
    /// <summary>
    /// Character Showcase — drives a single character through every KHR_character capability in ONE combined,
    /// capability-gated panel. Loads the hero (the LFS-committed VRoid <c>khr-character-example.glb</c>) when
    /// present, else falls back to the committed synthetic <c>SC-FacePlus.glb</c> (so a fresh public clone works).
    /// Each section is null-checked against the hub's sub-controllers, so a character missing a capability simply
    /// omits that section. A VRM-origin hero is consumed purely via KHR_character; its VRMC_* extensions are ignored.
    ///
    /// Expression rows are built inline (mirroring <see cref="ExpressionControlPanel"/>'s row pattern) rather than
    /// via that component, because its Rebuild() calls Ui.ClearRows() — which would wipe the other sections of the
    /// shared combined panel.
    /// </summary>
    public class CharacterShowcaseController : MonoBehaviour
    {
        [Tooltip("Optional explicit character path. If empty, uses the hero VRoid when present, else SC-FacePlus.")]
        public string HeroGlbPath;

        private Vector3 _targetAnchor = new Vector3(0f, 1.4f, GazeTargetUtil.DefaultDistance);

        private DemoUiBuilder _ui;
        private OrbitCameraRig _rig;
        private GazeSolver _gaze;
        private CameraHintSet _hints;
        private SkeletonMap _skeleton;
        private Transform _gazeTarget;
        private Text _rigStatus;
        private float _targetX;
        private float _targetYOffset;

        private async void Start()
        {
            bool usingHero = string.IsNullOrEmpty(HeroGlbPath) && CharacterLoader.HeroExists;

            _rig = Object.FindFirstObjectByType<OrbitCameraRig>();

            var targetGo = new GameObject("GazeTarget");
            targetGo.transform.SetParent(transform, false);
            targetGo.transform.position = _targetAnchor;
            _gazeTarget = targetGo.transform;

            _ui = DemoUiBuilder.Create("Character Showcase");
            _ui.AddLabel(CharacterLoader.DemoCharacterBlurb(usingHero, "SC-FacePlus"));

            var content = new GameObject("LoadedContent");
            content.transform.SetParent(transform, false);

            GameObject scene;
            try
            {
                scene = string.IsNullOrEmpty(HeroGlbPath)
                    ? await CharacterLoader.LoadDemoCharacterAsync(content.transform, "SC-FacePlus.glb")
                    : await CharacterLoader.LoadAsync(HeroGlbPath, content.transform);
            }
            catch (System.Exception e) { Debug.LogException(e); _ui.AddLabel("Load failed: " + e.Message); AddBack(); return; }
            if (scene == null) { _ui.AddLabel("Load failed (no scene)."); AddBack(); return; }

            FrameScene(scene);

            var hub = scene.GetComponent<KhrCharacter>();
            if (hub == null) { _ui.AddLabel("No KHR Character data on this asset."); AddBack(); return; }

            hub.WhenReady(BuildPanel);
        }

        // Builds the combined panel; each capability section is gated on its sub-controller being present.
        private void BuildPanel(KhrCharacter hub)
        {
            if (hub == null) { AddBack(); return; }

            // Anchor the gaze target ~2m in front of the head joint (the scene was framed in Start).
            _targetAnchor = GazeTargetUtil.InFrontOfHead(hub);
            UpdateTarget();

            // Live capability health.
            var healthText = _ui.AddLabel(string.Empty);
            gameObject.AddComponent<HealthPanel>().Bind(hub, healthText);

            BuildExpressions(hub.Expressions);
            BuildRig(hub.Skeleton);
            BuildCameraHints(hub.CameraHints);
            BuildGaze(hub.Gaze);

            AddBack();
        }

        private void BuildExpressions(ExpressionController controller)
        {
            if (controller == null || controller.Count == 0) return;

            _ui.AddLabel($"Expressions ({controller.Count}):");
            var handles = controller.Expressions;
            for (int i = 0; i < handles.Count; i++)
            {
                string name = handles[i].Name;
                if (string.IsNullOrEmpty(name)) continue;
                var slider = _ui.AddSlider(name, v => controller.SetWeight(name, v), 0f, 1f, controller.GetWeight(name));
                // Binary (all-STEP) expressions snap to 0/1.
                if (handles[i].IsBinary) slider.wholeNumbers = true;
            }
            _ui.AddButton("Reset Expressions", controller.ResetAll);
        }

        private void BuildRig(SkeletonMap skeleton)
        {
            _skeleton = skeleton;
            if (_skeleton == null) return;

            _ui.AddLabel("Rig (best-effort humanoid):");
            _rigStatus = _ui.AddLabel(string.Empty);
            _ui.AddButton("Rig: Humanoid", () => SwitchRig(RigImportMode.Humanoid));
            _ui.AddButton("Rig: Generic", () => SwitchRig(RigImportMode.Generic));
            _ui.AddButton("Apply Reference Pose", () => { if (_skeleton != null) _skeleton.ApplyReferencePose(); });
            RefreshRig();
        }

        private void BuildCameraHints(CameraHintSet hints)
        {
            _hints = hints;
            if (_hints == null || _hints.Hints == null || _hints.Hints.Count == 0) return;

            _ui.AddLabel("Camera hints:");
            foreach (var hint in _hints.Hints)
            {
                if (hint == null || string.IsNullOrEmpty(hint.Role)) continue;
                string role = hint.Role;
                _ui.AddButton($"Camera: {role}", () => ApplyHint(role));
            }
            _ui.AddButton("Free Look", () => { if (_rig != null) _rig.enabled = true; });
        }

        private void BuildGaze(GazeSolver gaze)
        {
            _gaze = gaze;
            if (_gaze == null) return;

            _gaze.Mode = GazeSolver.LookAtMode.CustomTarget;
            _gaze.Target = _gazeTarget;
            _gaze.Weight = 1f;

            _ui.AddLabel("Gaze:");
            _ui.AddSlider("Gaze Weight", v => { if (_gaze != null) _gaze.Weight = v; }, 0f, 1f, 1f);
            _ui.AddSlider("Gaze Target X", v => { _targetX = Mathf.Lerp(-1f, 1f, v); UpdateTarget(); }, 0f, 1f, 0.5f);
            _ui.AddSlider("Gaze Target Y", v => { _targetYOffset = Mathf.Lerp(-1f, 1f, v); UpdateTarget(); }, 0f, 1f, 0.5f);
        }

        private void SwitchRig(RigImportMode mode)
        {
            if (_skeleton == null) return;
            bool ok = _skeleton.SwitchRigMode(mode);
            if (mode == RigImportMode.Humanoid && !ok)
                _rigStatus.text = "Humanoid rejected (bones invalid) - stays Generic.";
            else
                RefreshRig();
        }

        private void RefreshRig()
        {
            if (_skeleton == null || _rigStatus == null) return;
            _rigStatus.text = $"HumanoidAvailable: {_skeleton.HumanoidAvailable}";
        }

        private void ApplyHint(string role)
        {
            if (_hints == null) return;
            var cam = Camera.main;
            if (cam == null) return;
            if (_rig != null) _rig.enabled = false; // stop orbiting so the hint framing holds
            if (_hints.TryGetByRole(role, out var hint)) _hints.Apply(hint, cam, true);
        }

        private void UpdateTarget()
        {
            if (_gazeTarget != null)
                _gazeTarget.position = _targetAnchor + new Vector3(_targetX, _targetYOffset, 0f);
        }

        private void FrameScene(GameObject scene)
        {
            if (_rig == null || scene == null) return;
            if (!SceneBoundsUtil.TryAggregate(scene, out var bounds))
            {
                _rig.FrameAndFace(new Bounds(new Vector3(0f, 1f, 0f), new Vector3(1.5f, 2f, 1f)), scene.transform);
                return;
            }
            _rig.FrameAndFace(bounds, scene.transform);
        }

        private void AddBack()
        {
            var back = gameObject.AddComponent<BackToHubButton>();
            _ui.AddButton("Back to Hub", back.GoToHub);
        }
    }
}
