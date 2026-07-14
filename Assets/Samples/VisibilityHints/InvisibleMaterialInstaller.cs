using UnityEngine;
using UnityGLTF.VisibilityHints;

namespace KhrCharacterTestbed
{
    /// <summary>
    /// Supplies this project's invisible material to UnityGLTF's VisibilityHints system for hiding mesh
    /// primitives flagged by <c>KHR_mesh_primitive_visibility_hint</c>.
    ///
    /// <para>UnityGLTF's <see cref="InvisibleMaterialCache"/> builds a best-effort transparent material at
    /// runtime via <see cref="Shader.Find"/>, which is fragile in player builds (a miss falls back to the
    /// magenta error shader) and isn't a guaranteed no-op. Here we override it with a material backed by the
    /// dedicated ColorMask-0 shader shipped in this project's <c>Resources</c> — build-safe (Resources assets
    /// are always included) and guaranteed to render nothing.</para>
    ///
    /// <para>Runs BeforeSceneLoad so the override is in place before the first hidden primitive is resolved
    /// during import.</para>
    /// </summary>
    public static class InvisibleMaterialInstaller
    {
        // Filename (no extension) of the shader under Assets/Samples/VisibilityHints/Resources/.
        private const string ShaderResourceName = "VisibilityHintsInvisible";

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Install()
        {
            // Respect an override another installer may have set first; only fill it in when empty.
            if (InvisibleMaterialCache.Override != null) return;

            var shader = Resources.Load<Shader>(ShaderResourceName);
            if (shader == null)
            {
                Debug.LogWarning(
                    $"[VisibilityHints] Invisible shader '{ShaderResourceName}' not found in Resources; " +
                    "falling back to UnityGLTF's runtime material.");
                return;
            }

            InvisibleMaterialCache.Override = new Material(shader)
            {
                name = "VisibilityHints_Invisible (testbed)",
                hideFlags = HideFlags.HideAndDontSave,
            };
        }
    }
}
