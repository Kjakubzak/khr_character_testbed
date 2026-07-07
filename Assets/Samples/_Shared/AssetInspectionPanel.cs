using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityGLTF.KhrCharacter;

namespace Samples.Shared
{
    /// <summary>
    /// Self-building asset-inspection panel, docked bottom-right. Lazily finds the loaded
    /// <see cref="KhrCharacter"/> (controllers load async, so the hub appears after Start) and, on
    /// <see cref="KhrCharacter.WhenReady"/>, lists capability/health, expressions (count + domain breakdown +
    /// vocabularies), skeleton, camera hints, gaze, geometry counts (<see cref="SceneBoundsUtil.Count"/>), and the
    /// source <c>extensionsUsed</c>. Also hosts the Bounds/Skeleton/Wireframe toggles that drive the scene's
    /// <see cref="AssetVisualizer"/>, so the UI and the keyboard share one visualization-state owner. Rebinds on
    /// character swap; no-ops gracefully when nothing is loaded (e.g. the hub scene). RP-agnostic overlay uGUI.
    /// </summary>
    [DisallowMultipleComponent]
    public class AssetInspectionPanel : MonoBehaviour
    {
        private const int MaxNamesShown = 12;

        private DemoUiBuilder _ui;
        private AssetVisualizer _viz;
        private KhrCharacter _hub;
        private bool _ready;

        private Toggle _boundsToggle;
        private Toggle _skeletonToggle;
        private Toggle _wireToggle;

        private void Start()
        {
            _ui = DemoUiBuilder.Create("Inspect", new Vector2(320f, 420f));
            DockBottomRight(_ui);
            Rebuild();
        }

        private void Update()
        {
            // Bind on first appearance / swap; data rows fill in on readiness.
            var hub = Object.FindFirstObjectByType<KhrCharacter>();
            if (hub != _hub)
            {
                _hub = hub;
                _ready = hub != null && hub.IsReady;
                if (hub != null) hub.WhenReady(OnReady);
                if (!_ready) Rebuild(); // when already ready, WhenReady -> OnReady rebuilds synchronously
            }

            // Keep the viz toggles in sync if the keyboard flips the same flags.
            if (_viz != null)
            {
                if (_boundsToggle != null) _boundsToggle.SetIsOnWithoutNotify(_viz.ShowBounds);
                if (_skeletonToggle != null) _skeletonToggle.SetIsOnWithoutNotify(_viz.ShowSkeleton);
                if (_wireToggle != null) _wireToggle.SetIsOnWithoutNotify(_viz.Wireframe);
            }
        }

        private void OnReady(KhrCharacter hub)
        {
            if (hub != _hub) return; // a newer character superseded this one
            _ready = true;
            Rebuild();
        }

        /// <summary>Show/hide the whole inspection panel (keyboard "I").</summary>
        public void ToggleVisible()
        {
            if (_ui != null) _ui.gameObject.SetActive(!_ui.gameObject.activeSelf);
        }

        private void Rebuild()
        {
            if (_ui == null) return;
            _ui.ClearRows();
            _boundsToggle = _skeletonToggle = _wireToggle = null;

            AddVisualizationToggles();

            if (_hub == null)
            {
                _ui.AddLabel("No KHR Character loaded.");
                return;
            }
            if (!_ready)
            {
                _ui.AddLabel("Loading character ...");
                return;
            }

            AddCapabilitySection();
            AddExpressionSection();
            AddSkeletonSection();
            AddCameraHintSection();
            AddGazeSection();
            AddGeometrySection();
            AddExtensionsSection();
        }

        private void AddVisualizationToggles()
        {
            if (_viz == null) _viz = Object.FindFirstObjectByType<AssetVisualizer>();

            _ui.AddLabel("[Visualize]");
            if (_viz == null) { _ui.AddLabel("(no visualizer in scene)"); }
            else
            {
                _boundsToggle = _ui.AddToggle("Show Bounds", on => _viz.ShowBounds = on, _viz.ShowBounds);
                _skeletonToggle = _ui.AddToggle("Show Skeleton", on => _viz.ShowSkeleton = on, _viz.ShowSkeleton);
                _wireToggle = _ui.AddToggle("Wireframe", on => _viz.Wireframe = on, _viz.Wireframe);
            }
            _ui.AddButton("Refresh", Rebuild);
        }

        private void AddCapabilitySection()
        {
            _ui.AddLabel("[Capabilities]");
            var report = _hub.GetHealth();
            if (report == null) { _ui.AddLabel("(no health data)"); return; }

            _ui.AddLabel($"Expressions: {report.ExpressionCount}");
            foreach (var capability in report.Capabilities)
                _ui.AddLabel($"  {capability.Capability}: {capability.Status}");
        }

