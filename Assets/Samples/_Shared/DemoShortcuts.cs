using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using UnityGLTF.KhrCharacter;

namespace Samples.Shared
{
    /// <summary>
    /// Centralized keyboard front-end for the demos (legacy Input Manager, to match the project). It is the single
    /// keyboard host so shortcuts work even while the pointer is over a panel — it runs in <c>Update</c> (not the
    /// rig's pointer-gated <c>LateUpdate</c>) and is suppressed only while a text <see cref="InputField"/> is
    /// focused. It drives the shared <see cref="OrbitCameraRig"/> movement API, the <see cref="AssetVisualizer"/>
    /// overlays, and the <see cref="AssetInspectionPanel"/>, and renders a help overlay. Every target is resolved
    /// lazily and null-checked, so it no-ops on scenes that lack one (e.g. the hub). Camera keys honor
    /// <c>rig.enabled</c>, so they stay inert while a camera-hint view holds the framing (until "Free Look").
    /// </summary>
    [DisallowMultipleComponent]
    public class DemoShortcuts : MonoBehaviour
    {
        [Header("Rates")]
        public float OrbitDegPerSec = 90f;
        public float PanScreenPerSec = 0.6f;
        public float DollyPerSec = 8f;
        public float FovPerSec = 30f;

        private OrbitCameraRig _rig;
        private AssetVisualizer _viz;
        private AssetInspectionPanel _inspect;
        private bool _headFraming;
        private bool _uiHidden;
        private bool _showHelp;

        private void Update()
        {
            if (IsTypingInField()) return;

            ResolveTargets();
            HandleCameraKeys();
            HandleVisualizationKeys();
            HandleUiKeys();
        }

        // Suppress shortcuts only while a uGUI text field has focus (so typing a GLB path / FOV value never moves
        // the camera); everything else — including the pointer being over a panel — still passes through.
        private static bool IsTypingInField()
        {
            var es = EventSystem.current;
            var selected = es != null ? es.currentSelectedGameObject : null;
            return selected != null && selected.GetComponent<InputField>() != null;
        }

        private void ResolveTargets()
        {
            if (_rig == null)
            {
                var cam = Camera.main;
                _rig = cam != null ? cam.GetComponent<OrbitCameraRig>() : null;
            }
            if (_viz == null) _viz = Object.FindFirstObjectByType<AssetVisualizer>();
            if (_inspect == null) _inspect = Object.FindFirstObjectByType<AssetInspectionPanel>();
        }

