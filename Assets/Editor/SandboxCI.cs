using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityGLTF;
using UnityGLTF.KhrCharacter;
using UnityGLTF.Plugins;
using UnityGLTF.VisibilityHints;

namespace Samples.Editor
{
    /// <summary>
    /// Headless CI seam invoked via Unity's <c>-executeMethod</c>. It enables the needed plugins IN CODE (never
    /// trusting the committed settings asset), regenerates the SC-*/VH-* fixtures, and writes normalized wire snapshots
    /// for the golden gate. The Tools/ci scripts call these methods.
    /// </summary>
    public static class SandboxCI
    {
        private const string SyntheticDir = "Assets/SampleAssets/Synthetic";

        private static string ProjectRoot => Directory.GetParent(Application.dataPath).FullName;
        private static string GlbStagingDir => Path.Combine(ProjectRoot, "Artifacts", "glb");
        private static string SnapshotDir => Path.Combine(ProjectRoot, "Artifacts", "snapshots");

        // Fixtures the harness exports + snapshots. Add new fixtures here (and they flow into goldens automatically).
        private static readonly (string Name, System.Func<string, string> Generate)[] Fixtures =
        {
            ("SC-Face", SampleCharacterFactory.GenerateSCFace),
            ("SC-FacePlus", SampleCharacterFactory.GenerateSCFacePlus),
            ("SC-Body", SampleCharacterFactory.GenerateSCBody),
            ("SC-LookAt", SampleCharacterFactory.GenerateSCLookAt),
            ("SC-Partial", SampleCharacterFactory.GenerateSCPartial),
            ("SC-PseudoVRM", SampleCharacterFactory.GenerateSCPseudoVRM),
            ("SC-ExprEdge", SampleCharacterFactory.GenerateSCExprEdge),
            ("VH-Node", SampleCharacterFactory.GenerateVHNode),
            ("VH-Primitive", SampleCharacterFactory.GenerateVHPrimitive),
            ("VH-ViewContext", SampleCharacterFactory.GenerateVHViewContext),
        };

        // ── URP nightly cell (Phase 5): create + (de)activate a URP pipeline asset ──────────────────────────────
        // Creation via reflection + activation via the base RenderPipelineAsset type, so this (Built-in) editor
        // assembly needs NO compile-time URP reference. The committed project default stays Built-in (GraphicsSettings
        // null); the nightly URP job calls ActivateUrp before -runTests, and materials follow the active pipeline
        // (Samples.Shared.RenderPipelineUtil). Built-in goldens are unaffected.
        private const string UrpDir = "Assets/Settings/URP";
        private const string UrpAssetPath = UrpDir + "/URP-Nightly.asset";
        private const string UrpRendererPath = UrpDir + "/URP-Nightly-Renderer.asset";
        private const string UrpRuntimeAsm = "Unity.RenderPipelines.Universal.Runtime";

        /// <summary>One-time: create the committed URP pipeline asset + its Universal renderer (idempotent).</summary>
        public static void SetupUrp()
        {
            var rendererType = System.Type.GetType($"UnityEngine.Rendering.Universal.UniversalRendererData, {UrpRuntimeAsm}");
            var assetType = System.Type.GetType($"UnityEngine.Rendering.Universal.UniversalRenderPipelineAsset, {UrpRuntimeAsm}");
            if (rendererType == null || assetType == null)
            {
                Debug.LogError("[SandboxCI] SetupUrp: URP types not found - is com.unity.render-pipelines.universal installed?");
                EditorApplication.Exit(2);
                return;
            }
            Directory.CreateDirectory(UrpDir);
            if (AssetDatabase.LoadAssetAtPath<RenderPipelineAsset>(UrpAssetPath) != null)
            {
                Debug.Log("[SandboxCI] SetupUrp: URP asset already exists; nothing to do.");
                return;
            }

            var renderer = ScriptableObject.CreateInstance(rendererType);
            AssetDatabase.CreateAsset(renderer, UrpRendererPath);

            System.Reflection.MethodInfo create = null;
            foreach (var m in assetType.GetMethods(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static))
                if (m.Name == "Create" && m.GetParameters().Length == 1) { create = m; break; }
            if (create == null)
            {
                Debug.LogError("[SandboxCI] SetupUrp: UniversalRenderPipelineAsset.Create(rendererData) not found.");
                EditorApplication.Exit(2);
                return;
            }
            var asset = (UnityEngine.Object)create.Invoke(null, new object[] { renderer });
            AssetDatabase.CreateAsset(asset, UrpAssetPath);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log($"[SandboxCI] SetupUrp: created {UrpAssetPath} (+ renderer).");
        }

