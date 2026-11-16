using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace KhrCharacterSamples
{
    /// <summary>
    /// One character's test manifest (<c>character.json</c>, schema <c>khr-character-unity-test/1</c>), written by
    /// <c>pipeline/generic/unity/make_unity_manifest.py</c>. Every name, role and expected value comes from here.
    /// </summary>
    public sealed class CharacterManifest
    {
        public const string DefaultRootRelative = "SampleAssets/Characters";

        public string Directory { get; private set; }
        public string ManifestPath { get; private set; }
        public JObject Json { get; private set; }
        public string Name => (string)Json["name"];
        public string GlbPath => Path.Combine(Directory, (string)Json["glb"]);
        public bool IsKhrCharacter => (bool?)Json["khr_character"] ?? false;
        public bool IsPrivate => (bool?)Json["private"] ?? true;
        public JObject Expect => (JObject)Json["expect"] ?? new JObject();
        public JObject Retarget => (JObject)Json["retarget"] ?? new JObject();
        public JObject Capture => (JObject)Json["capture"] ?? new JObject();

        public override string ToString() => Name ?? Path.GetFileName(Directory);

        public static CharacterManifest Load(string manifestPath)
        {
            var json = JObject.Parse(File.ReadAllText(manifestPath));
            if ((string)json["schema"] != "khr-character-unity-test/1")
                throw new InvalidDataException($"{manifestPath}: unexpected schema '{json["schema"]}'");
            return new CharacterManifest
            {
                ManifestPath = manifestPath,
                Directory = Path.GetDirectoryName(manifestPath),
                Json = json,
            };
        }

        /// <summary>
        /// The characters root: env KHR_CHARACTERS_DIR, else Assets/SampleAssets/Characters. KHR_CHARACTERS
        /// (comma-separated names) restricts the set.
        /// </summary>
        public static string RootDirectory =>
            Environment.GetEnvironmentVariable("KHR_CHARACTERS_DIR") is string dir && dir.Length > 0
                ? dir
                : Path.Combine(Application.dataPath, DefaultRootRelative);

        public static IReadOnlyList<CharacterManifest> All()
        {
            var root = RootDirectory;
            var list = new List<CharacterManifest>();
            if (!System.IO.Directory.Exists(root)) return list;
            var only = (Environment.GetEnvironmentVariable("KHR_CHARACTERS") ?? "")
                .Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries).Select(s => s.Trim()).ToHashSet();
            foreach (var dir in System.IO.Directory.GetDirectories(root).OrderBy(d => d, StringComparer.Ordinal))
            {
                var path = Path.Combine(dir, "character.json");
                if (!File.Exists(path)) continue;
                var manifest = Load(path);
                if (only.Count > 0 && !only.Contains(manifest.Name)) continue;
                list.Add(manifest);
            }
            return list;
        }

        public IEnumerable<string> Strings(JToken token) =>
            token is JArray array ? array.Select(t => (string)t).Where(s => s != null) : Enumerable.Empty<string>();

        public float Float(JObject obj, string key, float fallback) =>
            obj != null && obj[key] != null && obj[key].Type != JTokenType.Null ? (float)obj[key] : fallback;

        /// <summary>The retarget clips this character uses, resolved from the shared clips index.</summary>
        public IReadOnlyList<ClipEntry> RetargetClips(bool videoOnly = false)
        {
            var indexRel = (string)Retarget["clips_index"];
            if (string.IsNullOrEmpty(indexRel)) return Array.Empty<ClipEntry>();
            var index = ClipIndex.Load(Path.GetFullPath(Path.Combine(Directory, indexRel)));
            var wanted = Strings(Retarget[videoOnly ? "video_clips" : "clips"]).ToList();
            if (wanted.Count == 0 && !videoOnly) return index.Clips;
            return wanted.Select(n => index.Clips.FirstOrDefault(c => c.Name == n)).Where(c => c != null).ToList();
        }
    }

    public sealed class ClipEntry
    {
        public string Name;
        public string SourcePath;
        public string Animation;
        public float Duration;
        public string Sha256;
        public bool AddKhrCharacter;
        public override string ToString() => Name;
    }

    /// <summary>The shared retarget clip index (<c>clips.json</c>, schema <c>khr-character-unity-clips/1</c>).</summary>
    public sealed class ClipIndex
    {
        public string Path;
        public string Root;
        public List<ClipEntry> Clips = new List<ClipEntry>();

        public static ClipIndex Load(string path)
        {
            var json = JObject.Parse(File.ReadAllText(path));
            if ((string)json["schema"] != "khr-character-unity-clips/1")
                throw new InvalidDataException($"{path}: unexpected schema '{json["schema"]}'");
            var root = ExpandEnvironment((string)json["root"] ?? "");
            if (!System.IO.Path.IsPathRooted(root))
                root = System.IO.Path.GetFullPath(System.IO.Path.Combine(System.IO.Path.GetDirectoryName(path), root));
            var index = new ClipIndex { Path = path, Root = root };
            foreach (var c in (JArray)json["clips"] ?? new JArray())
                index.Clips.Add(new ClipEntry
                {
                    Name = (string)c["name"],
                    SourcePath = System.IO.Path.Combine(root, (string)c["src"]),
                    Animation = (string)c["animation"],
                    Duration = (float?)c["duration"] ?? 0f,
                    Sha256 = (string)c["sha256"],
                    AddKhrCharacter = (bool?)c["add_khr_character"] ?? false,
                });
            return index;
        }

        /// <summary>Expands ${NAME} from the environment; an unset variable is an error, never an empty path.</summary>
        public static string ExpandEnvironment(string text) =>
            Regex.Replace(text, @"\$\{([A-Za-z_][A-Za-z0-9_]*)\}", m =>
                Environment.GetEnvironmentVariable(m.Groups[1].Value) is string v && v.Length > 0
                    ? v
                    : throw new InvalidOperationException($"environment variable {m.Groups[1].Value} is not set"));
    }
}
