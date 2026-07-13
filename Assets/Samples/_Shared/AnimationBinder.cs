using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;
using UnityGLTF.KhrCharacter;

namespace Samples.Shared
{
    /// <summary>
    /// The rig-mode dimension for <see cref="AnimationBinder"/>: describes HOW a clip is applied
    /// to a character. Kept as a plain enum (not a class hierarchy) so it maps 1:1 onto a UI
    /// dropdown; consumers who need custom binding logic call
    /// <see cref="AnimationBinder.Bind"/> directly with the appropriate mode.
    /// </summary>
    public enum RigMode
    {
        /// <summary>Assign the built humanoid <see cref="Avatar"/> (via
        /// <see cref="SkeletonMap.SwitchRigMode"/> with Humanoid) and drive an <see cref="Animator"/>
        /// with any humanoid clip — retargets automatically via Mecanim's muscle system.</summary>
        Humanoid,
        /// <summary>Drive an <see cref="Animator"/> without an Avatar — clips target transform
        /// paths directly by bone name. Works with any clip whose curve paths match the
        /// character's bone hierarchy.</summary>
        Generic,
        /// <summary>Drive the legacy <see cref="Animation"/> component. Simpler than
        /// <see cref="Generic"/> for one-clip playback and works with the clips embedded in a
        /// KHR-Character glb (which import as legacy <see cref="Animation"/> clips).</summary>
        Legacy,
    }

    /// <summary>
    /// Applies a <see cref="AnimationClip"/> to a KHR-Character-loaded scene via a chosen
    /// <see cref="RigMode"/>. Mode-agnostic: the demo controllers pick a mode + a clip and hand
    /// them to <see cref="Bind"/> + <see cref="Play"/>. The three tiers of the animation demo
    /// (HumanoidAnimation, AnimationRigging, AnimationSandbox) all sit on top of this binder;
    /// only the sandbox exposes the rig mode as a user-facing dropdown.
    ///
    /// Static, stateless — each call returns the created / discovered component; callers keep
    /// references and are responsible for teardown alongside the scene.
    /// </summary>
    public static class AnimationBinder
    {
        /// <summary>Prepare <paramref name="character"/> for playback under <paramref name="mode"/>
        /// and return an <see cref="Animator"/> (for Humanoid / Generic) or <c>null</c> (for
        /// Legacy — use <see cref="GetLegacyAnimation"/> instead). Idempotent: calling multiple
        /// times with the same mode reuses the existing component.
        ///
        /// Returns null when the requested mode isn't supported by the character (e.g. Humanoid
        /// on a character with no <see cref="SkeletonMap"/>).</summary>
        public static Animator Bind(GameObject character, RigMode mode)
        {
            if (character == null) return null;
            switch (mode)
            {
                case RigMode.Humanoid: return BindHumanoid(character);
                case RigMode.Generic:  return BindGeneric(character);
                case RigMode.Legacy:   return null; // caller uses GetLegacyAnimation
                default:               return null;
            }
        }

        /// <summary>True when <paramref name="mode"/> is supported by <paramref name="character"/>.
        /// Humanoid requires a working <see cref="SkeletonMap"/> with a buildable Avatar.
        /// Generic + Legacy are always available (they degrade gracefully — clips without matching
        /// bone paths simply do nothing).</summary>
        public static bool IsSupported(GameObject character, RigMode mode)
        {
            if (character == null) return false;
            switch (mode)
            {
                case RigMode.Humanoid:
                    var hub = character.GetComponent<KhrCharacter>();
                    var skeleton = hub != null ? hub.Skeleton : null;
                    return skeleton != null;
                case RigMode.Generic: return true;
                case RigMode.Legacy:  return true;
            }
            return false;
        }

