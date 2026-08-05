using System.IO;
using System.Text;
using System.Threading.Tasks;
using GLTF.Schema;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using UnityGLTF.KhrCharacter;
using Samples.Shared;

namespace Samples.Characters
{
    /// <summary>
    /// RoundTrip demo. The default SC-Body exercises lossless skeleton/reference-pose/camera export and re-import.
    /// Imported expression assets demonstrate the fail-closed boundary: their passive response data cannot be
    /// silently replaced by the legacy controller's lossy projection during export.
    /// </summary>
    public class RoundTripController : DemoControllerBase
    {
        public string GlbPath;

        private DemoUiBuilder _ui;
        private KhrCharacter _a;
        private KhrCharacter _b;
        private Transform _bRoot;
        private byte[] _glb;
        private GLTFRoot _exportedRoot;
        private Text _neutrality;
        private Text _diff;
        private bool _busy;

        [Tooltip("Optional external character used to inspect passive-expression export safety.")]
        public string ExternalGlbPath;
        private Transform _externalRoot;
        private HealthPanel _externalHealthPanel;
        private Text _sourceUsed;
        private Text _externalHealth;
        private Text _reexportUsed;

        private async void Start()
        {
            bool usingHero = false;
            string sceneName = SceneManager.GetActiveScene().name;
            string fallbackFile = DemoCatalog.FallbackFor(sceneName, "SC-Body.glb");
            string fallbackDisplay = DemoCatalog.FallbackDisplayFor(sceneName, "SC-Body");

            _ui = CreatePanel("Round Trip");
            _ui.AddLabel("Round-trip lossless character data; imported expressions fail closed until passive export exists.");
            _ui.AddLabel(CharacterLoader.DemoCharacterBlurb(usingHero, fallbackDisplay));
            _ui.AddButton("Export A (in memory)", ExportA);
            _ui.AddButton("Re-import as B", () => { _ = ReimportB(); });
            _ui.AddButton("Save GLB + open web viewer [N6]", SaveAndOpenViewer);
            _neutrality = _ui.AddLabel(string.Empty);
            _diff = _ui.AddLabel(string.Empty);

            var aRoot = new GameObject("CharacterA");
            aRoot.transform.SetParent(transform, false);

            GameObject sceneA;
            try
            {
                string path = string.IsNullOrEmpty(GlbPath)
                    ? CharacterLoader.SyntheticPath(fallbackFile)
                    : GlbPath;
                sceneA = await CharacterLoader.LoadAsync(path, aRoot.transform);
            }
            catch (System.Exception e) { Debug.LogException(e); _diff.text = "Load failed: " + e.Message; return; }
            if (this == null) return; // scene changed / object destroyed mid-import
            if (sceneA == null) { _diff.text = "Load failed."; return; }

            _a = sceneA.GetComponent<KhrCharacter>();
            FrameAll();

            if (_a != null)
            {
                var healthText = _ui.AddLabel(string.Empty);
                gameObject.AddComponent<HealthPanel>().Bind(_a, healthText);
            }

            Caveats.Render(_ui, Caveat.Draft, Caveat.CameraProjectionOffWire, Caveat.OneCharacterPerDocument);

            // Inspect a VRM-origin character through KHR_character and demonstrate the passive export boundary.
            if (string.IsNullOrEmpty(ExternalGlbPath)) ExternalGlbPath = ResolveExternalDefault();
            _ui.AddLabel("Inspect a VRM-origin character; passive expressions block lossy re-export:");
            _ui.AddInputField("External path", ExternalGlbPath, v => ExternalGlbPath = v);
            _ui.AddButton("Load + check export safety", () => { _ = CheckExternalExportSafety(); });
            _sourceUsed = _ui.AddLabel(string.Empty);
            _externalHealth = _ui.AddLabel(string.Empty);
            _reexportUsed = _ui.AddLabel(string.Empty);
            // Back-to-Hub is guaranteed by DemoControllerBase (armed in CreatePanel).
        }

        // Synchronous: SaveGLBToByteArray runs the export pipeline in-process.
        private void ExportA()
        {
            if (_a == null) { _neutrality.text = "Character A is not loaded."; return; }
            try
            {
                _glb = CharacterLoader.ExportToGlb(_a.gameObject, out _exportedRoot);
                ShowExportProfile();
            }
            catch (System.InvalidOperationException e)
            {
                _glb = null;
                _exportedRoot = null;
                _neutrality.text = "Export blocked: " + e.Message;
            }
        }

        // N6: write the in-memory GLB to a temp file and open a public web glTF viewer to drag it into. Public
        // viewers render the base glTF and ignore unsupported KHR_character extensions (the fallback point).
        private void SaveAndOpenViewer()
        {
            if (_glb == null) { _neutrality.text = "Export A first."; return; }
            string path = Path.Combine(Application.temporaryCachePath, "khr-character-export.glb");
            try { File.WriteAllBytes(path, _glb); }
            catch (System.Exception e) { Debug.LogException(e); _neutrality.text = "Save failed: " + e.Message; return; }

            Debug.Log($"[Samples] Wrote exported GLB: {path}");
            _neutrality.text = $"Saved: {path}\nOpening a public glTF viewer - drag the file in.\n" +
                               "(Public viewers render the base mesh and ignore unsupported KHR_character data.)";
            Application.OpenURL("https://github.khronos.org/glTF-Sample-Viewer-Release/");
        }

