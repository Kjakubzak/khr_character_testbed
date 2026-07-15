using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using GLTF.Schema;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityGLTF;
using UnityGLTF.KhrCharacter;
using UnityGLTF.VisibilityHints;
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
        /// <summary>
        /// Absolute path of the most recent disk load via <see cref="LoadAsync"/> (null after an in-memory load),
        /// so inspection UI can re-read the source <c>extensionsUsed</c> without knowing the per-scene controller.
        /// </summary>
        public static string LastLoadedSourcePath { get; private set; }

        /// <summary>Project-relative path (under Assets/) of the committed synthetic sample.</summary>
        public const string DefaultRelativePath = "SampleAssets/Synthetic/SC-Face.glb";

        /// <summary>Absolute, runtime-readable path of the committed synthetic sample.</summary>
        public static string DefaultAbsolutePath => Path.Combine(Application.dataPath, DefaultRelativePath);

        // Default absolute directories (fallbacks used when the catalog has no matching source
        // registered — first-touch, or the user removed the auto-detected default).
        private static string DefaultSyntheticDirectory =>
            Path.Combine(Application.dataPath, "SampleAssets/Synthetic");
        private static string DefaultFromBlenderDirectory =>
            Path.Combine(Application.dataPath, "SampleAssets/FromBlender");

        /// <summary>Absolute directory of the committed synthetic samples. Consults the catalog's
        /// "Synthetic" source if registered; falls back to the hardcoded project path otherwise —
        /// so a user who renames the source via the runtime UI has this property track the rename.</summary>
        public static string SyntheticDirectory =>
            AssetSourceCatalog.ResolveDirectory("Synthetic", DefaultSyntheticDirectory);
        /// <summary>Absolute path of a committed synthetic sample by file name (e.g. "SC-Body.glb").
        /// Catalog-backed (see <see cref="SyntheticDirectory"/>).</summary>
        public static string SyntheticPath(string fileName) =>
            AssetSourceCatalog.Resolve("Synthetic", fileName, DefaultSyntheticDirectory);
        /// <summary>Absolute directory of the FromBlender fixture matrix (exported by the sibling
        /// <c>khr_character_blender</c> addon's <c>tests/fixtures/regenerate.py</c>). Ten canonical
        /// KHR-Character `.glb` variations; see <c>FromBlender/README.md</c>. Catalog-backed like
        /// <see cref="SyntheticDirectory"/>.</summary>
        public static string FromBlenderDirectory =>
            AssetSourceCatalog.ResolveDirectory("FromBlender", DefaultFromBlenderDirectory);
        /// <summary>Absolute path of a FromBlender fixture by file name (e.g. "full.glb").
        /// Catalog-backed (see <see cref="FromBlenderDirectory"/>).</summary>
        public static string FromBlenderPath(string fileName) =>
            AssetSourceCatalog.Resolve("FromBlender", fileName, DefaultFromBlenderDirectory);

        // ── Asset source catalog ──────────────────────────────────────────────────────
        //
        // Rather than hardcoding "Synthetic" and "FromBlender" as first-class named paths that every
        // consumer knows about, the catalog is a registry of (label, directory) pairs — one entry per
        // "place we look for .glb / .gltf". Consumers enumerate the catalog to build UI (GlbViewer's
        // preset dropdown) or drive tests (SandboxFromBlenderTests fixture matrix). New sources are
        // added via the runtime UI (which persists to PlayerPrefs so an added folder survives session
        // restart) or programmatically via ``AssetSourceCatalog.TryRegister``.
        //
        // Two default sources auto-register on first access if their directories exist:
        //   * ``Synthetic``   — SampleCharacterFactory output (SC-*, VH-*). Registers whether or not
        //                       the SC-* / VH-* fixtures have been generated yet; if the directory
        //                       is empty, its enumeration is empty and no dropdown entries appear.
        //                       Once you run "Generate Sample Characters" the entries populate.
        //   * ``FromBlender`` — the khr_character_blender addon's fixture matrix.
        //
        // The hero character is a single FILE, not a directory, so it stays out of the catalog and
        // remains a first-class named path via ``HeroAbsolutePath``. Everything else that's directory-
        // shaped goes through the catalog.

        /// <summary>One entry in the <see cref="AssetSourceCatalog"/> — a labeled directory that
        /// may hold ``.glb`` / ``.gltf`` assets.</summary>
        public sealed class AssetSource
        {
            /// <summary>Display label (e.g. "Synthetic", "FromBlender", "My local characters").</summary>
            public string Label;
            /// <summary>Absolute directory path.</summary>
            public string Directory;
            /// <summary>True when the entry was created by the built-in default registration (Synthetic,
            /// FromBlender). Auto-detected entries cannot be removed via the runtime UI — they'll just
            /// re-register on next session — but they can be hidden by clearing PlayerPrefs.</summary>
            public bool AutoDetected;

            /// <summary>Enumerate every ``.glb`` / ``.gltf`` file directly under this source
            /// (non-recursive). Returns empty if the directory is missing.</summary>
            public IEnumerable<string> EnumerateAssets()
            {
                if (string.IsNullOrEmpty(Directory) || !System.IO.Directory.Exists(Directory))
                    yield break;
                foreach (var f in System.IO.Directory.EnumerateFiles(Directory, "*.glb"))
                    yield return f;
                foreach (var f in System.IO.Directory.EnumerateFiles(Directory, "*.gltf"))
                    yield return f;
            }
        }

        /// <summary>Registry of asset-source directories consumers (GlbViewer, tests) enumerate to find
        /// loadable assets. Thread-safe lazy init on first access.</summary>
        public static class AssetSourceCatalog
        {
            private const string PrefsKey = "KhrCharacterTestbed.UserAssetSources.v1";
            private static readonly object _sync = new object();
            private static List<AssetSource> _sources;

            /// <summary>All registered sources (auto-detected + user-added). Lazily initialized on
            /// first access.</summary>
            public static IReadOnlyList<AssetSource> Sources
            {
                get { EnsureInitialized(); return _sources; }
            }

            /// <summary>Enumerate every asset across every registered source as
            /// ``(source, absolutePath)`` pairs. Missing directories contribute nothing (silent).</summary>
            public static IEnumerable<(AssetSource Source, string Path)> EnumerateAll()
            {
                EnsureInitialized();
                foreach (var src in _sources)
                    foreach (var p in src.EnumerateAssets())
                        yield return (src, p);
            }

            /// <summary>Resolve a fixture path from a labeled source in the catalog. Returns the
            /// catalog-driven absolute path when a source with <paramref name="sourceLabel"/> is
            /// registered; otherwise falls back to <paramref name="fallbackDirectory"/>. Tests and
            /// legacy callers use this to stay UI-configurable — if the user renames a source via
            /// the runtime UI the resolved path tracks the rename, but the hardcoded fallback
            /// keeps determinism when the catalog isn't yet initialized.</summary>
            public static string Resolve(string sourceLabel, string fileName, string fallbackDirectory)
            {
                if (string.IsNullOrEmpty(fileName)) return string.Empty;
                return System.IO.Path.Combine(ResolveDirectory(sourceLabel, fallbackDirectory), fileName);
            }

            /// <summary>Resolve the DIRECTORY for a labeled source. Returns the catalog-driven
            /// absolute path when registered, otherwise <paramref name="fallbackDirectory"/>.</summary>
            public static string ResolveDirectory(string sourceLabel, string fallbackDirectory)
            {
                EnsureInitialized();
                lock (_sync)
                {
                    foreach (var s in _sources)
                        if (string.Equals(s.Label, sourceLabel, System.StringComparison.OrdinalIgnoreCase))
                            return s.Directory;
                }
                return fallbackDirectory;
            }

            /// <summary>Register a source directory. Returns false when the directory is already
            /// registered (deduped case-insensitively) or the label/path is empty. Persists user-added
            /// (non-auto-detected) entries to PlayerPrefs so they survive session restart.</summary>
            public static bool TryRegister(string label, string absoluteDirectory, bool autoDetected = false)
            {
                EnsureInitialized();
                if (string.IsNullOrWhiteSpace(label) || string.IsNullOrWhiteSpace(absoluteDirectory))
                    return false;
                lock (_sync)
                {
                    foreach (var existing in _sources)
                        if (string.Equals(existing.Directory, absoluteDirectory,
                                System.StringComparison.OrdinalIgnoreCase))
                            return false;
                    _sources.Add(new AssetSource
                    {
                        Label = label.Trim(),
                        Directory = absoluteDirectory.Trim(),
                        AutoDetected = autoDetected,
                    });
                    if (!autoDetected) SaveUserSources();
                }
                return true;
            }

            /// <summary>Remove a user-added source. Auto-detected defaults cannot be removed
            /// (they'd re-register on next session anyway).</summary>
            public static bool Remove(AssetSource source)
            {
                if (source == null || source.AutoDetected) return false;
                EnsureInitialized();
                lock (_sync)
                {
                    bool removed = _sources.Remove(source);
                    if (removed) SaveUserSources();
                    return removed;
                }
            }

            /// <summary>Wipe all user-added sources (auto-detected defaults survive on next re-init).</summary>
            public static void ClearUserSources()
            {
                EnsureInitialized();
                lock (_sync)
                {
                    _sources.RemoveAll(s => !s.AutoDetected);
                    SaveUserSources();
                }
            }

            /// <summary>Force a re-initialization on next access — useful in tests or when
            /// generation just landed new files and we want the catalog to re-check.</summary>
            public static void Invalidate()
            {
                lock (_sync) _sources = null;
            }

            private static void EnsureInitialized()
            {
                if (_sources != null) return;
                lock (_sync)
                {
                    if (_sources != null) return;
                    _sources = new List<AssetSource>();
                    RegisterDefaults();
                    LoadUserSources();
                }
            }

            private static void RegisterDefaults()
            {
                // Use the raw Default* directories (not the catalog-consulting SyntheticDirectory /
                // FromBlenderDirectory properties) to avoid a circular init: those properties call
                // back into ResolveDirectory, which calls EnsureInitialized, which we're inside.
                RegisterDefaultInternal("Synthetic", DefaultSyntheticDirectory);
                RegisterDefaultInternal("FromBlender", DefaultFromBlenderDirectory);
            }

            private static void RegisterDefaultInternal(string label, string dir)
            {
                foreach (var s in _sources)
                    if (string.Equals(s.Directory, dir, System.StringComparison.OrdinalIgnoreCase))
                        return;
                _sources.Add(new AssetSource
                {
                    Label = label, Directory = dir, AutoDetected = true,
                });
            }

            // ── PlayerPrefs persistence ─────────────────────────────────────
            //
            // Format: newline-separated "label|absolutePath". PlayerPrefs is fine for per-user local
            // additions; if we ever want committed shared sources, ship a JSON asset that
            // RegisterDefaults reads on top of the two built-ins.

            private static void LoadUserSources()
            {
                string raw = PlayerPrefs.GetString(PrefsKey, string.Empty);
                if (string.IsNullOrEmpty(raw)) return;
                foreach (var line in raw.Split('\n'))
                {
                    if (string.IsNullOrWhiteSpace(line)) continue;
                    int pipe = line.IndexOf('|');
                    if (pipe <= 0) continue;
                    string label = line.Substring(0, pipe).Trim();
                    string path = line.Substring(pipe + 1).Trim();
                    if (string.IsNullOrEmpty(label) || string.IsNullOrEmpty(path)) continue;
                    // Dedupe against the (potentially-just-registered) defaults.
                    bool dup = false;
                    foreach (var existing in _sources)
                        if (string.Equals(existing.Directory, path,
                                System.StringComparison.OrdinalIgnoreCase))
                        { dup = true; break; }
                    if (dup) continue;
                    _sources.Add(new AssetSource
                    {
                        Label = label, Directory = path, AutoDetected = false,
                    });
                }
            }

            private static void SaveUserSources()
            {
                var sb = new System.Text.StringBuilder();
                foreach (var s in _sources)
                {
                    if (s.AutoDetected) continue;
                    sb.Append(s.Label).Append('|').Append(s.Directory).Append('\n');
                }
                PlayerPrefs.SetString(PrefsKey, sb.ToString());
                PlayerPrefs.Save();
            }
        }

        /// <summary>Project-relative path of the "hero" character (VRM-origin, committed via Git LFS).</summary>
        public const string HeroRelativePath = "SampleAssets/VRM_KHR_Examples/khr-character-example.glb";

        /// <summary>Absolute, runtime-readable path of the local hero character.</summary>
        public static string HeroAbsolutePath => Path.Combine(Application.dataPath, HeroRelativePath);

        /// <summary>Project-relative paths of the per-role visibility-hint variants of the hero
        /// (built by tools/make_hero_variants.py). Each isolates one <c>KHR_node_visibility_hint</c>
        /// role; the third_person one also carries a <c>KHR_mesh_primitive_visibility_hint</c> example.</summary>
        public static readonly string[] HeroVariantRelativePaths =
        {
            "SampleAssets/VRM_KHR_Examples/khr-character-example-always.glb",
            "SampleAssets/VRM_KHR_Examples/khr-character-example-first-person.glb",
            "SampleAssets/VRM_KHR_Examples/khr-character-example-third-person.glb",
        };

        /// <summary>Yields <c>(label, absolutePath)</c> for the hero and each per-role variant present on
        /// disk. Label form: <c>"Hero: &lt;filename-without-ext&gt;"</c>. Shared by the GlbViewer and Animation
        /// Sandbox pickers so the hero family shows as single-file entries outside the directory-based catalog.</summary>
        public static IEnumerable<(string Label, string Path)> EnumerateHeroFiles()
        {
            // Gate on the glTF magic (not File.Exists): an un-smudged Git-LFS pointer is a ~130-byte text file that
            // would otherwise populate the picker and then throw on import. IsRealGlb keeps pointers out of the list.
            if (HeroIsRealGlb)
                yield return ("Hero: khr-character-example", HeroAbsolutePath);
            foreach (var rel in HeroVariantRelativePaths)
            {
                string abs = System.IO.Path.Combine(Application.dataPath, rel);
                if (IsRealGlb(abs))
                    yield return ($"Hero: {System.IO.Path.GetFileNameWithoutExtension(rel)}", abs);
            }
        }

        /// <summary>True when the hero file is present on disk (does NOT prove it is a real GLB — see
        /// <see cref="HeroIsRealGlb"/>; an un-smudged Git LFS pointer also passes File.Exists).</summary>
        public static bool HeroExists => File.Exists(HeroAbsolutePath);

        /// <summary>
        /// True only when the hero file exists AND is a real glTF/GLB (see <see cref="IsRealGlb"/>).
        /// Use this — not <see cref="HeroExists"/> — to gate hero-dependent tests/demos: it distinguishes a real
        /// LFS-smudged GLB from an un-smudged LFS pointer (which would otherwise be mis-parsed as a GLB and throw
        /// on import rather than skip).
        /// </summary>
        public static bool HeroIsRealGlb => IsRealGlb(HeroAbsolutePath);

        /// <summary>
        /// The decision <see cref="LoadDemoCharacterAsync"/> makes when no explicit path is given: true only when
        /// tests aren't forcing synthetic AND the hero is a real (LFS-smudged) GLB. Controllers use this — not
        /// <see cref="HeroExists"/> — for the "which character loaded" blurb, so the label matches what the loader
        /// actually loads on an un-smudged clone (where the hero is a pointer and the synthetic fallback loads).
        /// </summary>
        public static bool WouldLoadHero => !ForceSyntheticForTests && HeroIsRealGlb;

        // The first four bytes of a binary glTF container spell "glTF": g=0x67 l=0x6C T=0x54 F=0x46
        // (the magic 0x676C5446). A Git LFS pointer that was never smudged is a ~130-byte ASCII text file
        // ("version https://git-lfs..."), so it fails this check and reads as NOT a real GLB.
        /// <summary>
        /// True when <paramref name="absolutePath"/> is a real, loadable glTF/GLB on disk — not missing and not an
        /// un-smudged Git-LFS pointer. A binary GLB is detected by its four-byte "glTF" magic (0x676C5446); a text
        /// .gltf document is accepted when it exists and is not an LFS pointer. Use this before feeding any
        /// hero/sample path to the importer so a fresh, un-pulled LFS clone shows a friendly hint instead of a raw
        /// parse exception (see <see cref="DescribeUnloadable"/>).
        /// </summary>
        public static bool IsRealGlb(string absolutePath)
        {
            try
            {
                if (string.IsNullOrEmpty(absolutePath) || !File.Exists(absolutePath)) return false;
                var head = new byte[20];
                int read;
                using (var stream = File.OpenRead(absolutePath))
                    read = stream.Read(head, 0, head.Length);
                if (read < 4) return false;
                // Binary GLB: first four bytes are the glTF magic "glTF".
                if (head[0] == 0x67 && head[1] == 0x6C && head[2] == 0x54 && head[3] == 0x46) return true;
                // Un-smudged Git-LFS pointers are ASCII text beginning "version https://git-lfs…" — reject them.
                string ascii = System.Text.Encoding.ASCII.GetString(head, 0, read);
                if (ascii.StartsWith("version https://git-lfs")) return false;
                // Not the binary magic and not a pointer: only a .gltf JSON document is loadable here.
                return string.Equals(Path.GetExtension(absolutePath), ".gltf", System.StringComparison.OrdinalIgnoreCase);
            }
            catch (System.Exception e) { Debug.LogException(e); return false; }
        }

        /// <summary>
        /// A user-facing reason <paramref name="absolutePath"/> can't be loaded, or null when it IS a real glTF/GLB.
        /// Distinguishes "not found" from an un-smudged Git-LFS pointer (the fresh-clone footgun) so demos can show
        /// an actionable message instead of a raw importer exception.
        /// </summary>
        public static string DescribeUnloadable(string absolutePath)
        {
            if (string.IsNullOrEmpty(absolutePath)) return "No file selected.";
            if (!File.Exists(absolutePath)) return $"Not found: {Path.GetFileName(absolutePath)}";
            if (!IsRealGlb(absolutePath))
                return $"'{Path.GetFileName(absolutePath)}' looks like a Git-LFS pointer, not a real GLB — run `git lfs pull` to download it.";
            return null;
        }

        /// <summary>
        /// Load the demo's character: the local hero (VRM-origin, consumed via KHR_character) when present, else the
        /// scene-appropriate committed synthetic fallback (e.g. "SC-Face.glb"). Returns the scene root, or null on
        /// failure. Read <see cref="WouldLoadHero"/> to label which one loaded; show a "Loading…" hint while awaiting
        /// (the hero is a ~10 MB GLB).
        /// TODO: if runtime-loading the hero per scene proves slow at validation, switch this to instantiate the
        /// editor-imported prefab in the editor; keep this runtime LoadAsync path for player builds / fresh clones.
        /// </summary>
        /// <summary>
        /// When true, <see cref="LoadDemoCharacterAsync"/> skips the (large) hero and always loads the synthetic
        /// fallback. Smoke tests set this so demo scenes boot fast and deterministically without the ~10 MB hero import.
        /// </summary>
        public static bool ForceSyntheticForTests = false;

        public static Task<GameObject> LoadDemoCharacterAsync(Transform parent, string fallbackSyntheticFileName)
        {
            string path = WouldLoadHero ? HeroAbsolutePath : SyntheticPath(fallbackSyntheticFileName);
            return LoadAsync(path, parent);
        }

        /// <summary>A short header noting which demo character loaded (hero vs synthetic fallback) + the VRM caveat.</summary>
        public static string DemoCharacterBlurb(bool usingHero, string fallbackName) => usingHero
            ? "Character: hero khr-character-example (VRM-origin — consumed via KHR_character; VRMC_* ignored)."
            : $"Character: synthetic fallback ({fallbackName}).";

        /// <summary>
        /// Enable the (disabled-by-default, non-ratified) KHR-Character import plugin (and set its Rig) plus the
        /// visibility-hint import plugin on the shared settings. Must run before an <see cref="ImportOptions"/> is
        /// constructed, because the default import context is built from the settings' enabled plugins at
        /// construction time. Idempotent. This is the SINGLE place the project turns import plugins on — enabling the
        /// visibility-hint plugin here (rather than only as a side effect of the VisibilityHints scene) makes hint
        /// import deterministic regardless of which demo scene ran first.
        /// </summary>
        public static void EnableImportPlugin(RigImportMode rig = RigImportMode.Humanoid)
        {
            var settings = GLTFSettings.GetOrCreateSettings();
            if (settings == null || settings.ImportPlugins == null) return;
            foreach (var plugin in settings.ImportPlugins)
            {
                if (plugin is KhrCharacterImportPlugin characterImport)
                {
                    characterImport.Enabled = true;
                    characterImport.Rig = rig;
                }
                else if (plugin is VisibilityHintImportPlugin visibilityHint)
                {
                    visibilityHint.Enabled = true;
                }
            }
        }

        /// <summary>
        /// Load a GLB/glTF by absolute path, parenting the result under <paramref name="parent"/> (null = scene
        /// root). Returns the scene root GameObject, or null on failure.
        /// </summary>
        public static async Task<GameObject> LoadAsync(string absolutePath, Transform parent)
        {
            LastLoadedSourcePath = absolutePath;
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
            catch (System.Exception e)
            {
                // Documented contract: return null on failure. A corrupt wire or an un-smudged Git-LFS pointer
                // throws from the importer; log it so the cause is visible, but don't propagate — callers null-check.
                Debug.LogException(e);
                scene = null;
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
            LastLoadedSourcePath = null; // in-memory re-import has no source file on disk
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
            catch (System.Exception e)
            {
                // Documented contract: return null on failure (corrupt bytes throw from the importer). Log it so
                // the cause is visible, but don't propagate — callers null-check.
                Debug.LogException(e);
                scene = null;
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
        public static string ExtractGltfJson(byte[] bytes)
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
