using UnityEngine;
using UnityEngine.EventSystems;

namespace Samples.Shared
{
    /// <summary>
    /// Minimal mouse-orbit camera. Hold the left mouse button and drag to orbit, scroll to zoom. Call
    /// <see cref="Frame"/> to center and fit a target's world bounds. Uses the legacy Input Manager so it works
    /// without the Input System package; if a project switches to Input-System-only, replace the reads in
    /// <c>LateUpdate</c>.
    /// </summary>
    [RequireComponent(typeof(Camera))]
    [DisallowMultipleComponent]
    public class OrbitCameraRig : MonoBehaviour
    {
        public Vector3 Pivot = Vector3.zero;
        public float Distance = 3f;
        public float Yaw = 0f;
        public float Pitch = 12f;

        [Header("Tuning")]
        public float OrbitSpeed = 200f;
        public float ZoomSpeed = 4f;
        public float MinDistance = 0.3f;
        public float MaxDistance = 50f;
        public float MinPitch = -85f;
        public float MaxPitch = 85f;

        [Header("Facing")]
        [Tooltip("Forward-convention toggle. UnityGLTF import negates X and preserves Z, so glTF/VRM-1.0 characters " +
                 "keep +Z as their front, and FrameAndFace's LookRotation already turns that front toward the camera — " +
                 "leave this OFF. Tick it only if a character authored with a -Z front (e.g. VRM 0.x) shows its back.")]
        public bool FaceFlip180 = false;

        [Header("Auto-rotate")]
        public bool AutoRotate = false;
        public float AutoRotateSpeed = 20f;

        /// <summary>Set true by a 3D drag widget (e.g. RuntimeMoveWidget) to suppress camera orbit/zoom while dragging.</summary>
        public static bool InputCaptured;

        // Framing memory so the camera UI can restore the initial shot (ResetView) and re-frame (SetFraming).
        private bool _hasFraming;
        private bool _lastWasHead;
        private Bounds _lastBounds;
        private Transform _lastCharacter;
        private Vector3 _lastHeadCenter;
        private float _lastHeadRadius;
        private Vector3 _shotPivot;
        private float _shotYaw;
        private float _shotPitch;
        private float _shotDistance;

        private void LateUpdate()
        {
            // Block camera orbit/zoom while a 3D widget is being dragged (InputCaptured) or the pointer is over UI
            // (so a UI scroll area doesn't zoom the camera and widget clicks don't orbit it). Apply() resumes next
            // frame; the camera simply holds its pose meanwhile.
            if (InputCaptured) return;
            if (EventSystem.current != null && EventSystem.current.IsPointerOverGameObject()) return;

            bool dragging = Input.GetMouseButton(0);
            if (dragging)
            {
                Yaw += Input.GetAxis("Mouse X") * OrbitSpeed * Time.deltaTime;
                Pitch -= Input.GetAxis("Mouse Y") * OrbitSpeed * Time.deltaTime;
                Pitch = Mathf.Clamp(Pitch, MinPitch, MaxPitch);
            }
            else if (AutoRotate)
            {
                Yaw += AutoRotateSpeed * Time.deltaTime;
            }

            float scroll = Input.mouseScrollDelta.y;
            if (Mathf.Abs(scroll) > 0.0001f)
                Distance = Mathf.Clamp(Distance - scroll * ZoomSpeed, MinDistance, MaxDistance);

            Apply();
        }

        /// <summary>Position and orient the camera from the current pivot/yaw/pitch/distance.</summary>
        public void Apply()
        {
            var rot = Quaternion.Euler(Pitch, Yaw, 0f);
            transform.position = Pivot + rot * new Vector3(0f, 0f, -Distance);
            transform.rotation = rot;
        }

        /// <summary>Center the pivot on the bounds and back the camera off to fit them in view.</summary>
        public void Frame(Bounds bounds, float padding = 1.4f)
        {
            Pivot = bounds.center;
            float radius = Mathf.Max(bounds.extents.magnitude, 0.01f);
            var cam = GetComponent<Camera>();
            float fovDeg = cam != null ? cam.fieldOfView : 60f;
            float fit = radius / Mathf.Tan(fovDeg * 0.5f * Mathf.Deg2Rad);
            Distance = Mathf.Clamp(fit * padding, MinDistance, MaxDistance);
            Apply();

            _lastWasHead = false;
            _lastBounds = bounds;
            _lastCharacter = null;
            CaptureShot();
        }

        /// <summary>
        /// Frame the bounds, then rotate <paramref name="character"/> ONCE (start-only, never per-frame) so it faces
        /// the camera horizontally. Mouse-orbit still works afterward because this never runs again. Applies the
        /// <see cref="FaceFlip180"/> convention flip.
        /// </summary>
        public void FrameAndFace(Bounds bounds, Transform character, float padding = 1.4f)
        {
            Frame(bounds, padding);
            _lastCharacter = character; // remember the character for ResetView / SetFraming
            if (character == null) return;

            Vector3 toCam = transform.position - character.position;
            toCam.y = 0f;
            if (toCam.sqrMagnitude > 1e-4f)
                character.rotation = Quaternion.LookRotation(toCam.normalized);
            if (FaceFlip180)
                character.rotation *= Quaternion.Euler(0f, 180f, 0f);
        }

