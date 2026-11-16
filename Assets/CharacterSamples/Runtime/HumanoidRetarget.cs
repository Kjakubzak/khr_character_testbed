using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using UnityEngine;
using UnityGLTF.KhrCharacter;

namespace KhrCharacterSamples
{
    /// <summary>A runtime-imported character with the Humanoid Avatar UnityGLTF builds from the KHR mapping.</summary>
    public sealed class LoadedCharacter : IDisposable
    {
        public GameObject Root;
        public KhrCharacter Hub;
        public Avatar Avatar;
        public HumanPoseHandler PoseHandler;
        public string SourcePath;
        /// <summary>The file's glTF JSON, for checks that compare what UnityGLTF imported with what the file says.</summary>
        public Newtonsoft.Json.Linq.JObject Gltf;

        public SkeletonMap Skeleton => Hub != null ? Hub.Skeleton : null;

        public static async Task<LoadedCharacter> LoadAsync(string path, byte[] bytes = null, bool expressionController = true)
        {
            var root = bytes != null
                ? await CharacterImport.LoadBytesAsync(bytes, expressionController)
                : await CharacterImport.LoadFileAsync(path, expressionController);
            if (root == null) return null;
            // TryGetComponent gives a real null; in the editor GetComponent returns a "fake null" object that
            // defeats ?? and ?. (UnityEngine.Object overloads == only).
            var loaded = new LoadedCharacter
            {
                Root = root, Hub = root.TryGetComponent<KhrCharacter>(out var hub) ? hub : null, SourcePath = path,
                Gltf = bytes == null ? CharacterImport.ReadGlbJson(path) : null,
            };
            loaded.BuildAvatar();
            return loaded;
        }

        /// <summary>
        /// Builds the Avatar through <see cref="SkeletonMap.BuildHumanoidAvatar"/> (the same call the editor import
        /// makes for the <c>KhrCharacterAvatar</c> sub-asset) and assigns it to an Animator that never runs on its own.
        /// </summary>
        public void BuildAvatar()
        {
            if (Skeleton == null) return;
            Avatar = Skeleton.BuildHumanoidAvatar();
            if (Avatar == null) return;
            if (!Root.TryGetComponent<Animator>(out var animator)) animator = Root.AddComponent<Animator>();
            animator.avatar = Avatar;
            animator.enabled = false;
            PoseHandler = new HumanPoseHandler(Avatar, Root.transform);
        }

        /// <summary>Local TRS of every transform, so a test can restore the rest pose after posing.</summary>
        public Dictionary<Transform, (Vector3, Quaternion, Vector3)> Snapshot()
        {
            var map = new Dictionary<Transform, (Vector3, Quaternion, Vector3)>();
            foreach (var t in Root.GetComponentsInChildren<Transform>(true))
                map[t] = (t.localPosition, t.localRotation, t.localScale);
            return map;
        }

        public static void Restore(Dictionary<Transform, (Vector3, Quaternion, Vector3)> snapshot)
        {
            foreach (var kv in snapshot)
                if (kv.Key != null)
                {
                    kv.Key.localPosition = kv.Value.Item1;
                    kv.Key.localRotation = kv.Value.Item2;
                    kv.Key.localScale = kv.Value.Item3;
                }
        }

        public bool TryGetRole(string role, out Transform bone)
        {
            bone = null;
            return Skeleton != null && Skeleton.TryGetBone(role, out bone) && bone != null;
        }

        public void Dispose()
        {
            PoseHandler?.Dispose();
            PoseHandler = null;
            if (Avatar != null) UnityEngine.Object.Destroy(Avatar);
            if (Root != null) UnityEngine.Object.Destroy(Root);
        }
    }

