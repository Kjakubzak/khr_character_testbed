using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using UnityGLTF.KhrCharacter;
using Samples.Shared;

namespace Samples.Characters
{
    /// <summary>
    /// Health demo. Shows a character's live capability health (Active / Degraded / Inert) and expression count via
    /// <see cref="HealthPanel"/> (driven by <see cref="KhrCharacter.GetHealth"/>). A picker switches between the
    /// default demo character and shipped fixtures chosen to make the tri-state observable: SC-Body (a full skeleton
    /// mapping → SkeletonMapping Active), SC-Partial (a minimal character → most capabilities simply absent), and
    /// SC-Degraded (a skeleton mapping missing a required bone → SkeletonMapping Degraded).
    /// </summary>
    public class HealthController : DemoControllerBase
    {
        public string GlbPath;

        // (dropdown label, synthetic fixture file — null means "the default demo character / hero").
        private static readonly (string Label, string File)[] Choices =
        {
            ("Default demo character", null),
            ("SC-Body (skeleton mapping: Active)", "SC-Body.glb"),
            ("SC-Partial (minimal: capabilities absent)", "SC-Partial.glb"),
            ("SC-Degraded (skeleton mapping: Degraded)", "SC-Degraded.glb"),
        };

        private DemoUiBuilder _ui;
        private Text _healthText;
        private Transform _root;
        private HealthPanel _panel;
        private bool _loading;

        private async void Start()
        {
            _ui = CreatePanel("Health");
            _ui.AddLabel("Capability health for the loaded character (Active / Degraded / Inert).");
            _ui.AddLabel("Pick a character to see the tri-state vary: SC-Partial omits capabilities; " +
                         "SC-Degraded's skeleton mapping is missing a required bone.");

            var options = new List<string>();
            foreach (var c in Choices) options.Add(c.Label);
            _ui.AddDropdown("Character", options, i => { _ = LoadChoice(i); }, 0);
            _healthText = _ui.AddLabel("Loading ...");

            Caveats.Render(_ui, Caveat.Draft, Caveat.SkeletonGracefulDegrade, Caveat.HeroNonCommercial);

            await LoadChoice(0);
        }

        private async Task LoadChoice(int index)
        {
            if (_loading || index < 0 || index >= Choices.Length) return;
            _loading = true;
            try
            {
                // Tear down the previously-inspected character + its panel before loading the next.
                if (_panel != null) { Destroy(_panel); _panel = null; }
                if (_root != null) { Destroy(_root.gameObject); _root = null; }

                var rootGo = new GameObject("CharacterRoot");
                rootGo.transform.SetParent(transform, false);
                _root = rootGo.transform;

                string sceneName = SceneManager.GetActiveScene().name;
                string fallbackFile = DemoCatalog.FallbackFor(sceneName, "SC-Face.glb");

                GameObject scene;
                try
                {
                    if (index == 0)
                    {
                        scene = string.IsNullOrEmpty(GlbPath)
                            ? await CharacterLoader.LoadDemoCharacterAsync(_root, fallbackFile)
                            : await CharacterLoader.LoadAsync(GlbPath, _root);
                    }
                    else
                    {
                        string path = CharacterLoader.SyntheticPath(Choices[index].File);
                        string problem = CharacterLoader.DescribeUnloadable(path);
                        if (problem != null) { _healthText.text = problem; return; }
                        scene = await CharacterLoader.LoadAsync(path, _root);
                    }
                }
                catch (System.Exception e)
                {
                    Debug.LogException(e);
                    if (this != null && _healthText != null) _healthText.text = "Load failed: " + e.Message;
                    return;
                }
                if (this == null) return; // scene changed / object destroyed mid-import
                if (scene == null) { _healthText.text = "Load failed."; return; }

                FrameScene(scene, new Bounds(new Vector3(0f, 0.5f, 0f), new Vector3(2f, 2f, 2f)));

                var hub = scene.GetComponent<KhrCharacter>();
                if (hub != null) { _panel = gameObject.AddComponent<HealthPanel>(); _panel.Bind(hub, _healthText); }
                else _healthText.text = "No KHR Character data on this asset.";
            }
            finally { _loading = false; }
        }
    }
}