        /// <summary>Activate URP as the project render pipeline (nightly URP cell, before -runTests).</summary>
        public static void ActivateUrp() => SetActivePipeline(UrpAssetPath);

        /// <summary>Restore the Built-in pipeline (the committed default).</summary>
        public static void DeactivateUrp() => SetActivePipeline(null);

        private static void SetActivePipeline(string assetPath)
        {
            RenderPipelineAsset rp = null;
            if (assetPath != null)
            {
                rp = AssetDatabase.LoadAssetAtPath<RenderPipelineAsset>(assetPath);
                if (rp == null)
                {
                    Debug.LogError($"[SandboxCI] render pipeline asset not found at {assetPath}; run SetupUrp first.");
                    EditorApplication.Exit(2);
                    return;
                }
            }
            GraphicsSettings.defaultRenderPipeline = rp;   // in-memory for the current editor session
            QualitySettings.renderPipeline = rp;
            // Persist to ProjectSettings/GraphicsSettings.asset: AssetDatabase.SaveAssets alone does NOT flush the
            // GraphicsSettings singleton in batchmode, so write m_CustomRenderPipeline via SerializedObject + SetDirty.
            var gs = GraphicsSettings.GetGraphicsSettings();
            var so = new SerializedObject(gs);
            var prop = so.FindProperty("m_CustomRenderPipeline");
            if (prop != null) { prop.objectReferenceValue = rp; so.ApplyModifiedProperties(); }
            EditorUtility.SetDirty(gs);
            AssetDatabase.SaveAssets();
            Debug.Log($"[SandboxCI] active render pipeline = {(rp != null ? rp.name : "Built-in")}.");
        }

        /// <summary>Enable the KHR Character and Visibility Hint import + export plugins (and AnimationPointer) on the shared settings.</summary>
        public static void EnablePlugins()
        {
            var settings = GLTFSettings.GetOrCreateSettings();
            if (settings == null) { Debug.LogError("[SandboxCI] Could not load GLTFSettings."); return; }

            int updated = 0;
            if (settings.ImportPlugins != null)
                foreach (var plugin in settings.ImportPlugins)
                    if (plugin is KhrCharacterImportPlugin || plugin is VisibilityHintImportPlugin) updated += SetEnabled(plugin);
            if (settings.ExportPlugins != null)
                foreach (var plugin in settings.ExportPlugins)
                    if (plugin is KhrCharacterExportPlugin || plugin is AnimationPointerExport || plugin is VisibilityHintExportPlugin) updated += SetEnabled(plugin);

            EditorUtility.SetDirty(settings);
            AssetDatabase.SaveAssets();
            Debug.Log($"[SandboxCI] EnablePlugins: {updated} plugin(s) enabled.");
        }

        /// <summary>Regenerate the committed SC-*/VH-* GLB fixtures under Assets/SampleAssets/Synthetic.</summary>
        public static void ExportAllFixtures()
        {
            EnablePlugins();
            var written = new List<string>();
            foreach (var fixture in Fixtures)
                written.Add(fixture.Generate(SyntheticDir));
            AssetDatabase.Refresh();
            Debug.Log("[SandboxCI] ExportAllFixtures wrote:\n  " + string.Join("\n  ", written));
        }

