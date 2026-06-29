using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityGLTF.KhrCharacter;

namespace Samples.Shared
{
    /// <summary>
    /// RP-agnostic 3D debug overlay for the loaded character: a world-bounds box, skeleton lines, and a global
    /// wireframe toggle. Owns the visualization state (<see cref="ShowBounds"/> / <see cref="ShowSkeleton"/> /
    /// <see cref="Wireframe"/>) so the inspection panel and the keyboard front-end flip the same flags. Finds the
    /// scene's <see cref="KhrCharacter"/> lazily (controllers load async) and re-binds on character swap. Bounds +
    /// skeleton use <see cref="LineRenderer"/>s (which draw under Built-in and URP); only the wireframe path is
    /// pipeline-sensitive and hooks BOTH the Built-in camera callbacks and the SRP render callbacks.
    /// </summary>
    [DisallowMultipleComponent]
    public class AssetVisualizer : MonoBehaviour
    {
        // 16-point path over the 8 box corners that traces all 12 edges in one polyline (3 edges retraced).
        private static readonly int[] BoxPath = { 0, 1, 2, 3, 0, 4, 5, 6, 7, 4, 5, 1, 2, 6, 7, 3 };

        public Color BoundsColor = new Color(0.2f, 0.9f, 1f, 1f);
        public Color SkeletonColor = new Color(1f, 0.55f, 0.1f, 1f);

        private KhrCharacter _hub;
        private GameObject _characterRoot;

        private bool _showBounds;
        private bool _showSkeleton;
        private bool _wireframe;
        private bool _wireframeHooked;

        private Material _lineMaterial;
        private LineRenderer _boundsLine;
        private GameObject _skeletonRoot;
        private readonly List<LineRenderer> _skeletonLines = new List<LineRenderer>();
        private readonly List<(Transform a, Transform b)> _skeletonSegments = new List<(Transform, Transform)>();

        /// <summary>Show/hide the world-bounds wire box around the loaded character.</summary>
        public bool ShowBounds
        {
            get => _showBounds;
            set
            {
                if (_showBounds == value) return;
                _showBounds = value;
                if (!_showBounds) DestroyBoundsLine();
            }
        }

        /// <summary>Show/hide the skeleton lines (parent→child bone segments) of the loaded character.</summary>
        public bool ShowSkeleton
        {
            get => _showSkeleton;
            set
            {
                if (_showSkeleton == value) return;
                _showSkeleton = value;
                if (_showSkeleton) BuildSkeleton();
                else DestroySkeleton();
            }
        }

        /// <summary>Toggle a global <see cref="GL.wireframe"/> around the camera's render (Built-in + URP).</summary>
        public bool Wireframe
        {
            get => _wireframe;
            set
            {
                if (_wireframe == value) return;
                _wireframe = value;
                if (_wireframe) HookWireframe();
                else UnhookWireframe();
            }
        }

        private void Update()
        {
            // Re-bind when the character first appears or is swapped (e.g. the GLB viewer's Load button). The root
            // is set on discovery (bounds need only its renderers); the skeleton (re)builds on readiness.
            var hub = Object.FindFirstObjectByType<KhrCharacter>();
            if (hub == _hub) return;
            _hub = hub;
            _characterRoot = hub != null ? hub.gameObject : null;
            DestroySkeleton(); // clear any stale lines now; rebuilt on readiness if shown
            if (hub != null) hub.WhenReady(OnCharacterReady);
        }

        private void OnCharacterReady(KhrCharacter hub)
        {
            if (hub == null || hub != _hub) return; // a newer character superseded this one
            _characterRoot = hub.gameObject;
            if (_showSkeleton) BuildSkeleton(); // (re)build segments for the now-ready character
        }

        // Bounds + skeleton are refreshed after animation so the box/lines track the live pose each frame.
        private void LateUpdate()
        {
            if (_showBounds) RefreshBounds();
            if (_showSkeleton) RefreshSkeleton();
        }

        private void RefreshBounds()
        {
            if (_characterRoot == null || !SceneBoundsUtil.TryAggregate(_characterRoot, out var bounds))
            {
                DestroyBoundsLine();
                return;
            }

            if (_boundsLine == null) _boundsLine = CreateLine("BoundsBox", BoundsColor);

            Vector3 mn = bounds.min, mx = bounds.max;
            var corners = new[]
            {
                new Vector3(mn.x, mn.y, mn.z), new Vector3(mx.x, mn.y, mn.z),
                new Vector3(mx.x, mn.y, mx.z), new Vector3(mn.x, mn.y, mx.z),
                new Vector3(mn.x, mx.y, mn.z), new Vector3(mx.x, mx.y, mn.z),
                new Vector3(mx.x, mx.y, mx.z), new Vector3(mn.x, mx.y, mx.z),
            };

            _boundsLine.positionCount = BoxPath.Length;
            for (int i = 0; i < BoxPath.Length; i++) _boundsLine.SetPosition(i, corners[BoxPath[i]]);
            _boundsLine.widthMultiplier = Mathf.Max(bounds.size.magnitude * 0.004f, 0.002f);
        }

