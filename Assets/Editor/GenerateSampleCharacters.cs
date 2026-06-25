using UnityEditor;
using UnityEngine;

namespace Samples.Editor
{
    /// <summary>
    /// Tool B — generates the synthetic sample characters (SC-Face, SC-FacePlus, SC-Body) by delegating to
    /// <see cref="SampleCharacterFactory"/> and writing them under <c>Assets/SampleAssets/Synthetic/</c>. Each
    /// factory method is also callable directly from CI/tests. Batchmode-safe (no blocking dialog when headless).
    /// </summary>
    public static class GenerateSampleCharacters
    {
        private const string MenuPath = "Assets/UnityGLTF/KHR Character/Generate Sample Characters";
        private const string OutputDirectory = "Assets/SampleAssets/Synthetic";

        [MenuItem(MenuPath)]
        public static void Generate()
        {
            try
            {
                string face = SampleCharacterFactory.GenerateSCFace(OutputDirectory);
                string facePlus = SampleCharacterFactory.GenerateSCFacePlus(OutputDirectory);
                string body = SampleCharacterFactory.GenerateSCBody(OutputDirectory);
                string lookAt = SampleCharacterFactory.GenerateSCLookAt(OutputDirectory);
                string partial = SampleCharacterFactory.GenerateSCPartial(OutputDirectory);
                string pseudoVrm = SampleCharacterFactory.GenerateSCPseudoVRM(OutputDirectory);
                string exprEdge = SampleCharacterFactory.GenerateSCExprEdge(OutputDirectory);

                Debug.Log($"[Samples] Generated sample characters:\n  {face}\n  {facePlus}\n  {body}\n  {lookAt}\n  {partial}\n  {pseudoVrm}\n  {exprEdge}");
                if (!Application.isBatchMode)
                    EditorUtility.DisplayDialog("Generate Sample Characters",
                        $"Wrote:\n{face}\n{facePlus}\n{body}\n{lookAt}\n{partial}\n{pseudoVrm}\n{exprEdge}", "OK");
            }
            catch (System.Exception ex)
            {
                Debug.LogException(ex);
                if (!Application.isBatchMode)
                    EditorUtility.DisplayDialog("Generate Sample Characters",
                        "Generation failed; see the Console for details.", "OK");
            }
        }
    }
}
