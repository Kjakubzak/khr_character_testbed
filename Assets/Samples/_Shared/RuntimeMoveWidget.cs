using UnityEngine;

namespace Samples.Shared
{
    /// <summary>
    /// Makes its GameObject draggable at runtime via a small visible handle (a colored sphere primitive with a
    /// collider). Mouse-down raycasts <c>Camera.main</c> against the handle's collider; on a hit it drags the
    /// transform in the screen-facing plane (a plane through the handle, perpendicular to the camera) and sets
    /// <see cref="OrbitCameraRig.InputCaptured"/> so the camera does not orbit during the drag. Mouse-up clears it.
    /// Simple screen-plane translate (no 3-axis arrows). RP-agnostic: the handle material falls back across pipelines.
    /// </summary>
    [DisallowMultipleComponent]
    public class RuntimeMoveWidget : MonoBehaviour
    {
        public float HandleRadius = 0.08f;
        public Color HandleColor = new Color(0.2f, 0.9f, 1f, 1f);

        private Transform _handle;
        private Collider _handleCollider;
        private Camera _cam;
        private Material _handleMaterial;
        private bool _dragging;
        private Plane _dragPlane;
        private Vector3 _grabOffset;

        private void Start()
        {
            CreateHandle();
        }

        private void CreateHandle()
        {
            var sphere = GameObject.CreatePrimitive(PrimitiveType.Sphere); // ships a SphereCollider + MeshRenderer
            sphere.name = "DragHandle";
            _handle = sphere.transform;
            _handle.SetParent(transform, false);
            _handle.localPosition = Vector3.zero;
            _handle.localScale = Vector3.one * Mathf.Max(HandleRadius * 2f, 0.01f);
            _handleCollider = sphere.GetComponent<Collider>();

            var renderer = sphere.GetComponent<Renderer>();
            if (renderer != null)
            {
                var shader = Shader.Find("Universal Render Pipeline/Lit");
                if (shader == null) shader = Shader.Find("Standard");
                if (shader == null) shader = Shader.Find("Unlit/Color");
                if (shader != null)
                {
                    _handleMaterial = new Material(shader);
                    if (_handleMaterial.HasProperty("_BaseColor")) _handleMaterial.SetColor("_BaseColor", HandleColor);
                    if (_handleMaterial.HasProperty("_Color")) _handleMaterial.SetColor("_Color", HandleColor);
                    renderer.sharedMaterial = _handleMaterial;
                }
            }
        }

        private void Update()
        {
            if (_cam == null) _cam = Camera.main;
            if (_cam == null || _handleCollider == null) return;

            if (!_dragging)
            {
                if (Input.GetMouseButtonDown(0)) TryBeginDrag();
            }
            else if (Input.GetMouseButton(0))
            {
                Drag();
            }
            else
            {
                EndDrag();
            }
        }

        private void TryBeginDrag()
        {
            // Ignore clicks over UI (so a slider/button press doesn't start a drag).
            var es = UnityEngine.EventSystems.EventSystem.current;
            if (es != null && es.IsPointerOverGameObject()) return;

            var ray = _cam.ScreenPointToRay(Input.mousePosition);
            if (!_handleCollider.Raycast(ray, out _, 1000f)) return;

            _dragging = true;
            OrbitCameraRig.InputCaptured = true;
            _dragPlane = new Plane(-_cam.transform.forward, transform.position);
            _grabOffset = _dragPlane.Raycast(ray, out float enter) ? transform.position - ray.GetPoint(enter) : Vector3.zero;
        }

        private void Drag()
        {
            var ray = _cam.ScreenPointToRay(Input.mousePosition);
            if (_dragPlane.Raycast(ray, out float enter))
                transform.position = ray.GetPoint(enter) + _grabOffset;
        }

        private void EndDrag()
        {
            _dragging = false;
            OrbitCameraRig.InputCaptured = false;
        }

        // Make sure a drag in progress never leaves the camera permanently captured (e.g. on scene unload).
        private void OnDisable()
        {
            if (_dragging)
            {
                _dragging = false;
                OrbitCameraRig.InputCaptured = false;
            }
        }

        private void OnDestroy()
        {
            // The handle uses a runtime-created Material instance; destroy it so it isn't leaked when the widget goes.
            if (_handleMaterial != null) Destroy(_handleMaterial);
        }
    }
}