        private void HandleCameraKeys()
        {
            // Inert while the rig is disabled (a camera-hint view holds the framing until "Free Look" re-enables it).
            if (_rig == null || !_rig.enabled) return;

            float dt = Time.deltaTime;

            // Orbit (arrows): match the mouse convention (drag up lowers pitch, drag right raises yaw).
            float yaw = (Key(KeyCode.RightArrow) ? 1f : 0f) - (Key(KeyCode.LeftArrow) ? 1f : 0f);
            float pitch = (Key(KeyCode.DownArrow) ? 1f : 0f) - (Key(KeyCode.UpArrow) ? 1f : 0f);
            if (yaw != 0f || pitch != 0f) _rig.OrbitBy(yaw * OrbitDegPerSec * dt, pitch * OrbitDegPerSec * dt);

            // Pan (WASD) in the camera's screen plane.
            float px = (Key(KeyCode.D) ? 1f : 0f) - (Key(KeyCode.A) ? 1f : 0f);
            float py = (Key(KeyCode.W) ? 1f : 0f) - (Key(KeyCode.S) ? 1f : 0f);
            if (px != 0f || py != 0f) _rig.Pan(new Vector2(px, py) * (PanScreenPerSec * dt));

            // Dolly (Q out / E in).
            float dolly = (Key(KeyCode.E) ? 1f : 0f) - (Key(KeyCode.Q) ? 1f : 0f);
            if (dolly != 0f) _rig.Dolly(dolly * DollyPerSec * dt);

            // FOV ( [ - / ] + , 0 reset ).
            float fov = (Key(KeyCode.RightBracket) ? 1f : 0f) - (Key(KeyCode.LeftBracket) ? 1f : 0f);
            if (fov != 0f) _rig.Fov += fov * FovPerSec * dt;
            if (KeyDown(KeyCode.Alpha0) || KeyDown(KeyCode.Keypad0)) _rig.ResetFov();

            // Presets 1-5.
            if (KeyDown(KeyCode.Alpha1)) _rig.ApplyPreset(CameraPreset.Front);
            if (KeyDown(KeyCode.Alpha2)) _rig.ApplyPreset(CameraPreset.ThreeQuarter);
            if (KeyDown(KeyCode.Alpha3)) _rig.ApplyPreset(CameraPreset.Side);
            if (KeyDown(KeyCode.Alpha4)) _rig.ApplyPreset(CameraPreset.Back);
            if (KeyDown(KeyCode.Alpha5)) _rig.ApplyPreset(CameraPreset.Top);

            // Frame / reset / framing toggle / auto-rotate.
            if (KeyDown(KeyCode.F)) FrameCharacter();
            if (KeyDown(KeyCode.R)) _rig.ResetView();
            if (KeyDown(KeyCode.T)) { _headFraming = !_headFraming; _rig.SetFraming(_headFraming); }
            if (KeyDown(KeyCode.Space)) _rig.AutoRotate = !_rig.AutoRotate;
        }

        private void HandleVisualizationKeys()
        {
            if (_viz == null) return;
            if (KeyDown(KeyCode.B)) _viz.ShowBounds = !_viz.ShowBounds;
            if (KeyDown(KeyCode.K)) _viz.ShowSkeleton = !_viz.ShowSkeleton;
            if (KeyDown(KeyCode.M)) _viz.Wireframe = !_viz.Wireframe;
        }

        private void HandleUiKeys()
        {
            if (KeyDown(KeyCode.I) && _inspect != null) _inspect.ToggleVisible();
            if (KeyDown(KeyCode.H)) ToggleHideAllUi();
            if (KeyDown(KeyCode.Slash) || KeyDown(KeyCode.F1)) _showHelp = !_showHelp;
        }

        private void FrameCharacter()
        {
            var hub = Object.FindFirstObjectByType<KhrCharacter>();
            if (hub != null && SceneBoundsUtil.TryAggregate(hub.gameObject, out var bounds))
                _rig.FrameAndFace(bounds, hub.transform);
            else
                _rig.ResetView();
        }

        private void ToggleHideAllUi()
        {
            _uiHidden = !_uiHidden;
            foreach (var panel in Object.FindObjectsByType<DemoUiBuilder>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                if (panel != null) panel.gameObject.SetActive(!_uiHidden);
        }

        private static bool Key(KeyCode code) => Input.GetKey(code);
        private static bool KeyDown(KeyCode code) => Input.GetKeyDown(code);

        private void OnGUI()
        {
            if (!_showHelp) return;
            const float w = 320f, h = 230f;
            GUILayout.BeginArea(new Rect(10f, 10f, w, h), GUI.skin.box);
            GUILayout.Label("<b>Keyboard shortcuts</b>");
            GUILayout.Label("Arrows: orbit   WASD: pan   Q/E: dolly out/in");
            GUILayout.Label("[ / ] : FOV -/+    0 : reset FOV");
            GUILayout.Label("1-5: Front / 3-4 / Side / Back / Top");
            GUILayout.Label("F: frame   R: reset view   T: head/full   Space: auto-rotate");
            GUILayout.Label("B: bounds   K: skeleton   M: wireframe");
            GUILayout.Label("I: inspect panel   H: hide all UI   ? : this help");
            GUILayout.Label("(Camera keys pause during a hint view — press Free Look.)");
            GUILayout.EndArea();
        }
    }
}
