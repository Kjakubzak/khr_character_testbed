using UnityEngine;
using UnityEngine.UI;

namespace Samples.Shared
{
    /// <summary>
    /// Self-building runtime camera-control + QoL panel. Finds the scene's <see cref="OrbitCameraRig"/> via
    /// <c>Camera.main</c> and, if present, builds a docked uGUI panel: orbit/pitch/zoom sliders (two-way synced),
    /// Reset View + view presets, Auto-rotate, Head/Full framing, and Hide-other-UI. No-ops gracefully on scenes
    /// without an orbit camera (e.g. the hub). RP-agnostic Screen-Space-Overlay uGUI.
    /// </summary>
    [DisallowMultipleComponent]
    public class CameraControlPanel : MonoBehaviour
    {
        private OrbitCameraRig _rig;
        private DemoUiBuilder _ui;
        private Slider _yaw;
        private Slider _pitch;
        private Slider _zoom;
        private bool _headFraming;

        private void Start()
        {
            var cam = Camera.main;
            _rig = cam != null ? cam.GetComponent<OrbitCameraRig>() : null;
            if (_rig == null) return; // no orbit camera in this scene -> nothing to control

            _ui = DemoUiBuilder.Create("Camera", new Vector2(300f, 380f));
            DockRight(_ui);

            _yaw = _ui.AddSlider("Orbit (Yaw)", v => { _rig.Yaw = v; _rig.Apply(); }, -180f, 180f, WrapYaw(_rig.Yaw));
            _pitch = _ui.AddSlider("Pitch", v => { _rig.Pitch = v; _rig.Apply(); }, _rig.MinPitch, _rig.MaxPitch, _rig.Pitch);
            _zoom = _ui.AddSlider("Zoom (Distance)", v => { _rig.Distance = v; _rig.Apply(); }, _rig.MinDistance, _rig.MaxDistance, _rig.Distance);

            _ui.AddButton("Reset View", () => _rig.ResetView());
            _ui.AddButton("Front", () => SetAngles(0f, 5f));
            _ui.AddButton("3/4 View", () => SetAngles(30f, 8f));
            _ui.AddButton("Side", () => SetAngles(90f, 5f));

            _ui.AddToggle("Auto-rotate", on => _rig.AutoRotate = on, _rig.AutoRotate);
            _ui.AddToggle("Head framing", on => { _headFraming = on; _rig.SetFraming(on); }, _headFraming);
            _ui.AddToggle("Hide other UI", HideOtherUi, false);

            _ui.AddLabel("LMB drag: orbit \u00b7 Scroll: zoom");
        }

        // Keep the sliders in sync with the rig (mouse-orbit, auto-rotate) without re-triggering their callbacks.
        private void Update()
        {
            if (_rig == null) return;
            if (_yaw != null) _yaw.SetValueWithoutNotify(WrapYaw(_rig.Yaw));
            if (_pitch != null) _pitch.SetValueWithoutNotify(Mathf.Clamp(_rig.Pitch, _rig.MinPitch, _rig.MaxPitch));
            if (_zoom != null) _zoom.SetValueWithoutNotify(Mathf.Clamp(_rig.Distance, _rig.MinDistance, _rig.MaxDistance));
        }

        private void SetAngles(float yaw, float pitch)
        {
            _rig.Yaw = yaw;
            _rig.Pitch = Mathf.Clamp(pitch, _rig.MinPitch, _rig.MaxPitch);
            _rig.Apply();
        }

        // Toggle every OTHER demo panel (the controller's control panel, expression panel, etc.) for a clean view,
        // keeping this camera panel visible. Includes inactive objects so re-showing works after hiding.
        private void HideOtherUi(bool hide)
        {
            foreach (var panel in Object.FindObjectsByType<DemoUiBuilder>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                if (panel != null && panel != _ui)
                    panel.gameObject.SetActive(!hide);
        }

        private static float WrapYaw(float yaw) => Mathf.Repeat(yaw + 180f, 360f) - 180f;

        private static void DockRight(DemoUiBuilder ui)
        {
            if (ui == null || ui.Panel == null) return;
            var rt = ui.Panel;
            rt.anchorMin = new Vector2(1f, 1f);
            rt.anchorMax = new Vector2(1f, 1f);
            rt.pivot = new Vector2(1f, 1f);
            rt.anchoredPosition = new Vector2(-10f, -10f);
        }
    }
}
