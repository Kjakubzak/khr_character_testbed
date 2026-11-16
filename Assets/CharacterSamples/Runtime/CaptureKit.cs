using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEngine;
using UnityEngine.Rendering;
using UnityGLTF.KhrCharacter;

namespace KhrCharacterSamples
{
    /// <summary>Camera, light, framing, stick-figure overlay and PNG output shared by capture and video.</summary>
    public sealed class CaptureKit : System.IDisposable
    {
        public readonly Camera Camera;
        public readonly Light Light;
        private readonly List<GameObject> _overlay = new List<GameObject>();
        private Material _overlayMaterial;

        public CaptureKit(Color background)
        {
            var cameraGo = new GameObject("KhrCaptureCamera");
            Camera = cameraGo.AddComponent<Camera>();
            Camera.clearFlags = CameraClearFlags.SolidColor;
            Camera.backgroundColor = background;
            Camera.nearClipPlane = 0.01f;
            Camera.farClipPlane = 200f;
            Camera.fieldOfView = 30f;
            Camera.tag = "MainCamera";
            var lightGo = new GameObject("KhrCaptureLight");
            Light = lightGo.AddComponent<Light>();
            Light.type = LightType.Directional;
            Light.intensity = 1.2f;
            lightGo.transform.rotation = Quaternion.Euler(35f, 150f, 0f);
            RenderSettings.ambientMode = AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(0.45f, 0.45f, 0.48f);
        }

        /// <summary>Named orbit views around the character, which faces +Z after import (glTF +Z forward).</summary>
        public static float ViewYaw(string view)
        {
            switch (view)
            {
                case "front": return 0f;
                case "side": return -90f;          // from the character's left (glTF +X is Unity -X)
                case "three_quarter": return -45f;
                case "back": return 180f;
                default: return float.TryParse(view, out var yaw) ? yaw : 0f;
            }
        }

        /// <summary>Places the camera on a horizontal orbit that frames <paramref name="bounds"/>.</summary>
        public void Frame(Bounds bounds, float yawDegrees, float margin = 1.15f, float pitchDegrees = 5f)
        {
            Camera.ResetWorldToCameraMatrix();
            Camera.ResetProjectionMatrix();
            Camera.orthographic = false;
            Camera.fieldOfView = 30f;
            float radius = Mathf.Max(bounds.extents.magnitude, 0.05f) * margin;
            float distance = radius / Mathf.Sin(Camera.fieldOfView * 0.5f * Mathf.Deg2Rad);
            var direction = Quaternion.Euler(pitchDegrees, yawDegrees, 0f) * Vector3.forward;
            Camera.transform.position = bounds.center + direction * distance;
            Camera.transform.LookAt(bounds.center, Vector3.up);
            Camera.nearClipPlane = Mathf.Max(0.01f, distance - radius * 2f);
            Camera.farClipPlane = distance + radius * 4f;
        }

        /// <summary>
        /// <see cref="CameraHintSet.Apply"/>, plus the hint's projection read from the file when UnityGLTF supplies
        /// none. UnityGLTF resolves the hint's <c>camera</c> only through a Unity Camera that some node instantiated
        /// (<c>node.camera</c>), so a camera definition referenced only by the hint arrives as
        /// <c>Projection == null</c>, and Apply would leave the previous near/far/FOV in place. Returns where the
        /// projection came from.
        /// </summary>
        public string ApplyHint(LoadedCharacter c, CameraHint hint)
        {
            c.Hub.CameraHints.Apply(hint, Camera, copyProjection: true);
            if (hint.Projection != null) return "unitygltf";
            var cameraIndex = c.Gltf?["nodes"]?[hint.NodeIndex]?["extensions"]?["KHR_node_camera_hint"]?["camera"];
            var definition = cameraIndex != null ? c.Gltf["cameras"]?[(int)cameraIndex] : null;
            if (definition == null) return "none";
            if (definition["perspective"] is Newtonsoft.Json.Linq.JObject p)
            {
                Camera.orthographic = false;
                Camera.fieldOfView = (float)p["yfov"] * Mathf.Rad2Deg;
                Camera.nearClipPlane = (float)p["znear"];
                Camera.farClipPlane = (float?)p["zfar"] ?? 1000f;
            }
            else if (definition["orthographic"] is Newtonsoft.Json.Linq.JObject o)
            {
                Camera.orthographic = true;
                Camera.orthographicSize = (float)o["ymag"];
                Camera.nearClipPlane = (float)o["znear"];
                Camera.farClipPlane = (float)o["zfar"];
            }
            return "file (harness fallback)";
        }

        /// <summary>Frames a joint (e.g. the mapped head) at close range for face shots.</summary>
        public void FrameJoint(Transform joint, float radius, float yawDegrees = 0f)
        {
            Frame(new Bounds(joint.position, Vector3.one * radius * 2f), yawDegrees, 1.0f, 0f);
        }

