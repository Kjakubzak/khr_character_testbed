using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.UI;
using UnityGLTF.KhrCharacter;
using Samples.Shared;

namespace Samples.Characters
{
    /// <summary>
    /// RigAndPose demo (M4 + M6). Loads SC-Body and lets the user switch the rig between Generic and Humanoid
    /// (<see cref="SkeletonMap.SwitchRigMode"/>) and apply the reference pose. The humanoid build is best-effort:
    /// if Unity rejects the bone layout, <c>SwitchRigMode(Humanoid)</c> returns false and the character gracefully
    /// stays Generic (caveat C11) — never throws. M6: the imported character does not auto-play / snap to the
    /// T-pose on load (import-side suppression), but the reference-pose clip can still be played on demand.
    /// </summary>
    public class RigAndPoseController : MonoBehaviour
    {
        public string BodyGlbPath;

        private DemoUiBuilder _ui;
        private Text _status;
        private SkeletonMap _skeleton;
        private Animation _animation;

        private async void Start()
        {
            bool usingHero = string.IsNullOrEmpty(BodyGlbPath) && CharacterLoader.HeroExists;

            _ui = DemoUiBuilder.Create("Rig & Pose");
            _ui.AddLabel("Switch Generic/Humanoid and apply the reference pose. Humanoid is best-effort.");
            _ui.AddLabel(CharacterLoader.DemoCharacterBlurb(usingHero, "SC-Body"));
            _status = _ui.AddLabel("Loading ...");

            var bodyRoot = new GameObject("BodyRoot");
            bodyRoot.transform.SetParent(transform, false);

            GameObject scene;
            try
            {
                scene = string.IsNullOrEmpty(BodyGlbPath)
                    ? await CharacterLoader.LoadDemoCharacterAsync(bodyRoot.transform, "SC-Body.glb")
                    : await CharacterLoader.LoadAsync(BodyGlbPath, bodyRoot.transform);
            }
            catch (System.Exception e) { Debug.LogException(e); _status.text = "Load failed: " + e.Message; return; }
            if (scene == null) { _status.text = "Load failed."; return; }

            var hub = scene.GetComponent<KhrCharacter>();
            _skeleton = hub != null ? hub.Skeleton : null;
            FrameBody(scene);

            if (_skeleton == null)
            {
                _status.text = "This asset has no skeleton mapping.";
            }
            else
            {
                _ui.AddButton("Rig: Humanoid", () => SwitchRig(RigImportMode.Humanoid));
                _ui.AddButton("Rig: Generic", () => SwitchRig(RigImportMode.Generic));
                _ui.AddButton("Apply Reference Pose", () => { if (_skeleton != null) _skeleton.ApplyReferencePose(); });
                Refresh();
            }

            // M6: the importer suppresses auto-play, so the character does not loop or snap to the T-pose on load.
            _animation = scene.GetComponent<Animation>();
            _ui.AddLabel(_animation == null
                ? "Auto-play on load: none (no animation host)."
                : $"Auto-play on load: {_animation.isPlaying} (suppressed - no T-pose snap).");
            if (_animation != null)
                _ui.AddButton("Play ReferencePose clip", PlayReferencePose);

            if (hub != null)
            {
                var healthText = _ui.AddLabel(string.Empty);
                gameObject.AddComponent<HealthPanel>().Bind(hub, healthText);
            }

            _ui.AddLabel("C11: missing/invalid required bones -> stays Generic (graceful).");
            var back = gameObject.AddComponent<BackToHubButton>();
            _ui.AddButton("Back to Hub", back.GoToHub);
        }

        private void SwitchRig(RigImportMode mode)
        {
            if (_skeleton == null) return;
            bool ok = _skeleton.SwitchRigMode(mode);
            if (mode == RigImportMode.Humanoid && !ok)
                _status.text = "Humanoid rejected (bones invalid) - stays Generic.";
            else
                Refresh();
        }

        private void Refresh()
        {
            if (_skeleton == null || _status == null) return;
            _status.text = $"HumanoidAvailable: {_skeleton.HumanoidAvailable}    Direction: {_skeleton.DetectedDirection}";
        }

        private void PlayReferencePose()
        {
            if (_animation == null) return;
            string state = FindReferencePoseState(_animation);
            if (!string.IsNullOrEmpty(state)) _animation.Play(state);
        }

        // The reference-pose clip is exported as "ReferencePose_<PoseType>"; find its Animation state by name.
        private static string FindReferencePoseState(Animation animation)
        {
            foreach (AnimationState state in animation)
                if (state != null && state.clip != null &&
                    state.clip.name.IndexOf("ReferencePose", System.StringComparison.OrdinalIgnoreCase) >= 0)
                    return state.name;
            return null;
        }

        private void FrameBody(GameObject body)
        {
            var rig = Object.FindFirstObjectByType<OrbitCameraRig>();
            if (rig == null || body == null) return;

            var renderers = body.GetComponentsInChildren<Renderer>();
            if (renderers.Length == 0)
            {
                // SC-Body is bones-only (no mesh); frame a default human-sized volume around the skeleton.
                rig.Frame(new Bounds(new Vector3(0f, 1f, 0f), new Vector3(1.5f, 2f, 1f)));
                return;
            }
            var bounds = renderers[0].bounds;
            for (int i = 1; i < renderers.Length; i++) bounds.Encapsulate(renderers[i].bounds);
            rig.Frame(bounds);
        }
    }
}