    /// <summary>
    /// A retarget source: a <c>&lt;clip&gt;_src.glb</c> (source skeleton + KHR skeleton mapping + t_pose reference
    /// pose + one take), imported through UnityGLTF so its Avatar comes from its own mapping and reference pose. The
    /// take is sampled into Unity muscle space and replayed on a target Avatar, which is Unity's Humanoid retarget.
    /// </summary>
    public sealed class RetargetSource : IDisposable
    {
        public ClipEntry Entry;
        public LoadedCharacter Character;
        public AnimationClip Take;
        public bool DesignationAdded;

        public float Length => Take != null ? Take.length : 0f;

        public static async Task<RetargetSource> LoadAsync(ClipEntry entry)
        {
            var bytes = File.ReadAllBytes(entry.SourcePath);
            bool add = entry.AddKhrCharacter;
            if (add)
            {
                // Fallback for older _src.glb files without the top-level designation (ledger D57); the clip
                // index flags them and the results record designation_added.
                Debug.LogWarning($"[KhrCharacterSamples] {entry.Name}: _src.glb lacks KHR_character; adding it in memory.");
                bytes = CharacterImport.PatchGlbJson(bytes, CharacterImport.AddCharacterDesignation);
            }
            var character = await LoadedCharacter.LoadAsync(entry.SourcePath, bytes, expressionController: false);
            if (character == null) return null;
            var animation = character.Root.GetComponent<Animation>();
            if (animation != null) animation.enabled = false;
            return new RetargetSource
            {
                Entry = entry,
                Character = character,
                Take = CharacterImport.FindClip(character.Root, entry.Animation),
                DesignationAdded = add,
            };
        }

        /// <summary>Samples the take at <paramref name="time"/> and returns the source's human pose.</summary>
        public HumanPose Sample(float time)
        {
            var pose = new HumanPose();
            Take.SampleAnimation(Character.Root, time);
            Character.PoseHandler.GetHumanPose(ref pose);
            return pose;
        }

        /// <summary>Plays the sampled pose on <paramref name="target"/> through its Avatar.</summary>
        public void ApplyTo(LoadedCharacter target, float time, bool keepHorizontalRoot = true)
        {
            var pose = Sample(time);
            if (!keepHorizontalRoot) pose.bodyPosition = new Vector3(0f, pose.bodyPosition.y, 0f);
            target.PoseHandler.SetHumanPose(ref pose);
        }

        /// <summary>
        /// Bakes the take into a humanoid (muscle-curve) AnimationClip at <paramref name="frameRate"/>, the form an
        /// imported humanoid clip has, so it can also be played through an Animator or the Recorder.
        /// </summary>
        public AnimationClip BakeHumanoidClip(float frameRate = 30f)
        {
            int frames = Mathf.Max(2, Mathf.CeilToInt(Length * frameRate) + 1);
            var muscles = new AnimationCurve[HumanTrait.MuscleCount];
            for (int i = 0; i < muscles.Length; i++) muscles[i] = new AnimationCurve();
            var rootT = new[] { new AnimationCurve(), new AnimationCurve(), new AnimationCurve() };
            var rootQ = new[] { new AnimationCurve(), new AnimationCurve(), new AnimationCurve(), new AnimationCurve() };
            for (int f = 0; f < frames; f++)
            {
                float t = Mathf.Min(f / frameRate, Length);
                var pose = Sample(t);
                for (int i = 0; i < muscles.Length; i++) muscles[i].AddKey(t, pose.muscles[i]);
                for (int k = 0; k < 3; k++) rootT[k].AddKey(t, pose.bodyPosition[k]);
                for (int k = 0; k < 4; k++) rootQ[k].AddKey(t, pose.bodyRotation[k]);
            }
            var clip = new AnimationClip { name = Entry.Name + "_humanoid", frameRate = frameRate };
            for (int i = 0; i < muscles.Length; i++)
                clip.SetCurve("", typeof(Animator), MuscleCurveName(HumanTrait.MuscleName[i]), muscles[i]);
            string[] axes = { "x", "y", "z", "w" };
            for (int k = 0; k < 3; k++) clip.SetCurve("", typeof(Animator), "RootT." + axes[k], rootT[k]);
            for (int k = 0; k < 4; k++) clip.SetCurve("", typeof(Animator), "RootQ." + axes[k], rootQ[k]);
            return clip;
        }

