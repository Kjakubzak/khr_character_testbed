using UnityEngine;

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

        private void LateUpdate()
        {
            if (Input.GetMouseButton(0))
            {
                Yaw += Input.GetAxis("Mouse X") * OrbitSpeed * Time.deltaTime;
                Pitch -= Input.GetAxis("Mouse Y") * OrbitSpeed * Time.deltaTime;
                Pitch = Mathf.Clamp(Pitch, MinPitch, MaxPitch);
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
        }
    }
}
