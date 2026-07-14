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
        // Rotation curves are authored as Euler-angle channels in DEGREES, never as raw quaternion
        // components. Animating a single quaternion component (e.g. "localRotation.z") leaves the
        // other three — including w — at 0, so Unity reads a non-normalized quaternion and snaps the
        // bone to a ~180° flip (the "totally warped" pose). Euler channels always build a valid,
        // normalized rotation; BuildClip fills any untouched axis with a constant-0 curve so every
        // rotation is fully specified.
        private const string EulerPrefix = "localEulerAnglesRaw.";
        private const string EulerX = EulerPrefix + "x";
        private const string EulerZ = EulerPrefix + "z";
        private const string PositionPrefix = "localPosition.";
        private static readonly string[] EulerAxes = { "x", "y", "z" };

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
                    Property = EulerZ, Curve = SineCurve(4f, 3f, Mathf.PI / 2, 33) },
            },
        });

        private static ClipData WaveData => _wave ?? (_wave = new ClipData
        {
            Name = "Wave",
            WrapMode = WrapMode.Once,
            Curves = {
                new CurveData { VocabKey = "rightUpperArm", Type = typeof(Transform),
                    Property = EulerZ, Curve = WaveCurve() },
            },
        });

        private static ClipData NodData => _nod ?? (_nod = new ClipData
        {
            Name = "Nod",
            WrapMode = WrapMode.Once,
            Curves = {
                new CurveData { VocabKey = "head", Type = typeof(Transform),
                    Property = EulerX, Curve = NodCurve() },
            },
        });

        private static ClipData WalkInPlaceData => _walkInPlace ?? (_walkInPlace = new ClipData
        {
            Name = "WalkInPlace",
            WrapMode = WrapMode.Loop,
            Curves = {
                new CurveData { VocabKey = "leftUpperLeg", Type = typeof(Transform),
                    Property = EulerX, Curve = LeftLegCurve() },
                new CurveData { VocabKey = "rightUpperLeg", Type = typeof(Transform),
                    Property = EulerX, Curve = RightLegCurve() },
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
            System.Func<string, Transform> resolveBone = vocabKey =>
                skeleton.TryGetBone(vocabKey, out var b) ? b : null;
            var clip = BuildClip(data, resolve, character, characterAdaptive: true, resolveBone: resolveBone);
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
            bool characterAdaptive = false,
            System.Func<string, Transform> resolveBone = null)
        {
            var clip = new AnimationClip { frameRate = data.FrameRate };
            clip.wrapMode = data.WrapMode;
            clip.legacy = false;
            clip.name = characterAdaptive && character != null
                ? $"{data.Name}@{character.name}"
                : data.Name;
            clip.hideFlags = HideFlags.HideAndDontSave;

            // Group rotation (euler) curves by vocab. When we can resolve the ACTUAL bone we bake a
            // BIND-RELATIVE rotation (bindRotation * Euler(delta)) into four quaternion component curves,
            // because a clip's rotation is applied as an ABSOLUTE local rotation: authoring a small
            // absolute euler would OVERWRITE a non-identity bind pose (VRoid J_Bip_* bones) and collapse
            // the rig. On identity-bind synthetic rigs (or when no bone resolves) we keep the absolute
            // euler channels (bind == identity, so absolute == bind-relative there).
            var rotationByVocab = new Dictionary<string, List<CurveData>>();
            var otherCurves = new List<CurveData>();
            foreach (var curve in data.Curves)
            {
                if (curve.Property.StartsWith(EulerPrefix))
                {
                    if (!rotationByVocab.TryGetValue(curve.VocabKey, out var list))
                        rotationByVocab[curve.VocabKey] = list = new List<CurveData>();
                    list.Add(curve);
                }
                else otherCurves.Add(curve);
            }

            float maxTime = 0f;

            // Position (bind-relative offset — see OffsetCurve) + any other channels, applied verbatim.
            foreach (var curve in otherCurves)
            {
                maxTime = Mathf.Max(maxTime, CurveDuration(curve.Curve));
                var authored = curve.Curve;
                if (curve.Property.StartsWith(PositionPrefix))
                {
                    var bone = resolveBone?.Invoke(curve.VocabKey);
                    if (bone != null)
                        authored = OffsetCurve(curve.Curve, BindComponent(bone.localPosition, curve.Property));
                }
                foreach (var path in resolvePaths(curve.VocabKey, character))
                    clip.SetCurve(path, curve.Type, curve.Property, authored);
            }

            // Rotations: bind-relative quaternion bake when the bone resolves; else absolute euler.
            var eulerAxesByPath = new Dictionary<string, HashSet<string>>();
            foreach (var kv in rotationByVocab)
            {
                foreach (var c in kv.Value) maxTime = Mathf.Max(maxTime, CurveDuration(c.Curve));
                var bone = resolveBone?.Invoke(kv.Key);
                if (bone != null)
                {
                    BakeBindRelativeRotation(clip, resolvePaths(kv.Key, character), bone.localRotation, kv.Value);
                    continue;
                }
                foreach (var c in kv.Value)
                    foreach (var path in resolvePaths(kv.Key, character))
                    {
                        clip.SetCurve(path, c.Type, c.Property, c.Curve);
                        if (!eulerAxesByPath.TryGetValue(path, out var axes))
                            eulerAxesByPath[path] = axes = new HashSet<string>();
                        axes.Add(c.Property.Substring(EulerPrefix.Length));
                    }
            }

            // Complete partially-bound ABSOLUTE euler rotations (agnostic/unresolved paths only) with
            // constant-0 fillers so the sampled rotation is fully specified (never a degenerate quaternion).
            if (maxTime <= 0f) maxTime = 1f;
            var zero = ConstantCurve(maxTime, 0f);
            foreach (var kv in eulerAxesByPath)
                foreach (var axis in EulerAxes)
                    if (!kv.Value.Contains(axis))
                        clip.SetCurve(kv.Key, typeof(Transform), EulerPrefix + axis, zero);

            return clip;
        }

        // Bake bindRotation * Euler(delta(t)) into four localRotation.{x,y,z,w} curves on each path.
        // Sampling at the union of the source euler keyframe times preserves the authored motion; the
        // full-quaternion output is normalized by Unity on playback and never triggers the single-component
        // ~180° flip. Consecutive samples are kept on the same hemisphere so linear component interpolation
        // takes the short way around.
        private static void BakeBindRelativeRotation(
            AnimationClip clip, IEnumerable<string> paths, Quaternion bind, List<CurveData> axisCurves)
        {
            AnimationCurve cx = null, cy = null, cz = null;
            foreach (var c in axisCurves)
            {
                switch (c.Property.Substring(EulerPrefix.Length))
                {
                    case "x": cx = c.Curve; break;
                    case "y": cy = c.Curve; break;
                    case "z": cz = c.Curve; break;
                }
            }

            var times = new SortedSet<float>();
            foreach (var c in new[] { cx, cy, cz })
                if (c != null)
                    foreach (var k in c.keys) times.Add(k.time);
            if (times.Count == 0) return;

            var kx = new List<Keyframe>(); var ky = new List<Keyframe>();
            var kz = new List<Keyframe>(); var kw = new List<Keyframe>();
            Quaternion prev = Quaternion.identity; bool have = false;
            foreach (float t in times)
            {
                float ex = cx != null ? cx.Evaluate(t) : 0f;
                float ey = cy != null ? cy.Evaluate(t) : 0f;
                float ez = cz != null ? cz.Evaluate(t) : 0f;
                var q = bind * Quaternion.Euler(ex, ey, ez);
                if (have && (q.x * prev.x + q.y * prev.y + q.z * prev.z + q.w * prev.w) < 0f)
                    q = new Quaternion(-q.x, -q.y, -q.z, -q.w);
                prev = q; have = true;
                kx.Add(new Keyframe(t, q.x)); ky.Add(new Keyframe(t, q.y));
                kz.Add(new Keyframe(t, q.z)); kw.Add(new Keyframe(t, q.w));
            }

            var qx = new AnimationCurve(kx.ToArray()); var qy = new AnimationCurve(ky.ToArray());
            var qz = new AnimationCurve(kz.ToArray()); var qw = new AnimationCurve(kw.ToArray());
            foreach (var path in paths)
            {
                clip.SetCurve(path, typeof(Transform), "localRotation.x", qx);
                clip.SetCurve(path, typeof(Transform), "localRotation.y", qy);
                clip.SetCurve(path, typeof(Transform), "localRotation.z", qz);
                clip.SetCurve(path, typeof(Transform), "localRotation.w", qw);
            }
        }

        private static float CurveDuration(AnimationCurve curve)
            => curve != null && curve.length > 0 ? curve[curve.length - 1].time : 0f;

        private static AnimationCurve ConstantCurve(float duration, float value)
            => new AnimationCurve(new Keyframe(0f, value), new Keyframe(duration, value));

        // Re-anchor a delta curve on a bind-pose component so absolute-clip playback oscillates
        // around the bone's existing local position instead of snapping it to the raw delta.
        private static AnimationCurve OffsetCurve(AnimationCurve src, float offset)
        {
            if (offset == 0f) return src;
            var keys = src.keys; // AnimationCurve.keys returns a copy incl. tangent data
            for (int i = 0; i < keys.Length; i++) keys[i].value += offset;
            return new AnimationCurve(keys);
        }

        private static float BindComponent(Vector3 v, string property)
        {
            switch (property[property.Length - 1])
            {
                case 'x': return v.x;
                case 'y': return v.y;
                case 'z': return v.z;
                default:  return 0f;
            }
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

        // Rotation amplitudes are in DEGREES (see EulerPrefix note). Values approximate the
        // previous quaternion-component intent: 0.13 ≈ sin(θ/2) ⇒ θ ≈ 15°, 0.3 ⇒ ≈35°, 0.7 ⇒ ≈89°.
        private static AnimationCurve WaveCurve() => new AnimationCurve(
            new Keyframe(0f, 0f), new Keyframe(0.5f, 80f),
            new Keyframe(1.5f, 80f), new Keyframe(2f, 0f));

        private static AnimationCurve NodCurve() => new AnimationCurve(
            new Keyframe(0f, 0f), new Keyframe(0.5f, 15f),
            new Keyframe(1f, -15f), new Keyframe(1.5f, 0f));

        private static AnimationCurve LeftLegCurve() => new AnimationCurve(
            new Keyframe(0f, 0f), new Keyframe(0.3f, 35f),
            new Keyframe(0.6f, 0f), new Keyframe(0.9f, -35f),
            new Keyframe(1.2f, 0f));

        private static AnimationCurve RightLegCurve() => new AnimationCurve(
            new Keyframe(0f, 0f), new Keyframe(0.3f, -35f),
            new Keyframe(0.6f, 0f), new Keyframe(0.9f, 35f),
            new Keyframe(1.2f, 0f));
    }
}
