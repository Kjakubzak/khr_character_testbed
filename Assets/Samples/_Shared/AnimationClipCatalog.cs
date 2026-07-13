using System.Collections.Generic;
using UnityEngine;

namespace Samples.Shared
{
    /// <summary>
    /// Registry of animation-clip SOURCES, structural sibling of
    /// <see cref="CharacterLoader.AssetSourceCatalog"/>. Each source is a labeled lazy iterator
    /// over <see cref="AnimationClip"/>s — the concrete backing can be procedural (see
    /// <see cref="HumanoidClipFactory"/>), <c>Resources.LoadAll&lt;AnimationClip&gt;</c>,
    /// character-embedded clips discovered at runtime, or user-registered at runtime via
    /// <see cref="TryRegister"/>. Consumers (the HumanoidAnimation / AnimationRigging /
    /// AnimationSandbox demo scenes, plus tests) enumerate the catalog to populate dropdowns
    /// or drive parameterized fixtures.
    ///
    /// Two defaults auto-register on first access:
    /// * <c>Procedural</c>    — <see cref="HumanoidClipFactory.All"/>.
    /// * <c>Resources</c>     — every <see cref="AnimationClip"/> discoverable via
    ///                          <c>Resources.LoadAll&lt;AnimationClip&gt;("")</c>. Empty when no
    ///                          Resources folder has clips; harmless.
    ///
    /// Character-embedded clips (clips inside a loaded KHR-Character glb) are per-character, so
    /// they aren't auto-registered as a global source. Callers wanting to enumerate them attach
    /// a per-scene source after loading via
    /// <c>AnimationClipCatalog.TryRegister("Character", () =&gt; EnumerateCharacterClips(root))</c>
    /// or use <see cref="AnimationBinder.EnumerateCharacterClips"/> directly.
    /// </summary>
    public static class AnimationClipCatalog
    {
        public sealed class ClipSource
        {
            public string Label;
            public System.Func<IEnumerable<AnimationClip>> Enumerate;
            public bool AutoDetected;
        }

        private static readonly object _sync = new object();
        private static List<ClipSource> _sources;

        /// <summary>All registered sources, lazy-init.</summary>
        public static IReadOnlyList<ClipSource> Sources
        {
            get { EnsureInitialized(); return _sources; }
        }

        /// <summary>Enumerate every clip across every source as
        /// <c>(source, clip)</c> pairs.</summary>
        public static IEnumerable<(ClipSource Source, AnimationClip Clip)> EnumerateAll()
        {
            EnsureInitialized();
            foreach (var s in _sources)
            {
                if (s.Enumerate == null) continue;
                IEnumerable<AnimationClip> clips;
                try { clips = s.Enumerate(); }
                catch (System.Exception e) { Debug.LogException(e); continue; }
                if (clips == null) continue;
                foreach (var c in clips)
                    if (c != null) yield return (s, c);
            }
        }

        /// <summary>Register a source. Returns false when the label is empty or already
        /// registered (case-insensitive on <see cref="ClipSource.Label"/>).</summary>
        public static bool TryRegister(string label, System.Func<IEnumerable<AnimationClip>> enumerate,
            bool autoDetected = false)
        {
            if (string.IsNullOrWhiteSpace(label) || enumerate == null) return false;
            EnsureInitialized();
            lock (_sync)
            {
                foreach (var existing in _sources)
                    if (string.Equals(existing.Label, label, System.StringComparison.OrdinalIgnoreCase))
                        return false;
                _sources.Add(new ClipSource
                {
                    Label = label.Trim(), Enumerate = enumerate, AutoDetected = autoDetected,
                });
            }
            return true;
        }

        /// <summary>Remove a source by label. Auto-detected defaults can be removed but will
        /// re-register on next <see cref="Invalidate"/> + access.</summary>
        public static bool Remove(string label)
        {
            EnsureInitialized();
            lock (_sync)
                return _sources.RemoveAll(s =>
                    string.Equals(s.Label, label, System.StringComparison.OrdinalIgnoreCase)) > 0;
        }

        /// <summary>Force reinit on next access. Useful in tests, or after adding a per-scene
        /// source that should reset between demos.</summary>
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
                _sources = new List<ClipSource>();
                RegisterDefaults();
            }
        }

        private static void RegisterDefaults()
        {
            _sources.Add(new ClipSource
            {
                Label = "Procedural",
                Enumerate = HumanoidClipFactory.All,
                AutoDetected = true,
            });
            _sources.Add(new ClipSource
            {
                Label = "Resources",
                Enumerate = () => Resources.LoadAll<AnimationClip>(string.Empty),
                AutoDetected = true,
            });
        }
    }
}
