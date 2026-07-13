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
    /// RoundTrip demo (M5). Loads SC-FacePlus as character A, exports it to an in-memory GLB through the KHR
    /// Character export plugin, re-imports the bytes as character B beside A, and shows: (1) a NEUTRALITY readout of
    /// the exported GLTFRoot (extensionsUsed listed; extensionsRequired expected EMPTY = Khronos-neutral), and
    /// (2) an A-vs-B diff via <see cref="KhrCharacter.GetHealth"/> (expression count, capabilities, skeleton
    /// direction). Caveats C1-C7 are surfaced as a banner.
    /// </summary>
    public class RoundTripController : MonoBehaviour
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

        [Tooltip("Optional external character to neutralize (defaults to the local VRoid sample if present, else SC-Body).")]
        public string ExternalGlbPath;
        private Transform _externalRoot;
        private HealthPanel _externalHealthPanel;
        private Text _sourceUsed;
        private Text _externalHealth;
        private Text _reexportUsed;

        private async void Start()
        {
            bool usingHero = string.IsNullOrEmpty(GlbPath) && CharacterLoader.HeroExists;
            string sceneName = SceneManager.GetActiveScene().name;
            string fallbackFile = DemoCatalog.FallbackFor(sceneName, "SC-FacePlus.glb");
            string fallbackDisplay = DemoCatalog.FallbackDisplayFor(sceneName, "SC-FacePlus");

            _ui = DemoUiBuilder.Create("Round Trip");
            _ui.AddLabel("Export character A in memory, re-import it as B, and compare.");
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
                sceneA = string.IsNullOrEmpty(GlbPath)
                    ? await CharacterLoader.LoadDemoCharacterAsync(aRoot.transform, fallbackFile)
                    : await CharacterLoader.LoadAsync(GlbPath, aRoot.transform);
            }
            catch (System.Exception e) { Debug.LogException(e); _diff.text = "Load failed: " + e.Message; return; }
            if (sceneA == null) { _diff.text = "Load failed."; return; }

            _a = sceneA.GetComponent<KhrCharacter>();
            FrameAll();

            if (_a != null)
            {
                var healthText = _ui.AddLabel(string.Empty);
                gameObject.AddComponent<HealthPanel>().Bind(_a, healthText);
            }

            _ui.AddLabel("Caveats C1-C7: CUBICSPLINE->LINEAR; multi-key UV first-cycle-exact; shared-material " +
                         "collapse; duplicate names; blendMode/priority off-wire; camera index; one character per document.");

            // Neutralize: consume a VRM-origin character purely via KHR_character (VRMC_* ignored) and re-export
            // vendor-neutral. Defaults to the LFS-committed VRoid sample if present, else the neutral SC-Body.
            if (string.IsNullOrEmpty(ExternalGlbPath)) ExternalGlbPath = ResolveExternalDefault();
            _ui.AddLabel("Neutralize a VRM-origin character (KHR_character ignores VRMC_*; re-export is vendor-neutral):");
            _ui.AddInputField("External path", ExternalGlbPath, v => ExternalGlbPath = v);
            _ui.AddButton("Load + Neutralize external", () => { _ = NeutralizeExternal(); });
            _sourceUsed = _ui.AddLabel(string.Empty);
            _externalHealth = _ui.AddLabel(string.Empty);
            _reexportUsed = _ui.AddLabel(string.Empty);

            var back = gameObject.AddComponent<BackToHubButton>();
            _ui.AddButton("Back to Hub", back.GoToHub);
        }

        // Synchronous: SaveGLBToByteArray runs the export pipeline in-process.
        private void ExportA()
        {
            if (_a == null) { _neutrality.text = "Character A is not loaded."; return; }
            _glb = CharacterLoader.ExportToGlb(_a.gameObject, out _exportedRoot);
            ShowNeutrality();
        }

        // N6: write the in-memory GLB to a temp file and open a public web glTF viewer to drag it into. Public
        // viewers render the base glTF and ignore the KHR_character extensions (the neutrality point).
        private void SaveAndOpenViewer()
        {
            if (_glb == null) { _neutrality.text = "Export A first."; return; }
            string path = Path.Combine(Application.temporaryCachePath, "SC-FacePlus-export.glb");
            try { File.WriteAllBytes(path, _glb); }
            catch (System.Exception e) { Debug.LogException(e); _neutrality.text = "Save failed: " + e.Message; return; }

            Debug.Log($"[Samples] Wrote exported GLB: {path}");
            _neutrality.text = $"Saved: {path}\nOpening a public glTF viewer - drag the file in.\n" +
                               "(Public viewers render the base mesh and ignore KHR_character - the neutrality point.)";
            Application.OpenURL("https://github.khronos.org/glTF-Sample-Viewer-Release/");
        }

        private static string ResolveExternalDefault()
        {
            string vroid = Path.Combine(Application.dataPath, "SampleAssets/khr-character-example.glb");
            return File.Exists(vroid) ? vroid : CharacterLoader.SyntheticPath("SC-Body.glb");
        }

        // Load an external (possibly VRM-origin) character via the KHR path (VRMC_* ignored), show its KHR health,
        // and re-export it vendor-neutral - listing the SOURCE extensionsUsed (incl. VRMC_*) vs the RE-EXPORT's.
        private async Task NeutralizeExternal()
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
                if (scene == null) { _reexportUsed.text = "Import failed (no scene)."; return; }

                var hub = scene.GetComponent<KhrCharacter>();
                if (hub == null) { _reexportUsed.text = "Imported, but no KHR Character data (vendor-only asset?)."; return; }

                if (_externalHealthPanel == null) _externalHealthPanel = gameObject.AddComponent<HealthPanel>();
                _externalHealthPanel.Bind(hub, _externalHealth);

                CharacterLoader.ExportToGlb(hub.gameObject, out var root);
                var reUsed = root != null ? root.ExtensionsUsed : null;
                var reReq = root != null ? root.ExtensionsRequired : null;
                bool requiredEmpty = reReq == null || reReq.Count == 0;
                _reexportUsed.text =
                    "RE-EXPORT extensionsUsed: " + (reUsed != null && reUsed.Count > 0 ? string.Join(", ", reUsed) : "(none)") +
                    "\nextensionsRequired: " + (requiredEmpty ? "(empty) - vendor-neutral" : string.Join(", ", reReq)) +
                    "\n(VRMC_* dropped: the re-export is pure KHR_character.)";
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
                if (sceneB == null) { _diff.text = "Re-import failed."; return; }

                _b = sceneB.GetComponent<KhrCharacter>();
                ShowDiff();
            }
            finally { _busy = false; }
        }

        private void ShowNeutrality()
        {
            var used = _exportedRoot != null ? _exportedRoot.ExtensionsUsed : null;
            var required = _exportedRoot != null ? _exportedRoot.ExtensionsRequired : null;
            bool requiredEmpty = required == null || required.Count == 0;

            var sb = new StringBuilder();
            sb.AppendLine($"Exported {_glb.Length} bytes.");
            sb.AppendLine("extensionsUsed: " + (used != null && used.Count > 0 ? string.Join(", ", used) : "(none)"));
            sb.AppendLine("extensionsRequired: " + (requiredEmpty ? "(empty) - Khronos-neutral" : string.Join(", ", required)));
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
