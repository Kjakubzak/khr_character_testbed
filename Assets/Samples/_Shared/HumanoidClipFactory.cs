using System.Collections.Generic;
using UnityEngine;
using UnityGLTF.KhrCharacter;

namespace Samples.Shared
{
    /// <summary>
    /// Procedural humanoid-adjacent <see cref="AnimationClip"/>s generated in code, so the testbed
    /// ships committed motion without any third-party asset dependency (Mixamo etc.). Two flavours:
    ///
    ///   * <see cref="IdleSway"/> / <see cref="Wave"/> / <see cref="Nod"/> / <see cref="WalkInPlace"/>
    ///     (or <see cref="All"/>) — character-agnostic clips using vocab-key + PascalCase-node paths.
    ///     Works on characters whose bone names match one of the built-in conventions (SC-Body
    ///     PascalCase, KHR-vocab lowercase). Cached, shared across scenes.
    ///   * <see cref="BuildForCharacter"/> / <see cref="AllForCharacter"/> — clips whose curve paths
    ///     are resolved against the character's actual bone names via
    ///     <see cref="SkeletonMap.TryGetBone"/>. Works on ANY KHR character regardless of naming
    ///     convention (VRoid <c>J_Bip_C_Head</c>, Mixamo <c>mixamorig:Head</c>, custom rigs). Freshly
    ///     built per character (not cached across characters).
    ///
    /// Clip inventory:
    ///   * IdleSway     : gentle hips Y bob + spine roll — a 4 s loop that reads as "breathing".
    ///   * Wave         : right-shoulder pitch/yaw for a 2 s wave gesture.
    ///   * Nod          : head-bone pitch nod (0/+15/-15/0 over 1.5 s).
    ///   * WalkInPlace  : alternating hip/knee TRS — a 1.2 s two-step loop.
    ///
    /// Clips are NON-legacy — playable via <see cref="Animator"/> + Playables API. The
    /// <see cref="RigMode.Legacy"/> playback path in <see cref="AnimationBinder"/> is intended for
    /// glb-embedded clips (which UnityGLTF's KHR-Character importer surfaces as legacy
    /// <see cref="Animation"/> clips on the character root); pointing Legacy playback at a
    /// procedural clip logs a warning and no-ops.
    /// </summary>
    public static class HumanoidClipFactory
    {
        // ── Authoring data: curves keyed by KHR vocab, retargeted per character on demand ─────
        //
        // Storing curves ONCE keyed by vocab (rather than by path) lets us build both character-
        // agnostic clips (paths = fallback conventions) AND character-adaptive clips (paths =
        // actual bone names resolved via SkeletonMap.TryGetBone) from the same source of truth.

        private sealed class ClipData
        {
            public string Name;
            public float FrameRate = 30f;
            public WrapMode WrapMode = WrapMode.Loop;
            public List<CurveData> Curves = new List<CurveData>();
        }

        private sealed class CurveData
        {
            public string VocabKey;        // e.g. "hips" — a KHR skeleton_mapping vocabulary joint
            public System.Type Type;
            public string Property;        // e.g. "localPosition.y"
            public AnimationCurve Curve;
        }

        private static ClipData _idleSway, _wave, _nod, _walkInPlace;

        private static ClipData IdleSwayData => _idleSway ?? (_idleSway = new ClipData
        {
            Name = "IdleSway",
            WrapMode = WrapMode.Loop,
            Curves = {
                new CurveData { VocabKey = "hips", Type = typeof(Transform),
                    Property = "localPosition.y", Curve = SineCurve(4f, 0.01f, 0f, 33) },
                new CurveData { VocabKey = "spine", Type = typeof(Transform),
                    Property = "localRotation.z", Curve = SineCurve(4f, 0.026f, Mathf.PI / 2, 33) },
            },
        });

        private static ClipData WaveData => _wave ?? (_wave = new ClipData
        {
            Name = "Wave",
            WrapMode = WrapMode.Once,
            Curves = {
                new CurveData { VocabKey = "rightUpperArm", Type = typeof(Transform),
                    Property = "localRotation.z", Curve = WaveCurve() },
            },
        });

