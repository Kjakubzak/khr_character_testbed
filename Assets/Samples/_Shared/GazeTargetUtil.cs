using UnityEngine;
using UnityGLTF.KhrCharacter;

namespace Samples.Shared
{
    /// <summary>
    /// Helpers for placing a demo gaze target relative to a loaded character. The samples frame the character to
    /// face the camera, so the target must be anchored to the character's head + forward (not a fixed world Z),
    /// otherwise it lands behind the character and the eyes look the wrong way.
    /// </summary>
    public static class GazeTargetUtil
    {
        /// <summary>Default distance, in metres, the gaze target sits in front of the head joint.</summary>
        public const float DefaultDistance = 2f;

        /// <summary>
        /// World position <paramref name="distance"/> m directly in front of the character's head joint, along the
        /// character's forward (+Z). Call AFTER framing so the character's forward is settled. Falls back to the
        /// root forward / position when no head can be resolved.
        /// </summary>
        public static Vector3 InFrontOfHead(KhrCharacter hub, float distance = DefaultDistance)
        {
            if (hub == null) return Vector3.forward * distance;
            return ResolveHeadPosition(hub) + hub.transform.forward * distance;
        }

        /// <summary>
        /// World position of the character's head: the mapped "head" joint when available, else the shared
        /// head-focus heuristic (named/animator head bone, eyes, or top-of-bounds), else the character root.
        /// </summary>
        public static Vector3 ResolveHeadPosition(KhrCharacter hub)
        {
            if (hub == null) return Vector3.zero;

            if (hub.Skeleton != null && hub.Skeleton.TryGetBone("head", out var head) && head != null)
                return head.position;

            if (SceneBoundsUtil.TryAggregate(hub.gameObject, out var bounds)
                && OrbitCameraRig.TryGetHeadFocus(hub.gameObject, bounds, out var center, out _))
                return center;

            return hub.transform.position;
        }
    }
}