        /// <summary>
        /// Export each fixture to a staging dir and write a normalized wire snapshot to Artifacts/snapshots/. The
        /// Export-Goldens script then diffs (or updates) Tests/Golden against these.
        /// </summary>
        public static void ExportGoldens()
        {
            EnablePlugins();
            Directory.CreateDirectory(GlbStagingDir);
            Directory.CreateDirectory(SnapshotDir);

            foreach (var fixture in Fixtures)
            {
                string glbPath = fixture.Generate(GlbStagingDir);
                if (!ReadGlbChunks(glbPath, out string json, out byte[] bin) || json == null)
                {
                    Debug.LogError($"[SandboxCI] Could not read GLB JSON chunk for {fixture.Name} at {glbPath}");
                    continue;
                }
                string snapshot = NormalizeGltfJson(json, bin);
                string outPath = Path.Combine(SnapshotDir, fixture.Name + ".json");
                File.WriteAllText(outPath, snapshot);
                Debug.Log($"[SandboxCI] snapshot -> {outPath}");
            }
        }

        private static int SetEnabled(GLTFPlugin plugin)
        {
            if (plugin == null || plugin.Enabled) return 0;
            plugin.Enabled = true;
            EditorUtility.SetDirty(plugin);
            return 1;
        }

        // Reads the JSON (and BIN, if present) chunks out of a binary GLB container (12-byte header + length/
        // type-prefixed chunks). Returns true when a JSON chunk was found.
        private static bool ReadGlbChunks(string glbPath, out string json, out byte[] bin)
        {
            json = null; bin = null;
            if (!File.Exists(glbPath)) return false;
            var bytes = File.ReadAllBytes(glbPath);
            if (bytes.Length < 20) return false;
            if (System.BitConverter.ToUInt32(bytes, 0) != 0x46546C67u) return false; // "glTF"

            int offset = 12;
            while (offset + 8 <= bytes.Length)
            {
                uint chunkLength = System.BitConverter.ToUInt32(bytes, offset);
                uint chunkType = System.BitConverter.ToUInt32(bytes, offset + 4);
                int dataStart = offset + 8;
                if (dataStart + (long)chunkLength > bytes.Length) break;
                if (chunkType == 0x4E4F534Au) // "JSON"
                    json = System.Text.Encoding.UTF8.GetString(bytes, dataStart, (int)chunkLength);
                else if (chunkType == 0x004E4942u) // "BIN\0"
                {
                    bin = new byte[chunkLength];
                    System.Array.Copy(bytes, dataStart, bin, 0, (int)chunkLength);
                }
                offset = dataStart + (int)chunkLength;
            }
            return json != null;
        }

        // Deterministic snapshot: decode every FLOAT accessor to its actual values (rounded to 1e-5), drop the
        // byte-packing fields (accessor/bufferView byteOffset, bufferView/buffer byteLength) and volatile asset
        // fields, sort every object's keys, pretty-print. Snapshotting DECODED values (not raw byteOffset/min/max)
        // means an interior value change can't hide behind unchanged extremes, and packing-order jitter can't
        // false-diff the golden. NOTE: this is a golden FORMAT change - regenerate once via
        // Export-Goldens -Update and commit the diff.
        private static string NormalizeGltfJson(string json, byte[] bin)
        {
            var root = JToken.Parse(json);
            if (root is JObject obj)
            {
                if (obj["asset"] is JObject asset)
                {
                    asset.Remove("generator");
                    asset.Remove("copyright");
                }
                DecodeFloatAccessors(obj, bin);
                StripPackingFields(obj);
            }
            return SortToken(root).ToString(Newtonsoft.Json.Formatting.Indented);
        }