        private static ClipData NodData => _nod ?? (_nod = new ClipData
        {
            Name = "Nod",
            WrapMode = WrapMode.Once,
            Curves = {
                new CurveData { VocabKey = "head", Type = typeof(Transform),
                    Property = "localRotation.x", Curve = NodCurve() },
            },
        });

        private static ClipData WalkInPlaceData => _walkInPlace ?? (_walkInPlace = new ClipData
        {
            Name = "WalkInPlace",
            WrapMode = WrapMode.Loop,
            Curves = {
                new CurveData { VocabKey = "leftUpperLeg", Type = typeof(Transform),
                    Property = "localRotation.x", Curve = LeftLegCurve() },
                new CurveData { VocabKey = "rightUpperLeg", Type = typeof(Transform),
                    Property = "localRotation.x", Curve = RightLegCurve() },
                new CurveData { VocabKey = "hips", Type = typeof(Transform),
                    Property = "localPosition.y", Curve = SineCurve(0.6f, 0.015f, 0f, 25) },
            },
        });

        // ── Character-agnostic clip API (static cache, shared) ────────────────────────────────

        private static readonly Dictionary<string, AnimationClip> _staticCache = new Dictionary<string, AnimationClip>();

        public static IEnumerable<AnimationClip> All()
        {
            yield return IdleSway;
            yield return Wave;
            yield return Nod;
            yield return WalkInPlace;
        }

        public static AnimationClip IdleSway    => GetOrBuildStatic(IdleSwayData);
        public static AnimationClip Wave        => GetOrBuildStatic(WaveData);
        public static AnimationClip Nod         => GetOrBuildStatic(NodData);
        public static AnimationClip WalkInPlace => GetOrBuildStatic(WalkInPlaceData);

        private static AnimationClip GetOrBuildStatic(ClipData data)
        {
            if (_staticCache.TryGetValue(data.Name, out var cached) && cached != null) return cached;
            var clip = BuildClip(data, ResolveStaticPath);
            _staticCache[data.Name] = clip;
            return clip;
        }

        // Fallback path resolution when no SkeletonMap is available. Tries the vocab key AND the
        // PascalCase-node variant (which SC-Body / most in-code synthetic rigs use). Dead paths
        // are silent no-ops on the animator, so returning multiple paths per vocab is cheap.
        private static IEnumerable<string> ResolveStaticPath(string vocabKey, GameObject _)
        {
            yield return vocabKey;
            if (!string.IsNullOrEmpty(vocabKey) && char.IsLower(vocabKey[0]))
                yield return char.ToUpper(vocabKey[0]) + vocabKey.Substring(1);
        }

        // ── Character-adaptive clip API (per-character resolution via SkeletonMap) ─────────────

        /// <summary>Build all procedural clips with paths resolved against
        /// <paramref name="character"/>'s KHR skeleton mapping. Each returned clip has curves
        /// that target the character's ACTUAL bone names (e.g. VRoid <c>J_Bip_C_Head</c>,
        /// Mixamo <c>mixamorig:Head</c>) — so procedural motion produces visible results on any
        /// rig, not just the ones whose bone names match built-in conventions.
        ///
        /// Falls back to the static clip when the character has no <see cref="SkeletonMap"/>
        /// (e.g. a plain glb loaded via GlbViewer that isn't a KHR character).</summary>
        public static IEnumerable<AnimationClip> AllForCharacter(GameObject character)
        {
            yield return BuildForCharacter("IdleSway", character);
            yield return BuildForCharacter("Wave", character);
            yield return BuildForCharacter("Nod", character);
            yield return BuildForCharacter("WalkInPlace", character);
        }

