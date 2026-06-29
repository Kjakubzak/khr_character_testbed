using System.Threading.Tasks;
using UnityEngine;
using UnityGLTF.KhrCharacter;
using Samples.Shared;

namespace Samples.Characters
{
    /// <summary>
    /// Health demo. Loads SC-FacePlus and shows its live capability health (Active / Degraded / Inert), expression
    /// count, and skeleton-mapping direction via <see cref="HealthPanel"/> (driven by
    /// <see cref="KhrCharacter.GetHealth"/>).
    /// </summary>
    public class HealthController : MonoBehaviour
    {
        public string GlbPath;

        private async void Start()
        {
            bool usingHero = string.IsNullOrEmpty(GlbPath) && CharacterLoader.HeroExists;

            var ui = DemoUiBuilder.Create("Health");
            ui.AddLabel("Capability health for the loaded character (Active / Degraded / Inert).");
            ui.AddLabel(CharacterLoader.DemoCharacterBlurb(usingHero, "SC-Face"));
            var healthText = ui.AddLabel("Loading ...");

            var root = new GameObject("CharacterRoot");
            root.transform.SetParent(transform, false);

            GameObject scene;
            try
            {
                scene = string.IsNullOrEmpty(GlbPath)
                    ? await CharacterLoader.LoadDemoCharacterAsync(root.transform, "SC-Face.glb")
                    : await CharacterLoader.LoadAsync(GlbPath, root.transform);
            }
            catch (System.Exception e) { Debug.LogException(e); healthText.text = "Load failed: " + e.Message; return; }
            if (scene == null) { healthText.text = "Load failed."; return; }

            var hub = scene.GetComponent<KhrCharacter>();
            FrameScene(scene);

            if (hub != null) gameObject.AddComponent<HealthPanel>().Bind(hub, healthText);
            else healthText.text = "No KHR Character data on this asset.";

            var back = gameObject.AddComponent<BackToHubButton>();
            ui.AddButton("Back to Hub", back.GoToHub);
        }

        private void FrameScene(GameObject scene)
        {
            var rig = Object.FindFirstObjectByType<OrbitCameraRig>();
            if (rig == null || scene == null) return;

            if (!SceneBoundsUtil.TryAggregate(scene, out var bounds))
            {
                rig.FrameAndFace(new Bounds(new Vector3(0f, 0.5f, 0f), new Vector3(2f, 2f, 2f)), scene.transform);
                return;
            }
            rig.FrameAndFace(bounds, scene.transform);
        }
    }
}
