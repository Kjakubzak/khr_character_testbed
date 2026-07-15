using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.UI;
using UnityGLTF.KhrCharacter;
using Samples.Shared;

namespace Samples.GlbViewer
{
    /// <summary>
    /// M1 — runtime "load + inspect a GLB". Loads a GLB by absolute path (default: the local hero if
    /// present, else the first entry in the preset dropdown), frames it with the orbit camera, and
    /// lists discovered capabilities from <see cref="KhrCharacter.GetHealth"/>. A plain glTF with no
    /// character hub shows "no KHR Character data". Everything is null-checked.
    ///
    /// The preset dropdown enumerates the <see cref="CharacterLoader.AssetSourceCatalog"/>: every
    /// registered directory contributes its ``.glb`` / ``.gltf`` files, prefixed by the source's
    /// label. Two default sources auto-register (Synthetic, FromBlender); users can add ad-hoc
    /// directories at runtime via the "Add source folder" panel — persisted per-user in PlayerPrefs
    /// so the folder survives session restart. No hardcoded preset list here.
    /// </summary>
    public class GlbViewerController : DemoControllerBase
    {
        [Tooltip("Absolute path to the GLB/glTF to load. Defaults to the local hero if present, else the first preset in the catalog.")]
        public string GlbPath;

        private DemoUiBuilder _ui;
        private InputField _pathField;
        private Dropdown _presetDropdown;
        private InputField _newSourceLabelField;
        private InputField _newSourceDirField;
        private Text _sourceStatus;
        private Text _status;
        private Text _capabilities;
        private Transform _contentRoot;
        private GameObject _current;
        private bool _loading;

        // Parallel arrays: dropdown index → path. Rebuilt when the catalog changes.
        private readonly List<string> _presetPaths = new List<string>();

        private async void Start()
        {
            var content = new GameObject("LoadedContent");
            content.transform.SetParent(transform, false);
            _contentRoot = content.transform;

            _ui = CreatePanel("GLB Viewer");
            _ui.AddLabel("Load a glTF/GLB and inspect its KHR Character capabilities.");

            // Preset dropdown — populated from the AssetSourceCatalog. The initial options list
            // determines the default selection: prefer the hero if present, else the first entry.
            _presetDropdown = _ui.AddDropdown("Preset", BuildPresetOptions(), OnPresetChanged, 0);

            _pathField = _ui.AddInputField("Path", string.Empty, value => GlbPath = value);
            _ui.AddButton("Load", () => { _ = LoadAndShow(GlbPath); });
            _status = _ui.AddLabel(string.Empty);
            _capabilities = _ui.AddLabel(string.Empty);
            Caveats.Render(_ui, Caveat.Draft);

            // ── Add-source panel: any generalized folder can be added at runtime ─────
            _ui.AddLabel("── Asset sources ──");
            _ui.AddButton("Refresh preset list", RefreshPresetDropdown);
            _newSourceLabelField = _ui.AddInputField("New label", string.Empty, _ => { });
            _newSourceDirField = _ui.AddInputField("New directory", string.Empty, _ => { });
            _ui.AddButton("Add source folder", OnAddSource);
            _sourceStatus = _ui.AddLabel(string.Empty);

            // Initial load: use GlbPath if the user pre-set one; else the current dropdown selection.
            if (string.IsNullOrEmpty(GlbPath))
            {
                GlbPath = CharacterLoader.HeroIsRealGlb
                    ? CharacterLoader.HeroAbsolutePath
                    : (_presetPaths.Count > 0 ? _presetPaths[0] : string.Empty);
            }
            if (_pathField != null) _pathField.text = GlbPath;

            if (!string.IsNullOrEmpty(GlbPath) && File.Exists(GlbPath))
                await LoadAndShow(GlbPath);
            else if (_status != null)
                _status.text = "No preset asset found. Add a source folder to point at your .glb files.";
        }