        private void AddExpressionSection()
        {
            var expressions = _hub.Expressions;
            _ui.AddLabel("[Expressions]");
            if (expressions == null) { _ui.AddLabel("(none)"); return; }

            var handles = expressions.Expressions;
            int morph = 0, joint = 0, texture = 0, binary = 0;
            if (handles != null)
                foreach (var handle in handles)
                {
                    if ((handle.Domains & ExpressionDomain.Morph) != 0) morph++;
                    if ((handle.Domains & ExpressionDomain.Joint) != 0) joint++;
                    if ((handle.Domains & ExpressionDomain.Texture) != 0) texture++;
                    if (handle.IsBinary) binary++;
                }

            _ui.AddLabel($"Count: {expressions.Count}");
            _ui.AddLabel($"  Morph {morph} \u00b7 Joint {joint} \u00b7 Tex {texture}");
            _ui.AddLabel($"  Binary: {binary}");
            AddVocabularies(expressions.VocabularySets);
            AddExpressionNames(handles);
        }

        private void AddVocabularies(IReadOnlyList<string> vocabularies)
        {
            if (vocabularies == null || vocabularies.Count == 0) return;
            _ui.AddLabel($"Vocab sets: {string.Join(", ", vocabularies)}");
        }

        private void AddExpressionNames(IReadOnlyList<ExpressionController.ExpressionHandle> handles)
        {
            if (handles == null || handles.Count == 0) return;
            var names = new List<string>();
            for (int i = 0; i < handles.Count && names.Count < MaxNamesShown; i++)
                if (!string.IsNullOrEmpty(handles[i].Name)) names.Add(handles[i].Name);
            if (names.Count == 0) return;
            string suffix = handles.Count > names.Count ? $" (+{handles.Count - names.Count} more)" : string.Empty;
            _ui.AddLabel($"  {string.Join(", ", names)}{suffix}");
        }

        private void AddSkeletonSection()
        {
            var skeleton = _hub.Skeleton;
            _ui.AddLabel("[Skeleton]");
            if (skeleton == null) { _ui.AddLabel("(none)"); return; }

            int bones = skeleton.Result != null && skeleton.Result.Bones != null ? skeleton.Result.Bones.Count : 0;
            _ui.AddLabel($"Bones: {bones}");
            _ui.AddLabel($"Humanoid: {skeleton.HumanoidAvailable}");
            if (skeleton.RigVocabularies != null && skeleton.RigVocabularies.Count > 0)
                _ui.AddLabel($"Rig: {string.Join(", ", skeleton.RigVocabularies)}");
        }

        private void AddCameraHintSection()
        {
            var hints = _hub.CameraHints;
            _ui.AddLabel("[Camera hints]");
            if (hints == null || hints.Hints == null || hints.Hints.Count == 0) { _ui.AddLabel("(none)"); return; }

            _ui.AddLabel($"Count: {hints.Hints.Count}");
            foreach (var hint in hints.Hints)
            {
                if (hint == null) continue;
                string role = string.IsNullOrEmpty(hint.Role) ? "(role?)" : hint.Role;
                string label = string.IsNullOrEmpty(hint.Label) ? string.Empty : $" \u2014 {hint.Label}";
                _ui.AddLabel($"  {role}{label}");
            }
        }

        private void AddGazeSection()
        {
            var gaze = _hub.Gaze;
            _ui.AddLabel("[Gaze]");
            int targets = gaze != null && gaze.AuthoredTargets != null ? gaze.AuthoredTargets.Count : 0;
            _ui.AddLabel($"Authored targets: {targets}");
        }

        private void AddGeometrySection()
        {
            _ui.AddLabel("[Geometry]");
            var counts = SceneBoundsUtil.Count(_hub.gameObject);
            _ui.AddLabel($"Meshes {counts.Meshes} \u00b7 Tris {counts.Triangles}");
            _ui.AddLabel($"Materials {counts.Materials} \u00b7 Textures {counts.Textures}");
            _ui.AddLabel($"Renderers {counts.Renderers} \u00b7 Nodes {counts.Nodes}");
        }

        private void AddExtensionsSection()
        {
            _ui.AddLabel("[Extensions (source)]");
            var used = CharacterLoader.ReadSourceExtensionsUsed(CharacterLoader.LastLoadedSourcePath);
            _ui.AddLabel(used != null && used.Count > 0 ? $"used: {string.Join(", ", used)}" : "used: -");
        }

        // Free corner (the camera panel docks top-right); mirrors CameraControlPanel.DockRight but pins bottom-right.
        private static void DockBottomRight(DemoUiBuilder ui)
        {
            if (ui == null || ui.Panel == null) return;
            var rt = ui.Panel;
            rt.anchorMin = new Vector2(1f, 0f);
            rt.anchorMax = new Vector2(1f, 0f);
            rt.pivot = new Vector2(1f, 0f);
            rt.anchoredPosition = new Vector2(-10f, 10f);
        }
    }
}