        /// <summary>Build one clip by name with paths resolved for <paramref name="character"/>.
        /// Valid names: <c>IdleSway</c>, <c>Wave</c>, <c>Nod</c>, <c>WalkInPlace</c>.</summary>
        public static AnimationClip BuildForCharacter(string clipName, GameObject character)
        {
            ClipData data = clipName switch
            {
                "IdleSway" => IdleSwayData,
                "Wave" => WaveData,
                "Nod" => NodData,
                "WalkInPlace" => WalkInPlaceData,
                _ => null,
            };
            if (data == null || character == null) return null;
            var hub = character.GetComponent<KhrCharacter>();
            var skeleton = hub != null ? hub.Skeleton : null;
            if (skeleton == null) return GetOrBuildStatic(data); // no map, use fallback clip

            System.Func<string, GameObject, IEnumerable<string>> resolve = (vocabKey, ch) =>
                ResolveSkeletonPath(vocabKey, ch, skeleton);
            var clip = BuildClip(data, resolve, character, characterAdaptive: true);
            return clip;
        }

        // Resolve a vocab key to the character-relative path via SkeletonMap. Falls back to
        // static conventions if the vocab key isn't in the skeleton map (e.g. the character's
        // KHR skeleton doesn't declare a "rightUpperArm").
        private static IEnumerable<string> ResolveSkeletonPath(string vocabKey, GameObject character, SkeletonMap skeleton)
        {
            if (skeleton.TryGetBone(vocabKey, out var bone) && bone != null)
            {
                var rel = ComputeRelativePath(character.transform, bone);
                if (!string.IsNullOrEmpty(rel)) { yield return rel; yield break; }
            }
            foreach (var p in ResolveStaticPath(vocabKey, character)) yield return p;
        }

        // ── Common clip builder ───────────────────────────────────────────────────────────────

        private static AnimationClip BuildClip(
            ClipData data,
            System.Func<string, GameObject, IEnumerable<string>> resolvePaths,
            GameObject character = null,
            bool characterAdaptive = false)
        {
            var clip = new AnimationClip { frameRate = data.FrameRate };
            clip.wrapMode = data.WrapMode;
            clip.legacy = false;
            clip.name = characterAdaptive && character != null
                ? $"{data.Name}@{character.name}"
                : data.Name;
            clip.hideFlags = HideFlags.HideAndDontSave;
            foreach (var curve in data.Curves)
                foreach (var path in resolvePaths(curve.VocabKey, character))
                    clip.SetCurve(path, curve.Type, curve.Property, curve.Curve);
            return clip;
        }

        // ── Path arithmetic + shared curve authors ─────────────────────────────────────────────

        internal static string ComputeRelativePath(Transform root, Transform target)
        {
            if (root == null || target == null || target == root) return string.Empty;
            var parts = new List<string>();
            var t = target;
            while (t != null && t != root)
            {
                parts.Insert(0, t.name);
                t = t.parent;
            }
            return t == root ? string.Join("/", parts) : string.Empty;
        }

        private static AnimationCurve SineCurve(float period, float amplitude, float offset, int samples)
        {
            var keys = new Keyframe[samples];
            float w = 2f * Mathf.PI / period;
            for (int i = 0; i < samples; i++)
            {
                float t = i / (float)(samples - 1) * period;
                keys[i] = new Keyframe(t, amplitude * Mathf.Sin(w * t + offset));
            }
            var c = new AnimationCurve(keys);
            for (int i = 0; i < samples; i++) c.SmoothTangents(i, 0f);
            return c;
        }

        private static AnimationCurve WaveCurve() => new AnimationCurve(
            new Keyframe(0f, 0f), new Keyframe(0.5f, 0.7f),
            new Keyframe(1.5f, 0.7f), new Keyframe(2f, 0f));

        private static AnimationCurve NodCurve() => new AnimationCurve(
            new Keyframe(0f, 0f), new Keyframe(0.5f, 0.13f),
            new Keyframe(1f, -0.13f), new Keyframe(1.5f, 0f));

        private static AnimationCurve LeftLegCurve() => new AnimationCurve(
            new Keyframe(0f, 0f), new Keyframe(0.3f, 0.3f),
            new Keyframe(0.6f, 0f), new Keyframe(0.9f, -0.3f),
            new Keyframe(1.2f, 0f));

        private static AnimationCurve RightLegCurve() => new AnimationCurve(
            new Keyframe(0f, 0f), new Keyframe(0.3f, -0.3f),
            new Keyframe(0.6f, 0f), new Keyframe(0.9f, 0.3f),
            new Keyframe(1.2f, 0f));
    }
}
