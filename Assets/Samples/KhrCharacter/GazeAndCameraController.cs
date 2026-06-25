using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.UI;
using UnityGLTF.KhrCharacter;
using Samples.Shared;

namespace Samples.Characters
{
    /// <summary>
    /// GazeAndCamera demo (M3). Loads SC-Face for expression-driven gaze (a movable world target drives the
    /// look-left/right/up/down expressions via <see cref="GazeSolver"/>) and SC-Body for camera hints (role buttons
    /// frame a preview camera via <see cref="CameraHintSet.Apply"/>). Caveat C2: the camera projection index does
    /// not round-trip through glTF.
    /// </summary>
    public class GazeAndCameraController : MonoBehaviour
    {
        public string FaceGlbPath;
        public string BodyGlbPath;

        private const float TargetZ = 2f;

        private DemoUiBuilder _ui;
        private OrbitCameraRig _rig;
        private GazeSolver _gaze;
        private CameraHintSet _hints;
        private ViewModeController _viewMode;
        private EyeAimConstraint _eyeAim;
        private Transform _gazeTarget;
        private RuntimeMoveWidget _targetWidget;
        private float _targetX;
        private float _targetY;

        private async void Start()
        {
            if (string.IsNullOrEmpty(BodyGlbPath)) BodyGlbPath = CharacterLoader.SyntheticPath("SC-Body.glb");
            bool usingHero = string.IsNullOrEmpty(FaceGlbPath) && CharacterLoader.HeroExists;

            _rig = Object.FindFirstObjectByType<OrbitCameraRig>();

            var targetGo = new GameObject("GazeTarget");
            targetGo.transform.SetParent(transform, false);
            targetGo.transform.position = new Vector3(0f, 0f, TargetZ);
            _gazeTarget = targetGo.transform;

            _ui = DemoUiBuilder.Create("Gaze & Camera");
            _ui.AddLabel("Gaze: move the target and the face follows. Camera: pick a hint role.");
            _ui.AddLabel(CharacterLoader.DemoCharacterBlurb(usingHero, "SC-Face"));
            BuildGazeControls();

            // The demo character drives gaze (the hero when present, else SC-Face). SC-Body still provides the
            // camera hints below, so the fallback demoes both gaze and hints.
            var faceRoot = new GameObject("FaceRoot");
            faceRoot.transform.SetParent(transform, false);
            string facePath = !string.IsNullOrEmpty(FaceGlbPath) ? FaceGlbPath
                : (CharacterLoader.HeroExists ? CharacterLoader.HeroAbsolutePath : CharacterLoader.SyntheticPath("SC-Face.glb"));
            await LoadInto(facePath, faceRoot.transform, WireGaze);

            // SC-Body carries the camera hints (placed to the side so both are visible).
            var bodyRoot = new GameObject("BodyRoot");
            bodyRoot.transform.SetParent(transform, false);
            bodyRoot.transform.localPosition = new Vector3(1.5f, 0f, 0f);
            await LoadInto(BodyGlbPath, bodyRoot.transform, WireCameraHints);

            _ui.AddLabel("C2: camera projection index does not round-trip through glTF.");
            var back = gameObject.AddComponent<BackToHubButton>();
            _ui.AddButton("Back to Hub", back.GoToHub);
        }

        private static async Task LoadInto(string path, Transform parent, System.Action<KhrCharacter> onReady)
        {
            GameObject scene;
            try { scene = await CharacterLoader.LoadAsync(path, parent); }
            catch (System.Exception e) { Debug.LogException(e); return; }
            if (scene == null) return;
            var hub = scene.GetComponent<KhrCharacter>();
            if (hub != null) hub.WhenReady(onReady);
        }

        private void BuildGazeControls()
        {
            // Lambdas null-check the gaze solver, which is wired once SC-Face finishes loading.
            _ui.AddSlider("Gaze Weight", v => { if (_gaze != null) _gaze.Weight = v; }, 0f, 1f, 1f);
            _ui.AddSlider("Target X", v => { _targetX = Mathf.Lerp(-2f, 2f, v); UpdateTarget(); }, 0f, 1f, 0.5f);
            _ui.AddSlider("Target Y", v => { _targetY = Mathf.Lerp(-2f, 2f, v); UpdateTarget(); }, 0f, 1f, 0.5f);
            _ui.AddButton("Add movable target", AddMovableTarget);

            // N4: first/third-person view mode (first-person hides the head renderer).
            _ui.AddToggle("First-person (hide head) [N4]", on =>
            {
                if (_viewMode != null)
                    _viewMode.Mode = on ? ViewModeController.ViewMode.FirstPerson : ViewModeController.ViewMode.ThirdPerson;
            }, false);

            // N8: geometric eye-aim (NON-SPEC convenience, not KHR_character) on the named eye bones.
            _ui.AddToggle("Eye-aim at target (non-spec) [N8]", on =>
            {
                if (_eyeAim != null)
                    _eyeAim.Mode = on ? EyeAimConstraint.LookAtMode.CustomTarget : EyeAimConstraint.LookAtMode.None;
            }, false);
            _ui.AddLabel("N8: eye bones are markers (no eye geometry); they rotate toward the gaze target.");
        }

