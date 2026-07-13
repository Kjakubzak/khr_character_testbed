using System.Collections.Generic;
using UnityEngine;

namespace Samples.Shared
{
    /// <summary>
    /// Procedural humanoid-adjacent <see cref="AnimationClip"/>s generated in code, so the testbed
    /// ships committed motion without any third-party asset dependency (Mixamo etc.). Every clip
    /// targets bone-name paths that match the KHR humanoid vocabulary
    /// (<c>hips/spine/chest/neck/head</c> and their common variants), which is what
    /// <see cref="KhrCharacter.SkeletonMap"/> populates when
    /// <see cref="KhrCharacter.SkeletonMap.SwitchRigMode"/> is called with either Generic or
    /// Humanoid. The clips are Legacy (playable via <c>Animation</c>) AND compatible with a Generic
    /// <c>Animator</c> — they don't rely on Mecanim's Humanoid muscle system, so they work with any
    /// character whose bones use the expected names, without requiring Avatar retargeting.
    ///
    /// Clip inventory (all ~0.1-1 KB at Unity's default keyframe density):
    /// * IdleSway     : gentle hips Y bob + spine roll — a 4 s loop that reads as "breathing".
    /// * Wave         : right-shoulder pitch/yaw for a 2 s wave gesture.
    /// * Nod          : head-bone pitch nod (0/+15/-15/0 over 1.5 s).
    /// * WalkInPlace  : alternating hip/knee TRS — a 1.2 s two-step loop.
    ///
    /// Clips are NON-legacy — playable via <see cref="Animator"/> + Playables API. The
    /// <see cref="RigMode.Legacy"/> playback path in <see cref="AnimationBinder"/> is intended for
    /// glb-embedded clips (which UnityGLTF's KHR-Character importer surfaces as legacy
    /// <see cref="Animation"/> clips on the character root); pointing Legacy playback at a
    /// procedural clip logs a warning and no-ops.
    ///
    /// The factory is a static cache: each clip is generated exactly once per Unity domain reload
    /// and returned by reference on subsequent calls. Callers may safely share the reference across
    /// scenes; the cached clips are marked <c>hideFlags = HideAndDontSave</c> so they survive
    /// scene loads and don't leak into the Editor's Project window.
    /// </summary>
    public static class HumanoidClipFactory
    {
        private static readonly Dictionary<string, AnimationClip> _cache = new Dictionary<string, AnimationClip>();

        /// <summary>Every procedural clip, in stable declaration order (matches the dropdown UI).</summary>
        public static IEnumerable<AnimationClip> All()
        {
            yield return IdleSway;
            yield return Wave;
            yield return Nod;
            yield return WalkInPlace;
        }

        public static AnimationClip IdleSway     => GetOrBuild("IdleSway", BuildIdleSway);
        public static AnimationClip Wave         => GetOrBuild("Wave", BuildWave);
        public static AnimationClip Nod          => GetOrBuild("Nod", BuildNod);
        public static AnimationClip WalkInPlace  => GetOrBuild("WalkInPlace", BuildWalkInPlace);

        private static AnimationClip GetOrBuild(string name, System.Func<AnimationClip> build)
        {
            if (_cache.TryGetValue(name, out var cached) && cached != null) return cached;
            var clip = build();
            clip.name = name;
            // NON-legacy: modern Animator / Playables API requires legacy=false; the sandbox's
            // Legacy playback path is intended for glb-embedded clips (which import as legacy
            // via UnityGLTF). Procedural clips play through Humanoid / Generic modes only.
            clip.legacy = false;
            clip.hideFlags = HideFlags.HideAndDontSave;
            _cache[name] = clip;
            return clip;
        }

        // ── Individual clip builders ────────────────────────────────────