        private static string ResolveExternalDefault()
        {
            return CharacterLoader.HeroIsRealGlb
                ? CharacterLoader.HeroAbsolutePath
                : CharacterLoader.SyntheticPath("SC-PseudoVRM.glb");
        }

        // Load an external character via the KHR path, show its health, and report whether export is lossless.
        private async Task CheckExternalExportSafety()
        {
            if (_busy) return;
            _busy = true;
            try
            {
                string path = string.IsNullOrEmpty(ExternalGlbPath) ? ResolveExternalDefault() : ExternalGlbPath;

                var sourceUsed = CharacterLoader.ReadSourceExtensionsUsed(path);
                _sourceUsed.text = "SOURCE extensionsUsed: " +
                    (sourceUsed.Count > 0 ? string.Join(", ", sourceUsed) : "(none / unreadable)");

                if (_externalRoot == null)
                {
                    var go = new GameObject("ExternalCharacter");
                    go.transform.SetParent(transform, false);
                    go.transform.localPosition = new Vector3(3f, 0f, 0f);
                    _externalRoot = go.transform;
                }
                for (int i = _externalRoot.childCount - 1; i >= 0; i--) Destroy(_externalRoot.GetChild(i).gameObject);

                GameObject scene;
                try { scene = await CharacterLoader.LoadAsync(path, _externalRoot); }
                catch (System.Exception e) { Debug.LogException(e); _reexportUsed.text = "Import failed: " + e.Message; return; }
                if (this == null) return; // scene changed / object destroyed mid-import
                if (scene == null) { _reexportUsed.text = "Import failed (no scene)."; return; }

                var hub = scene.GetComponent<KhrCharacter>();
                if (hub == null) { _reexportUsed.text = "Imported, but no KHR Character data (vendor-only asset?)."; return; }

                if (_externalHealthPanel == null) _externalHealthPanel = gameObject.AddComponent<HealthPanel>();
                _externalHealthPanel.Bind(hub, _externalHealth);

                try
                {
                    CharacterLoader.ExportToGlb(hub.gameObject, out var root);
                    var reUsed = root != null ? root.ExtensionsUsed : null;
                    var reReq = root != null ? root.ExtensionsRequired : null;
                    bool requiredEmpty = reReq == null || reReq.Count == 0;
                    _reexportUsed.text =
                        "RE-EXPORT extensionsUsed: " + (reUsed != null && reUsed.Count > 0 ? string.Join(", ", reUsed) : "(none)") +
                        "\nextensionsRequired: " + (requiredEmpty ? "(empty)" : string.Join(", ", reReq));
                }
                catch (System.InvalidOperationException e)
                {
                    _reexportUsed.text = "EXPORT BLOCKED (no lossy projection): " + e.Message;
                }
            }
            finally { _busy = false; }
        }

        private async Task ReimportB()
        {
            if (_glb == null) { _neutrality.text = "Export A first."; return; }
            if (_busy) return;
            _busy = true;
            try
            {
                if (_bRoot == null)
                {
                    var go = new GameObject("CharacterB");
                    go.transform.SetParent(transform, false);
                    go.transform.localPosition = new Vector3(1.5f, 0f, 0f);
                    _bRoot = go.transform;
                }
                for (int i = _bRoot.childCount - 1; i >= 0; i--) Destroy(_bRoot.GetChild(i).gameObject);

                GameObject sceneB;
                try { sceneB = await CharacterLoader.LoadFromBytesAsync(_glb, _bRoot); }
                catch (System.Exception e) { Debug.LogException(e); _diff.text = "Re-import failed: " + e.Message; return; }
                if (this == null) return; // scene changed / object destroyed mid-import
                if (sceneB == null) { _diff.text = "Re-import failed."; return; }

                _b = sceneB.GetComponent<KhrCharacter>();
                ShowDiff();
            }
            finally { _busy = false; }
        }

        private void ShowExportProfile()
        {
            var used = _exportedRoot != null ? _exportedRoot.ExtensionsUsed : null;
            var required = _exportedRoot != null ? _exportedRoot.ExtensionsRequired : null;
            bool requiredEmpty = required == null || required.Count == 0;

            var sb = new StringBuilder();
            sb.AppendLine($"Exported {_glb.Length} bytes.");
            sb.AppendLine("extensionsUsed: " + (used != null && used.Count > 0 ? string.Join(", ", used) : "(none)"));
            sb.AppendLine("extensionsRequired: " + (requiredEmpty ? "(empty) - optional fallback retained" : string.Join(", ", required)));
            _neutrality.text = sb.ToString();
        }

        private void ShowDiff()
        {
            var a = _a != null ? _a.GetHealth() : null;
            var b = _b != null ? _b.GetHealth() : null;

            var sb = new StringBuilder();
            sb.AppendLine("A vs B (re-imported):");
            sb.AppendLine($"  KhrCharacter: {(_a != null)} vs {(_b != null)}");
            sb.AppendLine($"  Expressions: {(a != null ? a.ExpressionCount : 0)} vs {(b != null ? b.ExpressionCount : 0)}");
            sb.AppendLine($"  Capabilities: {(a != null ? a.Capabilities.Count : 0)} vs {(b != null ? b.Capabilities.Count : 0)}");
            _diff.text = sb.ToString();
        }

        private void FrameAll()
        {
            var rig = Object.FindFirstObjectByType<OrbitCameraRig>();
            if (rig == null) return;
            var bounds = new Bounds(new Vector3(0.75f, 0f, 0f), new Vector3(3.5f, 2f, 2f));
            rig.FrameAndFace(bounds, _a != null ? _a.transform : null);
        }
    }
}