        private void UpdateTarget()
        {
            if (_gazeTarget != null) _gazeTarget.position = new Vector3(_targetX, _targetY, TargetZ);
        }

        // Place the gaze target in front of the head (toward the camera at Yaw 0) and make it draggable via an
        // in-scene handle; the eyes track it as the user drags. Reuses the existing _gazeTarget the gaze points at.
        private void AddMovableTarget()
        {
            if (_gazeTarget == null) return;

            Vector3 headCenter = _rig != null ? _rig.Pivot : new Vector3(0f, 1.4f, 0f);
            _gazeTarget.position = headCenter + new Vector3(0f, 0f, -0.6f);

            if (_targetWidget == null)
                _targetWidget = _gazeTarget.gameObject.AddComponent<RuntimeMoveWidget>();

            if (_gaze != null)
            {
                _gaze.Mode = GazeSolver.LookAtMode.CustomTarget;
                _gaze.Target = _gazeTarget;
                if (_gaze.Weight <= 0f) _gaze.Weight = 1f;
            }
        }

        private void WireGaze(KhrCharacter hub)
        {
            if (hub == null) return;

            // Frame + face the gaze character to the camera (once, start-only — mouse-orbit still works after).
            if (_rig != null)
            {
                var renderers = hub.GetComponentsInChildren<Renderer>();
                Bounds bounds = renderers.Length > 0
                    ? renderers[0].bounds
                    : new Bounds(new Vector3(0f, 1f, 0f), new Vector3(1.5f, 2f, 1f));
                for (int i = 1; i < renderers.Length; i++) bounds.Encapsulate(renderers[i].bounds);

                // Gaze is a head shot: zoom onto the gaze character's head (still facing the camera).
                if (OrbitCameraRig.TryGetHeadFocus(hub.gameObject, bounds, out var headCenter, out var headRadius))
                    _rig.FrameAndFaceHead(bounds, hub.transform, headCenter, headRadius);
                else
                    _rig.FrameAndFace(bounds, hub.transform);
            }

            _gaze = hub.Gaze;
            if (_gaze == null) { _ui.AddLabel("Face asset has no gaze solver."); return; }
            _gaze.Mode = GazeSolver.LookAtMode.CustomTarget;
            _gaze.Target = _gazeTarget;
            _gaze.Weight = 1f;

            // N4: register the head renderer so first-person hides it. Reuse the importer-attached
            // ViewModeController ([DisallowMultipleComponent]); only add one if the import didn't.
            _viewMode = hub.View;
            if (_viewMode == null) _viewMode = hub.gameObject.GetComponent<ViewModeController>();
            if (_viewMode == null) _viewMode = hub.gameObject.AddComponent<ViewModeController>();
            var headRenderer = hub.GetComponentInChildren<SkinnedMeshRenderer>();
            if (headRenderer != null)
                _viewMode.RegisterRenderer(headRenderer, ViewModeController.RenderView.ThirdPersonOnly);

            // N8: add a (disabled) geometric eye-aim on the named eye bones, aiming at the same gaze target.
            var leftEye = hub.transform.Find("LeftEye");
            var rightEye = hub.transform.Find("RightEye");
            if (leftEye != null || rightEye != null)
            {
                _eyeAim = hub.gameObject.AddComponent<EyeAimConstraint>();
                _eyeAim.SetEyeBones(leftEye, rightEye, hub.transform);
                _eyeAim.Target = _gazeTarget;
                _eyeAim.Mode = EyeAimConstraint.LookAtMode.None;
            }
        }

        private void WireCameraHints(KhrCharacter hub)
        {
            _hints = hub != null ? hub.CameraHints : null;
            if (_hints == null) { _ui.AddLabel("Body asset has no camera hints."); return; }

            foreach (var hint in _hints.Hints)
            {
                if (hint == null || string.IsNullOrEmpty(hint.Role)) continue;
                string role = hint.Role;
                _ui.AddButton($"Camera: {role}", () => ApplyHint(role));
            }
            _ui.AddButton("Free Look", () => { if (_rig != null) _rig.enabled = true; });
        }

        private void ApplyHint(string role)
        {
            if (_hints == null) return;
            var cam = Camera.main;
            if (cam == null) return;
            if (_rig != null) _rig.enabled = false; // stop orbiting so the hint framing holds
            if (_hints.TryGetByRole(role, out var hint)) _hints.Apply(hint, cam, true);
        }
    }
}
