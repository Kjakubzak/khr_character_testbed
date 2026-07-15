using UnityEngine;

namespace Samples.Shared
{
    /// <summary>
    /// Attached to a loaded character root by <see cref="HumanoidClipFactory"/> when it caches character-adaptive
    /// clips. On teardown it destroys those clips so the <see cref="HideFlags.HideAndDontSave"/> AnimationClips
    /// (which survive scene unload and <c>Resources.UnloadUnusedAssets</c>) don't leak for the play session.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class HumanoidClipCacheCleaner : MonoBehaviour
    {
        /// <summary>Attach a cleaner to <paramref name="character"/> if it doesn't already have one (idempotent).</summary>
        public static void Ensure(GameObject character)
        {
            if (character == null) return;
            if (character.GetComponent<HumanoidClipCacheCleaner>() == null)
                character.AddComponent<HumanoidClipCacheCleaner>();
        }

        private void OnDestroy()
        {
            HumanoidClipFactory.ReleaseForCharacter(gameObject.GetInstanceID());
        }
    }
}
