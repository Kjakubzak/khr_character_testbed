using UnityEditor;
using UnityEngine;
using UnityGLTF.KhrCharacter;

namespace Samples.Editor
{
    /// <summary>
    /// N9 — "Extract Expression Set" editor tool. Takes the selected character's baked
    /// <see cref="CharacterExpressionSet"/> (from its <see cref="ExpressionController"/>), converts it to a
    /// scene-independent <see cref="CharacterExpressionSetAsset"/> via <see cref="CharacterExpressionSetAsset.Extract"/>,
    /// saves it as a ScriptableObject, and proves the round-trip by <see cref="CharacterExpressionSetAsset.Resolve"/>-ing
    /// it back and logging the expression counts.
    /// </summary>
    public static class ExtractExpressionSetTool
    {
        [MenuItem("Assets/UnityGLTF/KHR Character/Extract Expression Set")]
        public static void Extract()
        {
            var go = Selection.activeGameObject;
            if (go == null) { Notify("Select a character GameObject (with an ExpressionController) in the scene first."); return; }

            var controller = go.GetComponentInChildren<ExpressionController>();
            if (controller == null) { Notify($"No ExpressionController found under '{go.name}'."); return; }

            // Set is the live runtime copy (play mode); BakedSet is the persisted copy (edit-mode prefab).
            var baked = controller.Set ?? controller.BakedSet;
            if (baked?.Expressions == null || baked.Expressions.Length == 0)
            {
                Notify($"'{go.name}' has no baked expression set to extract.");
                return;
            }

            var root = controller.transform;
            var bindings = CharacterExpressionSetAsset.Extract(baked, root);

            string path = EditorUtility.SaveFilePanelInProject(
                "Save Expression Set", go.name + "-ExpressionSet", "asset",
                "Save the extracted, scene-independent expression set as a ScriptableObject.");
            if (string.IsNullOrEmpty(path)) return;

            var asset = ScriptableObject.CreateInstance<CharacterExpressionSetAsset>();
            asset.Bindings = bindings;
            AssetDatabase.CreateAsset(asset, path);
            AssetDatabase.SaveAssets();

            // Round-trip proof: resolve the bindings back onto the same character.
            var resolved = CharacterExpressionSetAsset.Resolve(asset.Bindings, root);
            int extracted = bindings.Expressions?.Length ?? 0;
            int restored = resolved.Expressions?.Length ?? 0;

            Debug.Log($"[Samples] Extract Expression Set: {extracted} expression(s) -> '{path}' -> Resolve restored {restored}.");
            Notify($"Saved '{path}'.\nExtracted {extracted} expression(s); round-trip Resolve restored {restored}.");
        }

        private static void Notify(string message)
        {
            Debug.Log("[Samples] " + message);
            if (!Application.isBatchMode) EditorUtility.DisplayDialog("Extract Expression Set", message, "OK");
        }
    }
}
