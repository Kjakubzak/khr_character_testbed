using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;

namespace Samples.Shared
{
    /// <summary>
    /// Playback lifecycle owner attached by <see cref="AnimationBinder.Play"/>. Holds one
    /// <see cref="PlayableGraph"/> per animator and one active <see cref="AnimationClipPlayable"/>;
    /// swapping clips destroys the old playable and creates a new one so we never leak playables.
    /// Disposes the whole graph on destroy so nothing survives the character being unloaded.
    ///
    /// Bind-pose snapshotting: on first PlayClip we capture every child Transform's local TRS so
    /// subsequent PlayClip calls can restore the character to its bind pose before starting the
    /// new clip. Without this, a Once-mode clip that raised the arms (e.g. Wave) would leave the
    /// arms raised when the next clip only touches the head (e.g. Nod) — because the new
    /// playable never writes to those bones, the old values persist. The bind-pose restore turns
    /// clip swaps into "start fresh from bind pose" behaviour that matches user expectation.
    ///
    /// Public API: just <see cref="PlayClip"/>. Direct callers use
    /// <see cref="AnimationBinder.Play"/>, which auto-attaches this and forwards.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class AnimationBinderPlayback : MonoBehaviour
    {
        private PlayableGraph _graph;
        private AnimationClipPlayable _clipPlayable;
        private AnimationPlayableOutput _output;

        // Bind pose captured on first PlayClip. Struct entries so we don't heap-allocate per bone.
        private readonly List<PoseEntry> _bindPose = new List<PoseEntry>();

        private struct PoseEntry
        {
            public Transform T;
            public Vector3 LocalPosition;
            public Quaternion LocalRotation;
            public Vector3 LocalScale;
        }

        /// <summary>Play <paramref name="clip"/> on this GameObject's <see cref="Animator"/>. On
        /// first call, captures the character's bind pose. On subsequent calls, restores the
        /// bind pose before swapping playables so residual state from the prior clip (e.g. a
        /// Wave that left the arms raised) doesn't bleed into the new clip.</summary>
        public void PlayClip(AnimationClip clip)
        {
            if (clip == null) return;
            var animator = GetComponent<Animator>();
            if (animator == null) return;

            if (!_graph.IsValid())
            {
                // First play — capture bind pose from current state (nothing has animated yet).
                CaptureBindPose(transform);

                _graph = PlayableGraph.Create($"AnimationBinder_{gameObject.name}");
                _output = AnimationPlayableOutput.Create(_graph, "Animation", animator);
                _clipPlayable = AnimationClipPlayable.Create(_graph, clip);
                _output.SetSourcePlayable(_clipPlayable);
                _graph.Play();
                return;
            }

            // Subsequent play — restore bind pose so the new clip's curves start from a clean
            // slate. Any bones the new clip doesn't touch stay in bind pose (rather than in
            // whatever state the previous clip left them in).
            RestoreBindPose();

            if (_clipPlayable.IsValid()) _clipPlayable.Destroy();
            _clipPlayable = AnimationClipPlayable.Create(_graph, clip);
            _output.SetSourcePlayable(_clipPlayable);
        }

        private void CaptureBindPose(Transform root)
        {
            _bindPose.Clear();
            foreach (var t in root.GetComponentsInChildren<Transform>(true))
            {
                _bindPose.Add(new PoseEntry
                {
                    T = t,
                    LocalPosition = t.localPosition,
                    LocalRotation = t.localRotation,
                    LocalScale = t.localScale,
                });
            }
        }

        private void RestoreBindPose()
        {
            for (int i = 0; i < _bindPose.Count; i++)
            {
                var entry = _bindPose[i];
                if (entry.T == null) continue;
                entry.T.localPosition = entry.LocalPosition;
                entry.T.localRotation = entry.LocalRotation;
                entry.T.localScale = entry.LocalScale;
            }
        }

        private void OnDestroy()
        {
            if (_graph.IsValid()) _graph.Destroy();
        }
    }
}