        // The hub wires its SkeletonMap at import, but fall back to one on the character root so the skeleton draws
        // even if hub wiring lags (and so tests can bind a SkeletonMap directly).
        private SkeletonMap ResolveSkeleton()
        {
            if (_hub != null && _hub.Skeleton != null) return _hub.Skeleton;
            return _characterRoot != null ? _characterRoot.GetComponentInChildren<SkeletonMap>(true) : null;
        }

        private void BuildSkeleton()
        {
            DestroySkeleton();
            var skeleton = ResolveSkeleton();
            var bones = skeleton != null && skeleton.Result != null ? skeleton.Result.Bones : null;
            if (bones == null || bones.Count == 0) return;

            var boneSet = new HashSet<Transform>();
            foreach (var bone in bones.Values)
                if (bone != null) boneSet.Add(bone);

            // Connect each bone to its nearest ancestor that is also a mapped bone (skips unmapped intermediates).
            foreach (var bone in boneSet)
            {
                var ancestor = bone.parent;
                while (ancestor != null && !boneSet.Contains(ancestor)) ancestor = ancestor.parent;
                if (ancestor != null) _skeletonSegments.Add((ancestor, bone));
            }
            if (_skeletonSegments.Count == 0) return;

            _skeletonRoot = new GameObject("SkeletonLines");
            _skeletonRoot.transform.SetParent(transform, false);
            for (int i = 0; i < _skeletonSegments.Count; i++)
            {
                var line = CreateLine("Bone", SkeletonColor, _skeletonRoot.transform);
                line.positionCount = 2;
                line.widthMultiplier = 0.01f;
                _skeletonLines.Add(line);
            }
        }

        private void RefreshSkeleton()
        {
            if (_skeletonLines.Count != _skeletonSegments.Count) { BuildSkeleton(); return; }
            for (int i = 0; i < _skeletonSegments.Count; i++)
            {
                var (a, b) = _skeletonSegments[i];
                var line = _skeletonLines[i];
                if (line == null || a == null || b == null) continue;
                line.SetPosition(0, a.position);
                line.SetPosition(1, b.position);
            }
        }

        private LineRenderer CreateLine(string lineName, Color color, Transform parent = null)
        {
            var go = new GameObject(lineName);
            go.transform.SetParent(parent != null ? parent : transform, false);
            var line = go.AddComponent<LineRenderer>();
            line.useWorldSpace = true;
            line.loop = false;
            line.numCornerVertices = 0;
            line.numCapVertices = 0;
            line.alignment = LineAlignment.View;
            line.shadowCastingMode = ShadowCastingMode.Off;
            line.receiveShadows = false;
            line.material = LineMaterial();
            line.startColor = line.endColor = color;
            line.widthMultiplier = 0.01f;
            return line;
        }

        // One shared material instance for every line; RenderPipelineUtil keeps it pipeline-correct (never magenta).
        private Material LineMaterial()
        {
            if (_lineMaterial != null) return _lineMaterial;
            var shader = RenderPipelineUtil.LitShader();
            _lineMaterial = shader != null ? new Material(shader) : null;
            return _lineMaterial;
        }

        private void HookWireframe()
        {
            if (_wireframeHooked) return;
            Camera.onPreRender += OnPreRenderBuiltin;
            Camera.onPostRender += OnPostRenderBuiltin;
            RenderPipelineManager.beginCameraRendering += OnBeginCameraSrp;
            RenderPipelineManager.endCameraRendering += OnEndCameraSrp;
            _wireframeHooked = true;
        }

        private void UnhookWireframe()
        {
            if (!_wireframeHooked) return;
            Camera.onPreRender -= OnPreRenderBuiltin;
            Camera.onPostRender -= OnPostRenderBuiltin;
            RenderPipelineManager.beginCameraRendering -= OnBeginCameraSrp;
            RenderPipelineManager.endCameraRendering -= OnEndCameraSrp;
            _wireframeHooked = false;
            GL.wireframe = false;
        }

        private void OnPreRenderBuiltin(Camera cam) => GL.wireframe = _wireframe;
        private void OnPostRenderBuiltin(Camera cam) => GL.wireframe = false;
        private void OnBeginCameraSrp(ScriptableRenderContext ctx, Camera cam) => GL.wireframe = _wireframe;
        private void OnEndCameraSrp(ScriptableRenderContext ctx, Camera cam) => GL.wireframe = false;

        private void DestroyBoundsLine()
        {
            if (_boundsLine != null) Destroy(_boundsLine.gameObject);
            _boundsLine = null;
        }

        private void DestroySkeleton()
        {
            _skeletonLines.Clear();
            _skeletonSegments.Clear();
            if (_skeletonRoot != null) Destroy(_skeletonRoot);
            _skeletonRoot = null;
        }

        private void OnEnable()
        {
            // Re-hook if this component was re-enabled while the wireframe flag is on (OnDisable unhooks it).
            if (_wireframe) HookWireframe();
        }

        private void OnDisable()
        {
            // Never leave the global wireframe flag stuck on if this component is disabled mid-render-loop.
            if (_wireframeHooked) UnhookWireframe();
        }

        private void OnDestroy()
        {
            UnhookWireframe();
            DestroyBoundsLine();
            DestroySkeleton();
            if (_lineMaterial != null) Destroy(_lineMaterial);
        }
    }
}
