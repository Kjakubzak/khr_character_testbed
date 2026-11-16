using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityGLTF;
using UnityGLTF.KhrCharacter;
using UnityGLTF.Loader;
using UnityGLTF.VisibilityHints;

namespace KhrCharacterSamples
{
    /// <summary>
    /// Runtime import through UnityGLTF with the KHR_character plugin enabled as a Unity host would: Humanoid rig mode,
    /// the optional ExpressionController applicator, and no auto-play of expression or reference-pose clips.
    /// </summary>
    public static class CharacterImport
    {
        public static void EnablePlugins(bool expressionController = true)
        {
            var settings = GLTFSettings.GetOrCreateSettings();
            if (settings?.ImportPlugins == null) return;
            foreach (var plugin in settings.ImportPlugins)
            {
                if (plugin is KhrCharacterImportPlugin character)
                {
                    character.Enabled = true;
                    character.Rig = RigImportMode.Humanoid;
                    character.CreateExpressionController = expressionController;
                    character.SuppressNonInteractiveAnimationAutoPlay = true;
                }
                else if (plugin is VisibilityHintImportPlugin visibility)
                {
                    visibility.Enabled = true;
                }
            }
        }

        public static Task<GameObject> LoadFileAsync(string absolutePath, bool expressionController = true)
        {
            EnablePlugins(expressionController);
            var options = NewOptions(out var host);
            options.DataLoader = new UnityWebRequestLoader(Path.GetDirectoryName(absolutePath));
            return Run(new GLTFSceneImporter(Path.GetFileName(absolutePath), options), host);
        }

        public static Task<GameObject> LoadBytesAsync(byte[] glb, bool expressionController = true)
        {
            EnablePlugins(expressionController);
            var options = NewOptions(out var host);
            return Run(new GLTFSceneImporter(new MemoryStream(glb), options), host);
        }

        private static ImportOptions NewOptions(out GameObject host)
        {
            host = new GameObject("KhrCharacterImportHost");
            return new ImportOptions
            {
                AsyncCoroutineHelper = host.AddComponent<AsyncCoroutineHelper>(),
                AnimationMethod = AnimationMethod.Legacy,
            };
        }

        private static async Task<GameObject> Run(GLTFSceneImporter importer, GameObject host)
        {
            try
            {
                await importer.LoadSceneAsync();
                return importer.LastLoadedScene;
            }
            finally
            {
                importer.Dispose();
                UnityEngine.Object.Destroy(host);
            }
        }

        /// <summary>The JSON chunk of a GLB file, read without loading the BIN chunk.</summary>
        public static JObject ReadGlbJson(string path)
        {
            using (var stream = File.OpenRead(path))
            using (var reader = new BinaryReader(stream))
            {
                if (reader.ReadUInt32() != 0x46546C67u) throw new InvalidDataException("not a binary glTF: " + path);
                reader.ReadUInt32();
                reader.ReadUInt32();
                int length = (int)reader.ReadUInt32();
                if (reader.ReadUInt32() != 0x4E4F534Au) throw new InvalidDataException("first GLB chunk is not JSON: " + path);
                return JObject.Parse(Encoding.UTF8.GetString(reader.ReadBytes(length)).TrimEnd(' ', '\0'));
            }
        }