        /// <summary>
        /// Like <see cref="FrameAndFace"/>, but frames a tight head shot: pivot on <paramref name="headCenter"/>,
        /// back off to fit <paramref name="headRadius"/>, and use a small head-on pitch. Faces the character first
        /// (same LookRotation + <see cref="FaceFlip180"/> rule). <paramref name="fullBounds"/> is the whole-character
        /// bounds (kept for API symmetry / future clamping). Start-only, so mouse-orbit still works afterward.
        /// </summary>
        public void FrameAndFaceHead(Bounds fullBounds, Transform character, Vector3 headCenter, float headRadius, float padding = 1.15f)
        {
            Pivot = headCenter;
            float radius = Mathf.Max(headRadius, 0.01f);
            var cam = GetComponent<Camera>();
            float fovDeg = cam != null ? cam.fieldOfView : 60f;
            float fit = radius / Mathf.Tan(fovDeg * 0.5f * Mathf.Deg2Rad);
            Distance = Mathf.Clamp(fit * padding, MinDistance, MaxDistance);
            Pitch = 4f; // small head-on tilt for an eye-level face shot
            Apply();

            _lastWasHead = true;
            _lastBounds = fullBounds;
            _lastCharacter = character;
            _lastHeadCenter = headCenter;
            _lastHeadRadius = headRadius;
            CaptureShot();

            if (character == null) return;
            Vector3 toCam = transform.position - character.position;
            toCam.y = 0f;
            if (toCam.sqrMagnitude > 1e-4f)
                character.rotation = Quaternion.LookRotation(toCam.normalized);
            if (FaceFlip180)
                character.rotation *= Quaternion.Euler(0f, 180f, 0f);
        }

        /// <summary>Restore the initial framing shot (pivot/yaw/pitch/distance) captured by the last framing call.</summary>
        public void ResetView()
        {
            if (!_hasFraming) return;
            Pivot = _shotPivot;
            Yaw = _shotYaw;
            Pitch = _shotPitch;
            Distance = _shotDistance;
            Apply();
        }

        /// <summary>Re-frame the remembered character as a tight head shot or the full body.</summary>
        public void SetFraming(bool useHead)
        {
            if (!_hasFraming) return;
            if (useHead)
            {
                Vector3 center = _lastHeadCenter;
                float radius = _lastHeadRadius;
                if (radius <= 0f && _lastCharacter != null)
                    TryGetHeadFocus(_lastCharacter.gameObject, _lastBounds, out center, out radius);
                if (radius > 0f) { FrameAndFaceHead(_lastBounds, _lastCharacter, center, radius); return; }
            }
            FrameAndFace(_lastBounds, _lastCharacter);
        }

        private void CaptureShot()
        {
            _shotPivot = Pivot;
            _shotYaw = Yaw;
            _shotPitch = Pitch;
            _shotDistance = Distance;
            _hasFraming = true;
        }

        /// <summary>
        /// Find a tight head focus (center + radius) for a loaded character. Priority: (a) a humanoid Animator's
        /// Head bone, (b) a child whose name contains "head", (c) the average of "eye"-named children, (d) the top
        /// of the bounds. The radius is body-relative for a real body (head found via bone/name) and bounds-relative
        /// for a head-only mesh (eyes found but no head bone, e.g. the synthetic SC-Face). Always succeeds.
        /// </summary>
        public static bool TryGetHeadFocus(GameObject character, Bounds fullBounds, out Vector3 center, out float radius)
        {
            center = fullBounds.center;
            radius = Mathf.Clamp(0.13f * fullBounds.size.y, 0.08f, 0.5f);
            if (character == null) return false;

            // (a) humanoid Animator head bone -> a real body, so the head is a small fraction of the height.
            var animator = character.GetComponentInChildren<Animator>();
            if (animator != null && animator.isHuman)
            {
                var head = animator.GetBoneTransform(HumanBodyBones.Head);
                if (head != null) { center = head.position; return true; }
            }

            // (b) first child named "...head..."; (c) collect "...eye..." children for a fallback center.
            Transform headByName = null;
            Vector3 eyeSum = Vector3.zero;
            int eyeCount = 0;
            foreach (var t in character.GetComponentsInChildren<Transform>(true))
            {
                if (t == null) continue;
                string n = t.name.ToLowerInvariant();
                if (headByName == null && n.Contains("head")) headByName = t;
                if (n.Contains("eye")) { eyeSum += t.position; eyeCount++; }
            }
            if (headByName != null) { center = headByName.position; return true; } // named head bone -> body radius

            if (eyeCount > 0)
            {
                center = eyeSum / eyeCount;
                center.y += 0.05f; // raise slightly above the eyes toward mid-head
                // Eyes but no head bone => a head-only mesh (e.g. SC-Face): fill the view with most of the mesh.
                radius = Mathf.Max(0.5f * fullBounds.extents.magnitude, 0.08f);
                return true;
            }

            // (d) fallback: top of the bounds (assume a body with the head at the top).
            center = new Vector3(fullBounds.center.x, fullBounds.max.y - 0.10f * fullBounds.size.y, fullBounds.center.z);
            return true;
        }
    }
}
