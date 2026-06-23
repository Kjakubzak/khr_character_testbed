using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.UI;
using UnityGLTF.KhrCharacter;
using Samples.Shared;

namespace Samples.GlbViewer
{
    /// <summary>
    /// M1 — runtime "load + inspect a GLB". Loads a GLB by absolute path (default: the committed SC-Face), frames
    /// it with the orbit camera, and lists discovered capabilities from <see cref="KhrCharacter.GetHealth"/>. A
    /// plain glTF with no character hub shows "no KHR Character data". Everything is null-checked.
    /// </summary>
    public class GlbViewerController : MonoBehaviour
    {
        [Tooltip("Absolute path to the GLB/glTF to load. Defaults to the committed synthetic SC-Face.")]
        public string GlbPath;

        private DemoUiBuilder _ui;
        private InputField _pathField;
        private Text _status;
        private Text _capabilities;
        private Transform _contentRoot;
        private GameObject _current;
        private bool _loading;

        private async void Start()
        {
            // Default the initial load to the local hero when present, else the committed SC-Face.
            if (string.IsNullOrEmpty(GlbPath))
                GlbPath = CharacterLoader.HeroExists ? CharacterLoader.HeroAbsolutePath : CharacterLoader.SyntheticPath("SC-Face.glb");

            var content = new GameObject("LoadedContent");
            content.transform.SetParent(transform, false);
            _contentRoot = content.transform;

            _ui = DemoUiBuilder.Create("GLB Viewer");
            _ui.AddLabel("Load a glTF/GLB and inspect its KHR Character capabilities.");
            _pathField = _ui.AddInputField("Path", GlbPath, value => GlbPath = value);
            _ui.AddButton("Load", () => { _ = LoadAndShow(GlbPath); });
            _status = _ui.AddLabel(string.Empty);
            _capabilities = _ui.AddLabel(string.Empty);

            var back = gameObject.AddComponent<BackToHubButton>();
            _ui.AddButton("Back to Hub", back.GoToHub);

            await LoadAndShow(GlbPath);
        }

        private async Task LoadAndShow(string path)
        {
            if (_loading) return;
            _loading = true;
            try
            {
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

                if (scene == null)
                {
                    if (_status != null) _status.text = "Load failed (no scene produced).";
                    return;
                }

                _current = scene;
                FrameLoaded(scene);

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

        private void FrameLoaded(GameObject scene)
        {
            var rig = Object.FindFirstObjectByType<OrbitCameraRig>();
            if (rig == null || scene == null) return;
            var renderers = scene.GetComponentsInChildren<Renderer>();
            if (renderers.Length == 0) return;
            var bounds = renderers[0].bounds;
            for (int i = 1; i < renderers.Length; i++) bounds.Encapsulate(renderers[i].bounds);
            rig.Frame(bounds);
        }

        private void PopulateCapabilities(KhrCharacter hub)
        {
            if (hub == null || _capabilities == null) return;
            var report = hub.GetHealth();
            if (report == null) { _capabilities.text = string.Empty; return; }

            var sb = new System.Text.StringBuilder();
            sb.AppendLine($"Expressions: {report.ExpressionCount}");
            sb.AppendLine($"Skeleton direction: {report.SkeletonDirection}");
            foreach (var capability in report.Capabilities)
                sb.AppendLine($"  {capability.Capability}: {capability.Status}");
            _capabilities.text = sb.ToString();
        }
    }
}
