using UnityEngine;
using UnityGLTF.VisibilityHints;

namespace Samples.VisibilityHints
{
    /// <summary>
    /// Supplies the optional material-based visibility adapter with this testbed's pipeline-compatible no-draw
    /// material. Materials are one host integration route, not extension semantics; the passive evaluator remains
    /// independent of this provider.
    /// </summary>
    public static class InvisibleMaterialInstaller
    {
        private const string ShaderResourceName = "VisibilityHintsInvisible";
        private static Material _material;

        public static Material GetNoDrawMaterial()
        {
            if (_material != null) return _material;

            var shader = Resources.Load<Shader>(ShaderResourceName);
            if (shader == null)
            {
                Debug.LogWarning(
                    $"[VisibilityHints] No-draw shader '{ShaderResourceName}' was not found in Resources.");
                return null;
            }

            _material = new Material(shader)
            {
                name = "VisibilityHints_Invisible (testbed)",
                hideFlags = HideFlags.HideAndDontSave,
            };
            return _material;
        }
    }
}
