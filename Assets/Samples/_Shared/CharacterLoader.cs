using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using GLTF.Schema;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityGLTF;
using UnityGLTF.KhrCharacter;
using UnityGLTF.Loader;
using UnityGLTF.Plugins;

namespace Samples.Shared
{
    /// <summary>
    /// Shared runtime glTF/GLB loader for the samples. It enables the KHR Character import plugin on the project
    /// settings (so the default import context picks it up), runs a <see cref="GLTFSceneImporter"/>, and returns
    /// the loaded scene root. When the asset is a KHR Character, the import plugin attaches the
    /// <see cref="KhrCharacter"/> hub and marks it ready before the load completes, so callers can immediately call
    /// <see cref="KhrCharacter.WhenReady"/>.
    /// </summary>
    public static class CharacterLoader
    {
        /// <summary>Project-relative path (under Assets/) of the committed synthetic sample.</summary>
        public const string DefaultRelativePath = "SampleAssets/Synthetic/SC-Face.glb";

        /// <summary>Absolute, runtime-readable path of the committed synthetic sample.</summary>
        public static string DefaultAbsolutePath => Path.Combine(Application.dataPath, DefaultRelativePath);

        /// <summary>Absolute directory of the committed synthetic samples.</summary>
        public static string SyntheticDirectory => Path.Combine(Application.dataPath, "SampleAssets/Synthetic");

        /// <summary>Absolute path of a committed synthetic sample by file name (e.g. "SC-Body.glb").</summary>
        public static string SyntheticPath(string fileName) => Path.Combine(SyntheticDirectory, fileName);

        /// <summary>Project-relative path of the local "hero" character (git-ignored, bring-your-own VRoid/VRM).</summary>
        public const string HeroRelativePath = "SampleAssets/khr-character-example.glb";

        /// <summary>Absolute, runtime-readable path of the local hero character.</summary>
        public static string HeroAbsolutePath => Path.Combine(Application.dataPath, HeroRelativePath);

        /// <summary>True when the local hero character is present, so demos can default to it.</summary>
        public static bool HeroExists => File.Exists(HeroAbsolutePath);

        /// <summary>
        /// Load the demo's character: the local hero (VRM-origin, consumed via KHR_character) when present, else the
        /// scene-appropriate committed synthetic fallback (e.g. "SC-Face.glb"). Returns the scene root, or null on
        /// failure. Read <see cref="HeroExists"/> to label which one loaded; show a "Loading…" hint while awaiting
        /// (the hero is a ~10 MB GLB).
        /// TODO: if runtime-loading the hero per scene proves slow at validation, switch this to instantiate the
        /// editor-imported prefab in the editor; keep this runtime LoadAsync path for player builds / fresh clones.
        /// </summary>
        public static Task<GameObject> LoadDemoCharacterAsync(Transform parent, string fallbackSyntheticFileName)
        {
            string path = HeroExists ? HeroAbsolutePath : SyntheticPath(fallbackSyntheticFileName);
            return LoadAsync(path, parent);
        }

        /// <summary>A short header noting which demo character loaded (hero vs synthetic fallback) + the VRM caveat.</summary>
        public static string DemoCharacterBlurb(bool usingHero, string fallbackName) => usingHero
            ? "Character: hero khr-character-example (VRM-origin — consumed via KHR_character; VRMC_* ignored)."
            : $"Character: synthetic fallback ({fallbackName}).";

        /// <summary>
        /// Enable the KHR Character import plugin (and set its Rig) on the shared settings. Must run before an
        /// <see cref="ImportOptions"/> is constructed, because the default import context is built from the
        /// settings' enabled plugins at construction time.
        /// </summary>
        public static void EnableImportPlugin(RigImportMode rig = RigImportMode.Humanoid)
        {
            var settings = GLTFSettings.GetOrCreateSettings();
            if (settings == null || settings.ImportPlugins == null) return;
            foreach (var plugin in settings.ImportPlugins)
                if (plugin is KhrCharacterImportPlugin characterImport)
                {
                    characterImport.Enabled = true;
                    characterImport.Rig = rig;
                }
        }