        /// <summary>
        /// Returns a copy of a GLB whose JSON chunk <paramref name="patch"/> edited; the BIN chunk is copied unchanged.
        /// </summary>
        public static byte[] PatchGlbJson(byte[] glb, Action<JObject> patch)
        {
            if (glb.Length < 20 || BitConverter.ToUInt32(glb, 0) != 0x46546C67u)
                throw new InvalidDataException("not a binary glTF");
            var chunks = new List<(uint Type, byte[] Data)>();
            for (int offset = 12; offset + 8 <= glb.Length;)
            {
                int length = (int)BitConverter.ToUInt32(glb, offset);
                uint type = BitConverter.ToUInt32(glb, offset + 4);
                var data = new byte[length];
                Buffer.BlockCopy(glb, offset + 8, data, 0, length);
                chunks.Add((type, data));
                offset += 8 + length;
            }
            int json = chunks.FindIndex(c => c.Type == 0x4E4F534Au);
            var root = JObject.Parse(Encoding.UTF8.GetString(chunks[json].Data).TrimEnd(' ', '\0'));
            patch(root);
            var text = Encoding.UTF8.GetBytes(root.ToString(Formatting.None));
            int padded = (text.Length + 3) & ~3;
            var jsonData = new byte[padded];
            Buffer.BlockCopy(text, 0, jsonData, 0, text.Length);
            for (int i = text.Length; i < padded; i++) jsonData[i] = (byte)' ';
            chunks[json] = (chunks[json].Type, jsonData);

            using (var stream = new MemoryStream())
            using (var writer = new BinaryWriter(stream))
            {
                int total = 12;
                foreach (var c in chunks) total += 8 + c.Data.Length;
                writer.Write(0x46546C67u);
                writer.Write(2u);
                writer.Write((uint)total);
                foreach (var c in chunks)
                {
                    writer.Write((uint)c.Data.Length);
                    writer.Write(c.Type);
                    writer.Write(c.Data);
                }
                return stream.ToArray();
            }
        }

        /// <summary>Adds the top-level KHR_character designation (and its extensionsUsed entry) when absent.</summary>
        public static void AddCharacterDesignation(JObject root)
        {
            var used = root["extensionsUsed"] as JArray ?? new JArray();
            if (!used.Any(t => (string)t == "KHR_character")) used.Add("KHR_character");
            root["extensionsUsed"] = used;
            var extensions = root["extensions"] as JObject ?? new JObject();
            if (extensions["KHR_character"] == null) extensions["KHR_character"] = new JObject();
            root["extensions"] = extensions;
        }

        public static IEnumerable<AnimationClip> Clips(GameObject root)
        {
            var animation = root.GetComponent<Animation>();
            if (animation == null) yield break;
            foreach (AnimationState state in animation)
                if (state.clip != null) yield return state.clip;
        }

        public static AnimationClip FindClip(GameObject root, string name)
        {
            AnimationClip longest = null;
            foreach (var clip in Clips(root))
            {
                if (clip.name == name) return clip;
                if (longest == null || clip.length > longest.length) longest = clip;
            }
            return name == null ? longest : null;
        }
    }

    /// <summary>Evidence output: env KHR_EVIDENCE_DIR, else &lt;project&gt;/Logs/characters.</summary>
    public static class Evidence
    {
        public static string Root
        {
            get
            {
                var dir = Environment.GetEnvironmentVariable("KHR_EVIDENCE_DIR");
                if (string.IsNullOrEmpty(dir))
                    dir = Path.Combine(Directory.GetParent(Application.dataPath).FullName, "Logs", "characters");
                Directory.CreateDirectory(dir);
                return dir;
            }
        }

        public static string Dir(params string[] parts)
        {
            var all = new string[parts.Length + 1];
            all[0] = Root;
            Array.Copy(parts, 0, all, 1, parts.Length);
            var dir = Path.Combine(all);
            Directory.CreateDirectory(dir);
            return dir;
        }

        public static void WriteJson(string path, JToken json)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllText(path, json.ToString(Formatting.Indented) + "\n");
        }

        public static string Sha256(string path)
        {
            using (var sha = System.Security.Cryptography.SHA256.Create())
            using (var stream = File.OpenRead(path))
                return BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", "").ToLowerInvariant();
        }

        public static string SafeName(string name)
        {
            var sb = new StringBuilder();
            foreach (var ch in name ?? "unnamed") sb.Append(char.IsLetterOrDigit(ch) || ch == '-' || ch == '_' ? ch : '_');
            return sb.ToString();
        }
    }
}
