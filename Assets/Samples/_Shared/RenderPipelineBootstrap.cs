using UnityEngine;
using UnityEngine.Rendering;

namespace Samples.Shared
{
    /// <summary>
    /// Gives a sample scene basic directional lighting and ambient so it renders the same whether the project
    /// uses the Built-in render pipeline or a Scriptable Render Pipeline (e.g. URP). Render-pipeline-agnostic and
    /// idempotent: it only adds a light if the scene has none.
    /// </summary>
    [DisallowMultipleComponent]
    public class RenderPipelineBootstrap : MonoBehaviour
    {
        [Tooltip("Euler rotation of the directional light created when the scene has none.")]
        public Vector3 LightEuler = new Vector3(50f, -30f, 0f);

        [Tooltip("Flat ambient color applied to the scene.")]
        public Color AmbientColor = new Color(0.42f, 0.44f, 0.5f, 1f);

        [Tooltip("Intensity of the directional light created when the scene has none.")]
        public float LightIntensity = 1.1f;

        /// <summary>True when a Scriptable Render Pipeline asset is active (URP/HDRP), false for Built-in.</summary>
        public static bool IsScriptableRenderPipelineActive =>
            GraphicsSettings.defaultRenderPipeline != null || GraphicsSettings.currentRenderPipeline != null;

        private void Awake() => Configure();

        /// <summary>Ensures a directional light exists and sets a neutral flat ambient. Safe to call repeatedly.</summary>
        public void Configure()
        {
            EnsureDirectionalLight();
            RenderSettings.ambientMode = AmbientMode.Flat;
            RenderSettings.ambientLight = AmbientColor;
        }

        private void EnsureDirectionalLight()
        {
            foreach (var existing in Object.FindObjectsByType<Light>(FindObjectsSortMode.None))
                if (existing != null && existing.type == LightType.Directional)
                    return;

            var go = new GameObject("Directional Light (Bootstrap)");
            go.transform.SetParent(transform, false);
            go.transform.rotation = Quaternion.Euler(LightEuler);

            var light = go.AddComponent<Light>();
            light.type = LightType.Directional;
            light.color = Color.white;
            light.intensity = LightIntensity;
        }
    }
}