        /// <summary>
        /// Load a GLB/glTF by absolute path, parenting the result under <paramref name="parent"/> (null = scene
        /// root). Returns the scene root GameObject, or null on failure.
        /// </summary>
        public static async Task<GameObject> LoadAsync(string absolutePath, Transform parent)
        {
            EnableImportPlugin();

            string directory = Path.GetDirectoryName(absolutePath);
            string fileName = Path.GetFileName(absolutePath);

            // The async importer needs a MonoBehaviour host for its coroutine helper.
            GameObject tempHost = null;
            GameObject host = parent != null ? parent.gameObject : (tempHost = new GameObject("GltfImportHost"));
            var helper = host.GetComponent<AsyncCoroutineHelper>();
            if (helper == null) helper = host.AddComponent<AsyncCoroutineHelper>();

            // ImportContext is intentionally left at its default (built from GetOrCreateSettings()), which is how an
            // external assembly enables import plugins without the package-internal context constructor.
            var options = new ImportOptions
            {
                AsyncCoroutineHelper = helper,
                DataLoader = new UnityWebRequestLoader(directory),
            };

            var importer = new GLTFSceneImporter(fileName, options);
            importer.SceneParent = parent;

            GameObject scene = null;
            try
            {
                await importer.LoadSceneAsync();
                scene = importer.LastLoadedScene;
            }
            finally
            {
                importer.Dispose();
                if (tempHost != null) Object.Destroy(tempHost);
            }
            return scene;
        }

        /// <summary>
        /// Export a character to an in-memory GLB through the KHR Character export plugin (isolated default settings
        /// with the KHR Character + AnimationPointer export plugins enabled). Also returns the exported
        /// <see cref="GLTFRoot"/> so callers can inspect neutrality (extensionsUsed / extensionsRequired). The
        /// expression animation channels export only under UNITY_EDITOR (the plugin gate), so run this in the editor.
        /// </summary>
        public static byte[] ExportToGlb(GameObject root, out GLTFRoot exportedRoot)
        {
            var settings = GLTFSettings.GetDefaultSettings();
            foreach (var plugin in settings.ExportPlugins)
                if (plugin is KhrCharacterExportPlugin || plugin is AnimationPointerExport)
                    plugin.Enabled = true;

            var exporter = new GLTFSceneExporter(new[] { root.transform }, new ExportContext(settings));
            byte[] glb = exporter.SaveGLBToByteArray("roundtrip");
            exportedRoot = exporter.GetRoot();
            return glb;
        }

        /// <summary>
        /// Re-import a self-contained GLB from an in-memory byte[] (no external data loader needed), parenting the
        /// result under <paramref name="parent"/>. Returns the scene root, or null on failure.
        /// </summary>
        public static async Task<GameObject> LoadFromBytesAsync(byte[] glb, Transform parent)
        {
            EnableImportPlugin();

            GameObject tempHost = null;
            GameObject host = parent != null ? parent.gameObject : (tempHost = new GameObject("GltfImportHost"));
            var helper = host.GetComponent<AsyncCoroutineHelper>();
            if (helper == null) helper = host.AddComponent<AsyncCoroutineHelper>();

            var options = new ImportOptions { AsyncCoroutineHelper = helper };
            var importer = new GLTFSceneImporter(new MemoryStream(glb), options);
            importer.SceneParent = parent;

            GameObject scene = null;
            try
            {
                await importer.LoadSceneAsync();
                scene = importer.LastLoadedScene;
            }
            finally
            {
                importer.Dispose();
                if (tempHost != null) Object.Destroy(tempHost);
            }
            return scene;
        }

        /// <summary>
        /// Read the <c>extensionsUsed</c> array from a source GLB/glTF on disk (e.g. a VRM-origin file that lists
        /// VRMC_* vendor extensions). Used for the "source vs neutral re-export" comparison; it never imports.
        /// </summary>
        public static List<string> ReadSourceExtensionsUsed(string absolutePath)
        {
            var result = new List<string>();
            try
            {
                if (string.IsNullOrEmpty(absolutePath) || !File.Exists(absolutePath)) return result;
                string json = ExtractGltfJson(File.ReadAllBytes(absolutePath));
                if (json == null) return result;
                if (JObject.Parse(json)["extensionsUsed"] is JArray used)
                    foreach (var token in used) result.Add(token.ToString());
            }
            catch (System.Exception e) { Debug.LogException(e); }
            return result;
        }

        // Returns the JSON chunk of a binary GLB, or the whole file when it is already a .gltf JSON document.
        private static string ExtractGltfJson(byte[] bytes)
        {
            if (bytes == null || bytes.Length < 20) return null;
            if (System.BitConverter.ToUInt32(bytes, 0) == 0x46546C67u) // "glTF" -> binary container
            {
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
            return System.Text.Encoding.UTF8.GetString(bytes); // plain .gltf JSON
        }
    }
}