        // Decode each FLOAT (componentType 5126) accessor's values from the GLB BIN chunk, round to 1e-5, and store
        // them on the accessor as "values" (replacing the raw min/max + byteOffset). Non-float accessors (indices,
        // joints) keep their deterministic min/max; only their byteOffset (packing) is dropped by StripPackingFields.
        private static void DecodeFloatAccessors(JObject root, byte[] bin)
        {
            if (bin == null) return;
            if (!(root["accessors"] is JArray accessors)) return;
            var bufferViews = root["bufferViews"] as JArray;

            foreach (var token in accessors)
            {
                if (!(token is JObject acc)) continue;
                if (ReadInt(acc["componentType"], 0) != 5126) continue;   // FLOAT only
                if (bufferViews == null) continue;

                int bvIndex = ReadInt(acc["bufferView"], -1);
                if (bvIndex < 0 || bvIndex >= bufferViews.Count || !(bufferViews[bvIndex] is JObject bv)) continue;

                int count = ReadInt(acc["count"], 0);
                int numComp = ComponentCount((string)acc["type"]);
                if (count <= 0 || numComp <= 0) continue;

                int elementSize = numComp * 4;   // sizeof(float)
                int byteStride = ReadInt(bv["byteStride"], 0);
                int stride = byteStride > 0 ? byteStride : elementSize;
                int start = ReadInt(bv["byteOffset"], 0) + ReadInt(acc["byteOffset"], 0);

                var values = new JArray();
                bool ok = true;
                for (int i = 0; i < count && ok; i++)
                {
                    int elementStart = start + i * stride;
                    for (int c = 0; c < numComp; c++)
                    {
                        int byteOffset = elementStart + c * 4;
                        if (byteOffset < 0 || byteOffset + 4 > bin.Length) { ok = false; break; }
                        double rounded = System.Math.Round((double)System.BitConverter.ToSingle(bin, byteOffset), 5);
                        if (rounded == 0d) rounded = 0d;   // collapse -0 to +0 for a stable snapshot
                        values.Add(rounded);
                    }
                }
                if (!ok) continue;   // leave the accessor untouched if the slice is out of range (safety)

                acc.Remove("min");
                acc.Remove("max");
                acc.Remove("byteOffset");
                acc["values"] = values;
            }
        }

        // Drop byte-packing fields so packing order / jitter can't false-diff the golden (the decoded accessor
        // values carry the actual data). Structural fields (buffer index, target, byteStride, names) are kept.
        private static void StripPackingFields(JObject root)
        {
            if (root["bufferViews"] is JArray bufferViews)
                foreach (var bv in bufferViews)
                    if (bv is JObject o) { o.Remove("byteOffset"); o.Remove("byteLength"); }
            if (root["buffers"] is JArray buffers)
                foreach (var b in buffers)
                    if (b is JObject o) o.Remove("byteLength");
        }

        private static int ReadInt(JToken token, int fallback)
            => token != null && (token.Type == JTokenType.Integer || token.Type == JTokenType.Float)
                ? (int)token
                : fallback;

        // glTF accessor element component counts.
        private static int ComponentCount(string type)
        {
            switch (type)
            {
                case "SCALAR": return 1;
                case "VEC2": return 2;
                case "VEC3": return 3;
                case "VEC4": return 4;
                case "MAT2": return 4;
                case "MAT3": return 9;
                case "MAT4": return 16;
                default: return 0;
            }
        }

        private static JToken SortToken(JToken token)
        {
            if (token.Type == JTokenType.Object)
            {
                var source = (JObject)token;
                var names = new List<string>();
                foreach (var property in source.Properties()) names.Add(property.Name);
                names.Sort(System.StringComparer.Ordinal);

                var sorted = new JObject();
                foreach (var name in names) sorted.Add(name, SortToken(source[name]));
                return sorted;
            }
            if (token.Type == JTokenType.Array)
            {
                var sorted = new JArray();
                foreach (var item in (JArray)token) sorted.Add(SortToken(item));
                return sorted;
            }
            return token.DeepClone();
        }
    }
}