        /// <summary>Thin capsules along parent→child joint segments, for characters (or proxies) with no mesh.</summary>
        public void AddStickFigure(LoadedCharacter c, Color color)
        {
            ClearOverlay();
            var shader = Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Unlit/Color") ?? Shader.Find("Sprites/Default");
            _overlayMaterial = new Material(shader) { color = color };
            var joints = new HashSet<Transform>();
            if (c.Skeleton?.Result?.Bones != null)
                foreach (var kv in c.Skeleton.Result.Bones) if (kv.Value != null) joints.Add(kv.Value);
            if (joints.Count == 0)
                foreach (var t in c.Root.GetComponentsInChildren<Transform>()) if (t != c.Root.transform) joints.Add(t);
            var bounds = CharacterGeometry.WorldBounds(c);
            float thickness = Mathf.Max(bounds.size.y, 0.1f) * 0.012f;
            foreach (var joint in joints)
            {
                var parent = joint.parent;
                while (parent != null && !joints.Contains(parent) && parent != c.Root.transform) parent = parent.parent;
                if (parent == null || parent == c.Root.transform) continue;
                var segment = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                Object.Destroy(segment.GetComponent<Collider>());
                segment.name = "stick_" + joint.name;
                segment.GetComponent<Renderer>().sharedMaterial = _overlayMaterial;
                segment.AddComponent<StickSegment>().Bind(parent, joint, thickness);
                _overlay.Add(segment);
            }
        }

        public void ClearOverlay()
        {
            foreach (var go in _overlay) if (go != null) Object.Destroy(go);
            _overlay.Clear();
        }

        /// <summary>Renders the camera into a PNG at <paramref name="path"/>.</summary>
        public void RenderPng(string path, int width, int height)
        {
            foreach (var s in Object.FindObjectsByType<StickSegment>(FindObjectsSortMode.None)) s.Follow();
            // Stills pose the skeleton and render several times inside one frame; without this a skinned mesh can
            // keep the skinning matrices of its last per-frame update and show a stale pose.
            foreach (var s in Object.FindObjectsByType<SkinnedMeshRenderer>(FindObjectsSortMode.None)) s.forceMatrixRecalculationPerRender = true;
            var rt = RenderTexture.GetTemporary(width, height, 24, RenderTextureFormat.ARGB32);
            var previous = RenderTexture.active;
            try
            {
                Camera.aspect = (float)width / height;
                Camera.targetTexture = rt;
                bool submitted = false;
#if UNITY_2023_2_OR_NEWER
                if (GraphicsSettings.currentRenderPipeline != null)
                {
                    var request = new RenderPipeline.StandardRequest { destination = rt };
                    if (RenderPipeline.SupportsRenderRequest(Camera, request))
                    {
                        RenderPipeline.SubmitRenderRequest(Camera, request);
                        submitted = true;
                    }
                }
#endif
                if (!submitted) Camera.Render();
                RenderTexture.active = rt;
                var texture = new Texture2D(width, height, TextureFormat.RGB24, false);
                texture.ReadPixels(new Rect(0, 0, width, height), 0, 0);
                texture.Apply();
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                File.WriteAllBytes(path, texture.EncodeToPNG());
                Object.Destroy(texture);
            }
            finally
            {
                Camera.targetTexture = null;
                RenderTexture.active = previous;
                RenderTexture.ReleaseTemporary(rt);
            }
        }

        /// <summary>Tiles same-sized PNGs into one grid PNG.</summary>
        public static void WriteGrid(IReadOnlyList<string> tiles, int columns, string path)
        {
            if (tiles.Count == 0) return;
            var first = new Texture2D(2, 2);
            first.LoadImage(File.ReadAllBytes(tiles[0]));
            int w = first.width, h = first.height, rows = (tiles.Count + columns - 1) / columns;
            var grid = new Texture2D(w * columns, h * rows, TextureFormat.RGB24, false);
            var fill = new Color[grid.width * grid.height];
            for (int i = 0; i < fill.Length; i++) fill[i] = Color.black;
            grid.SetPixels(fill);
            for (int i = 0; i < tiles.Count; i++)
            {
                var tile = i == 0 ? first : new Texture2D(2, 2);
                if (i > 0) tile.LoadImage(File.ReadAllBytes(tiles[i]));
                if (tile.width == w && tile.height == h)
                    grid.SetPixels((i % columns) * w, (rows - 1 - i / columns) * h, w, h, tile.GetPixels());
                if (i > 0) Object.Destroy(tile);
            }
            grid.Apply();
            File.WriteAllBytes(path, grid.EncodeToPNG());
            Object.Destroy(first);
            Object.Destroy(grid);
        }

        /// <summary>
        /// Runs one ExpressionController step now (it otherwise runs in LateUpdate), so a still shows the weights just
        /// set. Reflection is a capture-only convenience: the controller exposes no public step method.
        /// </summary>
        public static void StepExpressions(ExpressionController controller)
        {
            if (controller == null) return;
            typeof(ExpressionController).GetMethod("LateUpdate", BindingFlags.Instance | BindingFlags.NonPublic)
                ?.Invoke(controller, null);
        }

        public void Dispose()
        {
            ClearOverlay();
            if (_overlayMaterial != null) Object.Destroy(_overlayMaterial);
            if (Camera != null) Object.Destroy(Camera.gameObject);
            if (Light != null) Object.Destroy(Light.gameObject);
        }
    }

    /// <summary>Keeps a stick-figure cylinder between two joints.</summary>
    public sealed class StickSegment : MonoBehaviour
    {
        private Transform _a, _b;
        private float _thickness;

        public void Bind(Transform a, Transform b, float thickness)
        {
            _a = a; _b = b; _thickness = thickness;
            Follow();
        }

        public void Follow()
        {
            if (_a == null || _b == null) return;
            var delta = _b.position - _a.position;
            transform.position = (_a.position + _b.position) * 0.5f;
            transform.rotation = delta.sqrMagnitude > 1e-12f ? Quaternion.FromToRotation(Vector3.up, delta) : Quaternion.identity;
            transform.localScale = new Vector3(_thickness, delta.magnitude * 0.5f, _thickness);
        }

        private void LateUpdate() => Follow();
    }
}
