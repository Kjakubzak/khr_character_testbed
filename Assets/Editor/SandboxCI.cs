using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;
using UnityGLTF;
using UnityGLTF.KhrCharacter;
using UnityGLTF.Plugins;

namespace Samples.Editor
{
    /// <summary>
    /// Headless CI seam invoked via Unity's <c>-executeMethod</c>. It enables the needed plugins IN CODE (never
    /// trusting the committed settings asset), regenerates the SC-* fixtures, and writes normalized wire snapshots
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
        };

        /// <summary>Enable the KHR Character import + export plugins (and AnimationPointer) on the shared settings.</summary>
        public static void EnablePlugins()
        {
            var settings = GLTFSettings.GetOrCreateSettings();
            if (settings == null) { Debug.LogError("[SandboxCI] Could not load GLTFSettings."); return; }

            int updated = 0;
            if (settings.ImportPlugins != null)
                foreach (var plugin in settings.ImportPlugins)
                    if (plugin is KhrCharacterImportPlugin) updated += SetEnabled(plugin);
            if (settings.ExportPlugins != null)
                foreach (var plugin in settings.ExportPlugins)
                    if (plugin is KhrCharacterExportPlugin || plugin is AnimationPointerExport) updated += SetEnabled(plugin);

            EditorUtility.SetDirty(settings);
            AssetDatabase.SaveAssets();
            Debug.Log($"[SandboxCI] EnablePlugins: {updated} plugin(s) enabled.");
        }

        /// <summary>Regenerate the committed SC-* GLB fixtures under Assets/SampleAssets/Synthetic.</summary>
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
                string json = ExtractGlbJson(glbPath);
                if (json == null)
                {
                    Debug.LogError($"[SandboxCI] Could not read GLB JSON chunk for {fixture.Name} at {glbPath}");
                    continue;
                }
                string snapshot = NormalizeGltfJson(json);
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

        // Reads the JSON chunk out of a binary GLB container (12-byte header + length/type-prefixed chunks).
        private static string ExtractGlbJson(string glbPath)
        {
            if (!File.Exists(glbPath)) return null;
            var bytes = File.ReadAllBytes(glbPath);
            if (bytes.Length < 20) return null;
            if (System.BitConverter.ToUInt32(bytes, 0) != 0x46546C67u) return null; // "glTF"

            int offset = 12;
            while (offset + 8 <= bytes.Length)
            {
                uint chunkLength = System.BitConverter.ToUInt32(bytes, offset);
                uint chunkType = System.BitConverter.ToUInt32(bytes, offset + 4);
                int dataStart = offset + 8;
                if (dataStart + (long)chunkLength > bytes.Length) break;
                if (chunkType == 0x4E4F534Au) // "JSON"
                    return System.Text.Encoding.UTF8.GetString(bytes, dataStart, (int)chunkLength);
                offset = dataStart + (int)chunkLength;
            }
            return null;
        }

        // Deterministic snapshot: drop volatile asset fields, sort every object's keys, pretty-print. This makes a
        // committed golden stable run-to-run while still capturing real wire changes (new/renamed keys, changed
        // min/max, reordered arrays). (Accessor-value decoding is a future enhancement if byte packing ever jitters.)
        private static string NormalizeGltfJson(string json)
        {
            var root = JToken.Parse(json);
            if (root is JObject obj && obj["asset"] is JObject asset)
            {
                asset.Remove("generator");
                asset.Remove("copyright");
            }
            return SortToken(root).ToString(Newtonsoft.Json.Formatting.Indented);
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