        /// <summary>Play <paramref name="clip"/> on the animator. Uses the Playables API
        /// (<see cref="AnimationClipPlayable"/>) — runtime-friendly, no
        /// <c>RuntimeAnimatorController</c> asset needed to ship with the project. A tiny
        /// <see cref="AnimationBinderPlayback"/> component is attached to the animator's
        /// GameObject to own the <see cref="PlayableGraph"/> lifecycle so it disposes cleanly on
        /// destroy. Subsequent <see cref="Play"/> calls on the same animator swap the clip on the
        /// existing graph — no leaks, no per-call graph creation.</summary>
        public static void Play(Animator animator, AnimationClip clip)
        {
            if (animator == null || clip == null) return;
            var playback = animator.GetComponent<AnimationBinderPlayback>()
                ?? animator.gameObject.AddComponent<AnimationBinderPlayback>();
            playback.PlayClip(clip);
        }

        /// <summary>Legacy-mode playback: add or reuse an <see cref="Animation"/> component,
        /// add the clip if new, and play it.</summary>
        public static Animation PlayLegacy(GameObject character, AnimationClip clip)
        {
            if (character == null || clip == null) return null;
            if (!clip.legacy)
            {
                Debug.LogWarning($"[AnimationBinder] Clip '{clip.name}' is not marked legacy; " +
                                 "legacy Animation component may reject it. Set clip.legacy = true, " +
                                 "or use RigMode.Generic / RigMode.Humanoid instead.");
            }
            var animation = character.GetComponent<Animation>();
            if (animation == null) animation = character.AddComponent<Animation>();
            if (animation.GetClip(clip.name) == null) animation.AddClip(clip, clip.name);
            animation.clip = clip;
            animation.Play(clip.name);
            return animation;
        }

        /// <summary>Fetch or add a legacy <see cref="Animation"/> component on
        /// <paramref name="character"/>. Convenience for callers using
        /// <see cref="RigMode.Legacy"/> who want the reference without playing anything yet.</summary>
        public static Animation GetLegacyAnimation(GameObject character)
        {
            if (character == null) return null;
            return character.GetComponent<Animation>() ?? character.AddComponent<Animation>();
        }

        /// <summary>Enumerate the clips embedded in <paramref name="character"/> — imported from
        /// the source .glb's <c>animations[]</c>. Used by the AnimationSandbox scene to append a
        /// per-character source to <see cref="AnimationClipCatalog"/>.</summary>
        public static IEnumerable<AnimationClip> EnumerateCharacterClips(GameObject character)
        {
            if (character == null) yield break;
            var animation = character.GetComponent<Animation>();
            if (animation == null) yield break;
            foreach (AnimationState state in animation)
                if (state != null && state.clip != null) yield return state.clip;
        }

        // ── Mode-specific binding ─────────────────────────────────────────

        private static Animator BindHumanoid(GameObject character)
        {
            var hub = character.GetComponent<KhrCharacter>();
            var skeleton = hub != null ? hub.Skeleton : null;
            if (skeleton == null) return null;
            bool ok = skeleton.SwitchRigMode(RigImportMode.Humanoid);
            if (!ok) return null; // Humanoid rejected — plugin logs the reason.
            // Explicit `== null` check for consistency with BindGeneric — Unity's overloaded ==
            // handles the destroyed-object case correctly.
            var animator = character.GetComponent<Animator>();
            if (animator == null) animator = character.AddComponent<Animator>();
            return animator;
        }

        private static Animator BindGeneric(GameObject character)
        {
            // On KHR characters, delegate the animator + avatar wiring to
            // SkeletonMap.SwitchRigMode(Generic) — the plugin knows how to (a) not fight prior
            // Humanoid state (nulls the humanoid Avatar on the Animator without removing the
            // component itself), (b) destroy the leaked _lastBuiltAvatar. On non-KHR characters
            // (plain glbs), just add a bare Animator.
            //
            // Uses explicit `== null` checks (Unity's overloaded operator handles fake-null
            // from destroyed native objects correctly, whereas `??` uses C# reference nullity
            // and would silently pass a destroyed-but-referenced Animator through).
            var hub = character.GetComponent<KhrCharacter>();
            var skeleton = hub != null ? hub.Skeleton : null;
            if (skeleton != null) skeleton.SwitchRigMode(RigImportMode.Generic);

            var animator = character.GetComponent<Animator>();
            if (animator == null) animator = character.AddComponent<Animator>();
            return animator;
        }
    }
}
