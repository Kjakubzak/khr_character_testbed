using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityGLTF.KhrCharacter;
using Samples.Shared;

namespace Samples.Characters
{
    /// <summary>
    /// Expressions demo (M1 + M2). Loads SC-FacePlus via the shared loader and, once the character is ready, wires
    /// the <see cref="ExpressionControlPanel"/> so its auto-generated sliders drive <see cref="ExpressionController.SetWeight"/>.
    /// Because SC-FacePlus carries morph, a jaw JOINT, and a TEXTURE (UV-offset + index-swap) expression, the
    /// auto-built rows surface all three domains (M2) alongside the morph sliders (M1).
    ///
    /// Note: the C# namespace is intentionally <c>Samples.Characters</c> (not <c>Samples.KhrCharacter</c>) so the
    /// bare type name <c>KhrCharacter</c> still resolves to the plugin type rather than a same-named namespace.
    /// </summary>
    public class ExpressionsDemoController : DemoControllerBase
    {
        [Tooltip("Absolute path to the character GLB to load. Defaults to the committed synthetic SC-FacePlus.")]
        public string GlbPath;

        private Transform _contentRoot;

        private async void Start()
        {
            bool usingHero = string.IsNullOrEmpty(GlbPath) && CharacterLoader.WouldLoadHero;
            string sceneName = SceneManager.GetActiveScene().name;
            string fallbackFile = DemoCatalog.FallbackFor(sceneName, "SC-FacePlus.glb");
            string fallbackDisplay = DemoCatalog.FallbackDisplayFor(sceneName, "SC-FacePlus");

            var content = new GameObject("LoadedContent");
            content.transform.SetParent(transform, false);
            _contentRoot = content.transform;

            var back = gameObject.AddComponent<BackToHubButton>();

            GameObject scene;
            try
            {
                scene = string.IsNullOrEmpty(GlbPath)
                    ? await CharacterLoader.LoadDemoCharacterAsync(_contentRoot, fallbackFile)
                    : await CharacterLoader.LoadAsync(GlbPath, _contentRoot);
            }
            catch (System.Exception e)
            {
                Debug.LogException(e);
                return;
            }
            if (this == null) return; // scene changed / object destroyed mid-import
            if (scene == null) return;

            FrameLoaded(scene);

            var hub = scene.GetComponent<KhrCharacter>();
            if (hub == null) return;

            // ExpressionControlPanel.Bind builds rows on WhenReady; the character is already ready post-load, so
            // Ui exists synchronously afterwards and we can append the N1/N2 controls + a Back-to-Hub button.
            var panel = gameObject.AddComponent<ExpressionControlPanel>();
            panel.Bind(hub);
            if (panel.Ui != null) panel.Ui.AddLabel(CharacterLoader.DemoCharacterBlurb(usingHero, fallbackDisplay));
            Caveats.Render(panel.Ui, Caveat.Draft, Caveat.BlendModeRuntimeOnly);

            // Guarantee a way back even when the character has no expression controller (A18):
            if (panel.Ui != null) panel.Ui.AddButton("Back to Hub", back.GoToHub);

            var controller = hub.Expressions;
            if (panel.Ui != null && controller != null)
                BuildNSeriesControls(panel.Ui, controller);
        }

        // N1 (additive vs override) + N2 (mask + vocabulary) controls, appended below the auto-built expression rows.
        private static void BuildNSeriesControls(DemoUiBuilder ui, ExpressionController controller)
        {
            // N1: "aa" and "jawOpen" drive the SAME blendshape. Additive sums them; Override takes the winner.
            // Override is RUNTIME-ONLY (see Caveats.BlendModeRuntimeOnly) — it is not written to the glTF wire.
            ui.AddLabel("N1: 'aa' + 'jawOpen' drive one blendshape. Raise both sliders above, then toggle:");
            ui.AddToggle($"Override (winner-takes) - runtime only [{Caveats.Id(Caveat.BlendModeRuntimeOnly)}]", on =>
                SetPairBlendMode(controller, on ? ExpressionBlendMode.Override : ExpressionBlendMode.Additive), false);

            // N2: 'happy' blend-masks 'aa' (raise both 'happy' and 'aa' above to watch aa attenuate); plus a
            // vocabulary slider per mapping target (SetWeightByVocabulary distributes to its source expressions).
            ui.AddLabel("N2: 'happy' blend-masks 'aa'. Vocabulary targets distribute to several expressions:");
            foreach (var setName in controller.VocabularySets)
            {
                string vocab = setName;
                foreach (var target in controller.VocabularyExpressions(vocab))
                {
                    string targetName = target;
                    ui.AddSlider($"Vocab {vocab}/{targetName}",
                        v => controller.SetWeightByVocabulary(vocab, targetName, v), 0f, 1f, 0f);
                }
            }
        }

        private static void SetPairBlendMode(ExpressionController controller, ExpressionBlendMode mode)
        {
            var set = controller?.Set;
            if (set?.Expressions == null) return;
            foreach (var track in set.Expressions)
                if (track != null && (track.Name == "aa" || track.Name == "jawOpen"))
                    track.BlendMode = mode;
        }

        private void FrameLoaded(GameObject scene)
        {
            var rig = Object.FindFirstObjectByType<OrbitCameraRig>();
            if (rig == null || scene == null) return;
            if (!SceneBoundsUtil.TryAggregate(scene, out var bounds)) return;

            // Expressions is a head shot: zoom onto the character's head (still facing the camera).
            if (OrbitCameraRig.TryGetHeadFocus(scene, bounds, out var headCenter, out var headRadius))
                rig.FrameAndFaceHead(bounds, scene.transform, headCenter, headRadius);
            else
                rig.FrameAndFace(bounds, scene.transform);
        }
    }
}