        private static AnimationClip BuildIdleSway()
        {
            var clip = new AnimationClip { frameRate = 30f };
            clip.wrapMode = WrapMode.Loop;
            // Hips Y bob: ±1 cm over a 4 s cosine cycle.
            var hipsY = SineCurve(period: 4f, amplitude: 0.01f, offset: 0f, samples: 33);
            clip.SetCurve("hips", typeof(Transform), "localPosition.y", hipsY);
            // Spine roll: ±3° over a 4 s sine cycle (out of phase with the bob).
            var spineRoll = SineCurve(period: 4f, amplitude: 0.026f, offset: Mathf.PI / 2, samples: 33);
            clip.SetCurve("spine", typeof(Transform), "localRotation.z", spineRoll);
            return clip;
        }

        private static AnimationClip BuildWave()
        {
            var clip = new AnimationClip { frameRate = 30f };
            clip.wrapMode = WrapMode.Once;
            // Right shoulder / arm raise: rotate rightUpperArm.z from 0 to ~90° and back.
            // Multiple bone-name variants tried (rightUpperArm / RightShoulder / RightArm) via
            // additive curves so the clip works across common naming conventions the KHR
            // skeleton mapping surfaces.
            var raise = new AnimationCurve(
                new Keyframe(0f, 0f, 0f, 0f),
                new Keyframe(0.5f, 0.7f, 0f, 0f),
                new Keyframe(1.5f, 0.7f, 0f, 0f),
                new Keyframe(2f, 0f, 0f, 0f));
            foreach (var path in new[] { "rightUpperArm", "RightUpperArm", "RightShoulder", "rightShoulder", "RightArm" })
                clip.SetCurve(path, typeof(Transform), "localRotation.z", raise);
            return clip;
        }

        private static AnimationClip BuildNod()
        {
            var clip = new AnimationClip { frameRate = 30f };
            clip.wrapMode = WrapMode.Once;
            var nod = new AnimationCurve(
                new Keyframe(0f, 0f, 0f, 0f),
                new Keyframe(0.5f, 0.13f, 0f, 0f),   // ~15° pitch (0.13 rad → 7.5° in quat.x approx)
                new Keyframe(1f, -0.13f, 0f, 0f),
                new Keyframe(1.5f, 0f, 0f, 0f));
            clip.SetCurve("head", typeof(Transform), "localRotation.x", nod);
            return clip;
        }

        private static AnimationClip BuildWalkInPlace()
        {
            var clip = new AnimationClip { frameRate = 30f };
            clip.wrapMode = WrapMode.Loop;
            // Alternating leg lift: left hip pitches forward while right pitches back and vice
            // versa. 1.2 s two-step cycle.
            var leftLeg = new AnimationCurve(
                new Keyframe(0f, 0f), new Keyframe(0.3f, 0.3f),
                new Keyframe(0.6f, 0f), new Keyframe(0.9f, -0.3f),
                new Keyframe(1.2f, 0f));
            var rightLeg = new AnimationCurve(
                new Keyframe(0f, 0f), new Keyframe(0.3f, -0.3f),
                new Keyframe(0.6f, 0f), new Keyframe(0.9f, 0.3f),
                new Keyframe(1.2f, 0f));
            foreach (var l in new[] { "leftUpperLeg", "LeftUpperLeg", "LeftLeg" })
                clip.SetCurve(l, typeof(Transform), "localRotation.x", leftLeg);
            foreach (var r in new[] { "rightUpperLeg", "RightUpperLeg", "RightLeg" })
                clip.SetCurve(r, typeof(Transform), "localRotation.x", rightLeg);
            // Slight hips-Y bob synchronised with the step.
            var hipsBob = SineCurve(period: 0.6f, amplitude: 0.015f, offset: 0f, samples: 25);
            clip.SetCurve("hips", typeof(Transform), "localPosition.y", hipsBob);
            return clip;
        }

        // ── Small helpers ───────────────────────────────────────────────

        /// <summary>Build a sampled sine curve: <c>amplitude * sin((2π/period) * t + offset)</c>
        /// over <c>[0, period]</c>, with <paramref name="samples"/> uniformly-spaced keyframes.</summary>
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
    }
}
