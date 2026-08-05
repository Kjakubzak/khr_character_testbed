using System;
using UnityEngine;
using UnityEngine.Rendering;
using UnityGLTF.VisibilityHints;

namespace Samples.VisibilityHints
{
    /// <summary>
    /// Optional sample host that realizes passive visibility predicates with a no-draw material for one camera's
    /// render scope. It restores every material immediately after that camera renders; other cameras may select a
    /// different context for the same character instance.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class VisibilityHintsCameraAdapter : MonoBehaviour
    {
        public Camera TargetCamera;

        private ViewContextController _view;
        private ScopedMaterialVisibilityAdapter _materials;
        private Renderer[] _renderers = Array.Empty<Renderer>();
        private IDisposable _scope;
        private Camera _scopeCamera;

        public void Configure(Camera targetCamera = null)
        {
            TargetCamera = targetCamera;
            _view = GetComponent<ViewContextController>();
            _materials = GetComponent<ScopedMaterialVisibilityAdapter>();
            if (_materials == null) _materials = gameObject.AddComponent<ScopedMaterialVisibilityAdapter>();
            _materials.NoDrawMaterial = InvisibleMaterialInstaller.GetNoDrawMaterial();
            RefreshRenderers();
        }

        public void RefreshRenderers()
            => _renderers = GetComponentsInChildren<Renderer>(true);

        private void OnEnable()
        {
            Camera.onPreCull += BeginBuiltinCamera;
            Camera.onPostRender += EndBuiltinCamera;
            RenderPipelineManager.beginCameraRendering += BeginScriptableCamera;
            RenderPipelineManager.endCameraRendering += EndScriptableCamera;
        }

        private void OnDisable()
        {
            Camera.onPreCull -= BeginBuiltinCamera;
            Camera.onPostRender -= EndBuiltinCamera;
            RenderPipelineManager.beginCameraRendering -= BeginScriptableCamera;
            RenderPipelineManager.endCameraRendering -= EndScriptableCamera;
            EndScope();
        }

        private void BeginBuiltinCamera(Camera camera) => BeginScope(camera);
        private void EndBuiltinCamera(Camera camera) => EndScope(camera);
        private void BeginScriptableCamera(ScriptableRenderContext _, Camera camera) => BeginScope(camera);
        private void EndScriptableCamera(ScriptableRenderContext _, Camera camera) => EndScope(camera);

        private void BeginScope(Camera camera)
        {
            if (!IsTarget(camera) || _scopeCamera == camera) return;
            EndScope();
            if (_view == null || _materials == null) Configure(TargetCamera);
            if (_materials == null || _materials.NoDrawMaterial == null) return;
            _scope = _materials.ApplyForView(
                _renderers,
                _view != null ? _view.ActiveContext : null,
                IsCoreVisible);
            _scopeCamera = camera;
        }

        private void EndScope(Camera camera)
        {
            if (_scopeCamera == camera) EndScope();
        }

        private void EndScope()
        {
            _scope?.Dispose();
            _scope = null;
            _scopeCamera = null;
        }

        private bool IsTarget(Camera camera)
            => camera != null && (TargetCamera != null ? camera == TargetCamera : camera == Camera.main);

        private bool IsCoreVisible(Transform node)
        {
            if (node == null) return false;
            for (var current = node; current != null; current = current.parent)
            {
                if (!current.gameObject.activeSelf) return false;
                if (current == transform) break;
            }
            var renderer = node.GetComponent<Renderer>();
            return renderer == null || renderer.enabled;
        }
    }
}
