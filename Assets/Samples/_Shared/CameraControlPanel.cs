using UnityEngine;
using UnityEngine.UI;

namespace Samples.Shared
{
    /// <summary>
    /// Self-building runtime camera-control + QoL panel. Finds the scene's <see cref="OrbitCameraRig"/> via
    /// <c>Camera.main</c> and, if present, builds a docked uGUI panel: orbit/pitch/zoom + pan + FOV sliders (all
    /// two-way synced), Reset View / Reset Pan-FOV, view presets (Front/3-4/Side/Back/Top), Auto-rotate, Head/Full
    /// framing, Hide-other-UI, and a keyboard legend. No-ops gracefully on scenes without an orbit camera (e.g. the
    /// hub). RP-agnostic Screen-Space-Overlay uGUI.
    /// </summary>
    [DisallowMultipleComponent]
    public class CameraControlPanel : MonoBehaviour
    {
        // World-space ± range (metres) of the Pan X/Y sliders, measured from the framed pivot.
        private const float PanRange = 2f;

        private OrbitCameraRig _rig;
        private DemoUiBuilder _ui;
        private Slider _yaw;
        private Slider _pitch;
        private Slider _zoom;
        private Slider _panX;
        private Slider _panY;
        private Slider _fov;
        private bool _headFraming;

        private void Start()
        {
            var cam = Camera.main;
            _rig = cam != null ? cam.GetComponent<OrbitCameraRig>() : null;
            if (_rig == null) return; // no orbit camera in this scene -> nothing to control

            _ui = DemoUiBuilder.Create("Camera", new Vector2(300f, 470f));
            DockRight(_ui);

            _yaw = _ui.AddSlider("Orbit (Yaw)", v => { _rig.Yaw = v; _rig.Apply(); }, -180f, 180f, WrapYaw(_rig.Yaw));
            _pitch = _ui.AddSlider("Pitch", v => { _rig.Pitch = v; _rig.Apply(); }, _rig.MinPitch, _rig.MaxPitch, _rig.Pitch);
            _zoom = _ui.AddSlider("Zoom (Distance)", v => { _rig.Distance = v; _rig.Apply(); }, _rig.MinDistance, _rig.MaxDistance, _rig.Distance);

            // Pan X/Y drive a world-space offset from the framed pivot; FOV drives the camera lens (clamped by the rig).
            _panX = _ui.AddSlider("Pan X", v => _rig.SetPivotOffsetXY(v, _panY != null ? _panY.value : 0f), -PanRange, PanRange, 0f);
            _panY = _ui.AddSlider("Pan Y", v => _rig.SetPivotOffsetXY(_panX != null ? _panX.value : 0f, v), -PanRange, PanRange, 0f);
            _fov = _ui.AddSlider("FOV", v => _rig.Fov = v, _rig.MinFov, _rig.MaxFov, _rig.Fov);

            _ui.AddButton("Reset View", () => { _rig.ResetView(); SyncPanSliders(); });
            _ui.AddButton("Reset Pan/FOV", () => { _rig.PanReset(); _rig.ResetFov(); SyncPanSliders(); });

            _ui.AddButton("Front", () => _rig.ApplyPreset(CameraPreset.Front));
            _ui.AddButton("3/4 View", () => _rig.ApplyPreset(CameraPreset.ThreeQuarter));
            _ui.AddButton("Side", () => _rig.ApplyPreset(CameraPreset.Side));
            _ui.AddButton("Back", () => _rig.ApplyPreset(CameraPreset.Back));
            _ui.AddButton("Top", () => _rig.ApplyPreset(CameraPreset.Top));

            _ui.AddToggle("Auto-rotate", on => _rig.AutoRotate = on, _rig.AutoRotate);
            _ui.AddToggle("Head framing", on => { _headFraming = on; _rig.SetFraming(on); }, _headFraming);
            _ui.AddToggle("Hide other UI", HideOtherUi, false);

            _ui.AddLabel("LMB drag: orbit \u00b7 Scroll: zoom");
            _ui.AddLabel("Keys: Arrows orbit \u00b7 WASD pan \u00b7 Q/E dolly");
            _ui.AddLabel("1-5 views \u00b7 F frame \u00b7 R reset \u00b7 H help");
        }

        // Keep the sliders in sync with the rig (mouse-orbit, auto-rotate, keyboard pan/dolly/FOV) without
        // re-triggering their callbacks.
        private void Update()
        {
            if (_rig == null) return;
            if (_yaw != null) _yaw.SetValueWithoutNotify(WrapYaw(_rig.Yaw));
            if (_pitch != null) _pitch.SetValueWithoutNotify(Mathf.Clamp(_rig.Pitch, _rig.MinPitch, _rig.MaxPitch));
            if (_zoom != null) _zoom.SetValueWithoutNotify(Mathf.Clamp(_rig.Distance, _rig.MinDistance, _rig.MaxDistance));
            if (_fov != null) _fov.SetValueWithoutNotify(Mathf.Clamp(_rig.Fov, _rig.MinFov, _rig.MaxFov));
            SyncPanSliders();
        }

        // Reflect the live pivot offset (framed baseline -> current pivot) on the pan sliders.
        private void SyncPanSliders()
        {
            if (_rig == null) return;
            var offset = _rig.Pivot - _rig.ShotPivot;
            if (_panX != null) _panX.SetValueWithoutNotify(Mathf.Clamp(offset.x, -PanRange, PanRange));
            if (_panY != null) _panY.SetValueWithoutNotify(Mathf.Clamp(offset.y, -PanRange, PanRange));
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