        /// <summary>
        /// Animator curve attribute for a muscle. Finger muscles are bound as "LeftHand.Thumb.1 Stretched", not by
        /// their HumanTrait display name "Left Thumb 1 Stretched".
        /// </summary>
        public static string MuscleCurveName(string muscle)
        {
            foreach (var side in new[] { "Left", "Right" })
                foreach (var finger in new[] { "Thumb", "Index", "Middle", "Ring", "Little" })
                {
                    var prefix = side + " " + finger + " ";
                    if (muscle.StartsWith(prefix, StringComparison.Ordinal))
                    {
                        var rest = muscle.Substring(prefix.Length);
                        var parts = rest.Split(' ');
                        return parts.Length == 2
                            ? $"{side}Hand.{finger}.{parts[0]} {parts[1]}"
                            : $"{side}Hand.{finger}.{rest}";
                    }
                }
            return muscle;
        }

        public void Dispose() => Character?.Dispose();
    }

    /// <summary>Geometry helpers shared by tests, capture and video.</summary>
    public static class CharacterGeometry
    {
        /// <summary>A Unity vector expressed in glTF axes (UnityGLTF negates X on import).</summary>
        public static Vector3 ToGltf(Vector3 v) => new Vector3(-v.x, v.y, v.z);

        /// <summary>Mirrors SkeletonMap's internal usability rule: valid and carrying resolved or authored values.</summary>
        public static bool IsUsable(ReferencePose pose) =>
            pose != null && pose.IsValid && (pose.HasResolvedValues || (pose.Bones != null && pose.Bones.Length > 0));

        public static readonly string[] FootRoles = { "leftFoot", "rightFoot", "leftToes", "rightToes" };

        public static Bounds WorldBounds(LoadedCharacter c)
        {
            bool any = false;
            var bounds = new Bounds(c.Root.transform.position, Vector3.zero);
            foreach (var r in c.Root.GetComponentsInChildren<Renderer>())
            {
                if (r is SkinnedMeshRenderer s) s.updateWhenOffscreen = true;
                if (!any) { bounds = r.bounds; any = true; } else bounds.Encapsulate(r.bounds);
            }
            if (c.Skeleton?.Result?.Bones != null)
                foreach (var kv in c.Skeleton.Result.Bones)
                    if (kv.Value != null)
                    {
                        if (!any) { bounds = new Bounds(kv.Value.position, Vector3.zero); any = true; }
                        else bounds.Encapsulate(kv.Value.position);
                    }
            if (!any)
                foreach (var t in c.Root.GetComponentsInChildren<Transform>())
                    bounds.Encapsulate(t.position);
            return bounds;
        }

        public static float MinFootHeight(LoadedCharacter c, out string lowestRole)
        {
            float min = float.PositiveInfinity;
            lowestRole = null;
            foreach (var role in FootRoles)
                if (c.TryGetRole(role, out var bone) && bone.position.y < min)
                {
                    min = bone.position.y;
                    lowestRole = role;
                }
            return min;
        }

        public static bool IsFinite(Quaternion q) =>
            !(float.IsNaN(q.x) || float.IsNaN(q.y) || float.IsNaN(q.z) || float.IsNaN(q.w) ||
              float.IsInfinity(q.x) || float.IsInfinity(q.y) || float.IsInfinity(q.z) || float.IsInfinity(q.w));

        public static bool IsFinite(Vector3 v) =>
            !(float.IsNaN(v.x) || float.IsNaN(v.y) || float.IsNaN(v.z) ||
              float.IsInfinity(v.x) || float.IsInfinity(v.y) || float.IsInfinity(v.z));
    }
}
