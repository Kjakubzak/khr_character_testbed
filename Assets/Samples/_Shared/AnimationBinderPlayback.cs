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
    /// Public API: just <see cref="PlayClip"/>. The class is intentionally internal-shape (public
    /// only because Unity component discovery needs it) — direct callers use
    /// <see cref="AnimationBinder.Play"/>, which auto-attaches this and forwards.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class AnimationBinderPlayback : MonoBehaviour
    {
        private PlayableGraph _graph;
        private AnimationClipPlayable _clipPlayable;
        private AnimationPlayableOutput _output;

        /// <summary>Play <paramref name="clip"/> on this GameObject's <see cref="Animator"/>. Idempotent
        /// on the graph (created lazily) — swaps the current playable when called again.</summary>
        public void PlayClip(AnimationClip clip)
        {
            if (clip == null) return;
            var animator = GetComponent<Animator>();
            if (animator == null) return;

            if (!_graph.IsValid())
            {
                _graph = PlayableGraph.Create($"AnimationBinder_{gameObject.name}");
                _output = AnimationPlayableOutput.Create(_graph, "Animation", animator);
                _clipPlayable = AnimationClipPlayable.Create(_graph, clip);
                _output.SetSourcePlayable(_clipPlayable);
                _graph.Play();
                return;
            }

            // Graph already exists — destroy the current playable, create a new one for the new
            // clip, and rewire the output. Cheaper than tearing down and rebuilding the graph.
            if (_clipPlayable.IsValid()) _clipPlayable.Destroy();
            _clipPlayable = AnimationClipPlayable.Create(_graph, clip);
            _output.SetSourcePlayable(_clipPlayable);
        }

        private void OnDestroy()
        {
            if (_graph.IsValid()) _graph.Destroy();
        }
    }
}