        // Build the dropdown option list from the catalog. Populates ``_presetPaths`` in parallel so
        // OnPresetChanged can look up the path by dropdown index.
        private List<string> BuildPresetOptions()
        {
            var options = new List<string>();
            _presetPaths.Clear();

            // Hero family as special "single file" entries (outside the directory-based catalog):
            // the base hero + each per-role visibility-hint variant present on disk.
            foreach (var (label, path) in CharacterLoader.EnumerateHeroFiles())
            {
                options.Add(label);
                _presetPaths.Add(path);
            }

            // Every source contributes its files, one dropdown entry per file. Files are labeled
            // ``<source label>: <filename-without-ext>`` so the origin is obvious.
            foreach (var (source, path) in CharacterLoader.AssetSourceCatalog.EnumerateAll())
            {
                options.Add($"{source.Label}: {Path.GetFileNameWithoutExtension(path)}");
                _presetPaths.Add(path);
            }

            if (options.Count == 0)
                options.Add("(no assets discovered — add a source folder)");

            return options;
        }

        private void OnPresetChanged(int index)
        {
            if (index < 0 || index >= _presetPaths.Count) return;
            var path = _presetPaths[index];
            GlbPath = path;
            if (_pathField != null) _pathField.text = path;
            _ = LoadAndShow(path);
        }

        private void OnAddSource()
        {
            if (_newSourceLabelField == null || _newSourceDirField == null) return;
            string label = _newSourceLabelField.text?.Trim();
            string dir = _newSourceDirField.text?.Trim();

            if (string.IsNullOrEmpty(label) || string.IsNullOrEmpty(dir))
            {
                if (_sourceStatus != null) _sourceStatus.text = "Add: label and directory both required.";
                return;
            }
            if (!Directory.Exists(dir))
            {
                if (_sourceStatus != null) _sourceStatus.text = $"Add: '{dir}' does not exist.";
                return;
            }

            bool added = CharacterLoader.AssetSourceCatalog.TryRegister(label, dir, autoDetected: false);
            if (!added)
            {
                if (_sourceStatus != null) _sourceStatus.text = $"Add: '{dir}' already registered.";
                return;
            }

            // Refresh the dropdown so the new source's contents appear.
            RefreshPresetDropdown();
            if (_sourceStatus != null)
                _sourceStatus.text = $"Added '{label}' → {dir} (persisted to PlayerPrefs).";
            _newSourceLabelField.text = string.Empty;
            _newSourceDirField.text = string.Empty;
        }

        private void RefreshPresetDropdown()
        {
            if (_presetDropdown == null) return;
            _presetDropdown.ClearOptions();
            _presetDropdown.AddOptions(BuildPresetOptions());
        }

        private async Task LoadAndShow(string path)
        {
            if (_loading) return;
            _loading = true;
            try
            {
                // Fresh-clone guard: never feed a missing file or an un-smudged Git-LFS pointer to the importer —
                // show an actionable hint instead of a raw parse exception, and keep the current view intact.
                string problem = CharacterLoader.DescribeUnloadable(path);
                if (problem != null)
                {
                    if (_status != null) _status.text = problem;
                    return;
                }

                if (_current != null) Destroy(_current);
                if (_capabilities != null) _capabilities.text = string.Empty;
                if (_status != null) _status.text = $"Loading {System.IO.Path.GetFileName(path)} ...";

                GameObject scene;
                try
                {
                    scene = await CharacterLoader.LoadAsync(path, _contentRoot);
                }
                catch (System.Exception e)
                {
                    if (_status != null) _status.text = "Load failed: " + e.Message;
                    Debug.LogException(e);
                    return;
                }

                if (this == null) return; // scene changed / object destroyed mid-import

                if (scene == null)
                {
                    if (_status != null) _status.text = "Load failed (no scene produced).";
                    return;
                }

                _current = scene;
                FrameScene(scene);

                var hub = scene.GetComponent<KhrCharacter>();
                if (hub == null)
                {
                    if (_status != null) _status.text = "Loaded. No KHR Character data.";
                    return;
                }

                if (_status != null) _status.text = "Loaded a KHR Character.";
                hub.WhenReady(PopulateCapabilities);
            }
            finally
            {
                _loading = false;
            }
        }

        private void PopulateCapabilities(KhrCharacter hub)
        {
            if (hub == null || _capabilities == null) return;
            var report = hub.GetHealth();
            if (report == null) { _capabilities.text = string.Empty; return; }

            var sb = new System.Text.StringBuilder();
            sb.AppendLine($"Expressions: {report.ExpressionCount}");
            foreach (var capability in report.Capabilities)
                sb.AppendLine($"  {capability.Capability}: {capability.Status}");
            _capabilities.text = sb.ToString();
        }
    }
}
