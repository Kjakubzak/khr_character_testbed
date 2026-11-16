using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityGLTF.KhrCharacter;

namespace KhrCharacterSamples
{
    /// <summary>
    /// Plays the video shot list for every character manifest in Play mode, one shot at a time, advancing by
    /// Time.deltaTime (the recorder fixes it at 1/fps). <see cref="ShotBegin"/> and <see cref="ShotEnd"/> let the
    /// editor-side CharacterVideo start and stop a Unity Recorder take per shot. Output paths are under
    /// KHR_VIDEO_DIR (default KHR_EVIDENCE_DIR/../video)/&lt;char&gt;/&lt;shot&gt;.mp4.
    /// </summary>
    public sealed class VideoDriver : MonoBehaviour
    {
        public static event Action<string> ShotBegin;   // absolute output path without extension
        public static event Action ShotEnd;
        public static bool Done { get; private set; }
        public static int Failures { get; private set; }
        public static readonly List<string> Messages = new List<string>();

        public static string VideoRoot
        {
            get
            {
                var dir = Environment.GetEnvironmentVariable("KHR_VIDEO_DIR");
                if (string.IsNullOrEmpty(dir)) dir = Path.Combine(Path.GetDirectoryName(Evidence.Root.TrimEnd('/')), "video");
                Directory.CreateDirectory(dir);
                return dir;
            }
        }

        private static float MaxShotSeconds =>
            float.TryParse(Environment.GetEnvironmentVariable("KHR_VIDEO_MAX_SECONDS"), out var s) && s > 0 ? s : 40f;

        private CaptureKit _kit;

        public static VideoDriver Launch()
        {
            Done = false;
            Failures = 0;
            Messages.Clear();
            var driver = new GameObject("KhrVideoDriver").AddComponent<VideoDriver>();
            driver.StartCoroutine(driver.RunAll());
            return driver;
        }

        private void Note(string message, bool failure = false)
        {
            if (failure) Failures++;
            Messages.Add(message);
            if (failure) Debug.LogError("[KhrVideo] " + message); else Debug.Log("[KhrVideo] " + message);
        }

        private IEnumerator RunAll()
        {
            yield return null;
            _kit = new CaptureKit(new Color(0.16f, 0.16f, 0.18f));
            foreach (var m in CharacterManifest.All())
            {
                var routine = Character(m);
                while (true)
                {
                    object current;
                    try
                    {
                        if (!routine.MoveNext()) break;
                        current = routine.Current;
                    }
                    catch (Exception e)
                    {
                        Note($"{m.Name}: {e}", true);
                        ShotEnd?.Invoke();
                        break;
                    }
                    yield return current;
                }
            }
            _kit.Dispose();
            Done = true;
        }

        // Never throws: a nested coroutine's exception would not reach the caller, and reading Task.Result on an
        // unfinished import would block the main thread the import needs. Callers check Completed() instead.
        private static IEnumerator Wait(Task task, float seconds)
        {
            float deadline = Time.realtimeSinceStartup + seconds;
            while (!task.IsCompleted && Time.realtimeSinceStartup < deadline) yield return null;
        }

        private static T Completed<T>(Task<T> task, string what) where T : class
        {
            if (!task.IsCompleted) throw new TimeoutException(what + " did not finish in time");
            return task.Result;
        }

        /// <summary>Runs <paramref name="perFrame"/>(t) for <paramref name="seconds"/> of recorded time.</summary>
        private IEnumerator Shot(string dir, string shot, float seconds, Action<float> perFrame, JArray list, JObject meta,
            bool capped = true)
        {
            if (capped) seconds = Mathf.Min(seconds, MaxShotSeconds);
            var path = Path.Combine(dir, Evidence.SafeName(shot));
            perFrame(0f);
            ShotBegin?.Invoke(path);
            for (float t = 0f; t <= seconds; t += Time.deltaTime)
            {
                perFrame(t);
                yield return null;
            }
            ShotEnd?.Invoke();
            var entry = (JObject)meta.DeepClone();
            entry["shot"] = shot;
            entry["file"] = Path.GetFileName(path) + ".mp4";
            entry["seconds"] = seconds;
            list.Add(entry);
            yield return null;
        }

        private IEnumerator Character(CharacterManifest m)
        {
            string dir = Path.Combine(VideoRoot, Evidence.SafeName(m.Name));
            Directory.CreateDirectory(dir);
            var meta = new JObject
            {
                ["character"] = m.Name, ["glb_sha256"] = Evidence.Sha256(m.GlbPath),
                ["unitygltf"] = Environment.GetEnvironmentVariable("KHR_UNITYGLTF_SHA") ?? "", ["unity"] = Application.unityVersion,
            };
            var shots = new JArray();
            var task = LoadedCharacter.LoadAsync(m.GlbPath);
            yield return Wait(task, 300f);
            var c = Completed(task, m.Name + " import");
            if (c == null) { Note($"{m.Name}: import failed", true); yield break; }
            try
            {
                var animation = c.Root.GetComponent<Animation>();
                if (animation != null) animation.enabled = false;
                var controller = c.Hub != null ? c.Hub.Expressions : null;
                if (controller != null) controller.enabled = false;
                if (c.Root.GetComponentsInChildren<Renderer>().Length == 0) _kit.AddStickFigure(c, new Color(1f, 0.6f, 0.15f));
                var rest = c.Snapshot();
                var restBounds = CharacterGeometry.WorldBounds(c);

                yield return Shot(dir, "turntable_rest", 8f, t => _kit.Frame(restBounds, -t * 45f), shots, meta);
                var pose = c.Skeleton?.ReferencePoses.FirstOrDefault(CharacterGeometry.IsUsable);
                if (pose != null)
                {
                    c.Skeleton.ApplyReferencePose(pose.AnimationIndex);
                    var poseBounds = CharacterGeometry.WorldBounds(c);
                    yield return Shot(dir, "turntable_" + pose.PoseType, 8f, t => _kit.Frame(poseBounds, -t * 45f), shots, meta);
                    LoadedCharacter.Restore(rest);
                }

                foreach (var a in ((JArray)m.Expect["animations"] ?? new JArray()).Where(x => (string)x["kind"] == "take"))
                {
                    var clip = CharacterImport.FindClip(c.Root, (string)a["name"]);
                    if (clip == null) continue;
                    yield return Shot(dir, "own_" + clip.name, clip.length, t =>
                    {
                        clip.SampleAnimation(c.Root, Mathf.Min(t, clip.length));
                        _kit.Frame(CharacterGeometry.WorldBounds(c), -20f);
                    }, shots, meta);
                    LoadedCharacter.Restore(rest);
                }

                RetargetSource idle = null;
                if (c.Avatar != null)
                    foreach (var entry in m.RetargetClips(videoOnly: true))
                    {
                        var load = RetargetSource.LoadAsync(entry);
                        yield return Wait(load, 120f);
                        var source = Completed(load, entry.Name + " import");
                        if (source?.Take == null) { Note($"{m.Name}: retarget {entry.Name} unusable", true); source?.Dispose(); continue; }
                        yield return Shot(dir, "retarget_" + entry.Name, source.Length, t =>
                        {
                            source.ApplyTo(c, t % Mathf.Max(source.Length, 1e-3f), keepHorizontalRoot: false);
                            _kit.Frame(restBounds, -30f);
                        }, shots, meta);
                        LoadedCharacter.Restore(rest);
                        if (idle == null) idle = source; else source.Dispose();
                    }

                var responses = c.Hub != null ? c.Hub.ExpressionResponses : null;
                if (controller != null && responses != null && responses.Count > 0)
                {
                    var names = responses.Entries.Select(e => e.Name).ToList();
                    float per = names.Count > 40 ? 0.5f : 1.2f;
                    yield return Shot(dir, "expression_sweep", names.Count * per, t =>
                    {
                        int i = Mathf.Min((int)(t / per), names.Count - 1);
                        float local = (t - i * per) / per;
                        controller.ResetAll();
                        controller.SetWeight(i, Mathf.Sin(Mathf.Clamp01(local) * Mathf.PI));
                        CaptureKit.StepExpressions(controller);
                        FrameFace(c);
                    }, shots, meta, capped: false);

                    // Mapping-set presets (e.g. vrm1_expressions) and the suppressions they reach.
                    foreach (var set in (m.Expect["expression_mapping_sets"] as JObject)?.Properties() ?? Enumerable.Empty<JProperty>())
                    {
                        var keys = m.Strings(set.Value).ToList();
                        if (keys.Count == 0 || !controller.SelectVocabularyInputSet(set.Name)) continue;
                        const float presetSeconds = 1.2f;
                        yield return Shot(dir, "presets_" + set.Name, keys.Count * presetSeconds, t =>
                        {
                            int i = Mathf.Min((int)(t / presetSeconds), keys.Count - 1);
                            controller.ResetAll();
                            controller.SetWeightByVocabulary(set.Name, keys[i], Mathf.Sin(Mathf.Clamp01((t - i * presetSeconds) / presetSeconds) * Mathf.PI));
                            CaptureKit.StepExpressions(controller);
                            FrameFace(c);
                        }, shots, meta, capped: false);
                        foreach (JObject check in (JArray)m.Expect["mapping_suppressions"] ?? new JArray())
                        {
                            if ((string)check["set"] != set.Name) continue;
                            string src = (string)check["source"], tgt = (string)check["target"];
                            yield return Shot(dir, $"suppression_{set.Name}_{src}_{tgt}", 4f, t =>
                            {
                                controller.ResetAll();
                                controller.SetWeightByVocabulary(set.Name, tgt, 1f);
                                controller.SetWeightByVocabulary(set.Name, src, t < 2f ? 0f : 1f);
                                CaptureKit.StepExpressions(controller);
                                FrameFace(c);
                            }, shots, meta);
                        }
                        controller.UseDirectNativeInputs();
                    }
                    foreach (JObject s in (JArray)m.Expect["suppressions"] ?? new JArray())
                    {
                        string src = (string)s["source"], tgt = (string)s["target"];
                        yield return Shot(dir, $"suppression_{src}_{tgt}", 4f, t =>
                        {
                            controller.ResetAll();
                            controller.SetWeight(tgt, 1f);
                            controller.SetWeight(src, t < 2f ? 0f : 1f);
                            CaptureKit.StepExpressions(controller);
                            FrameFace(c);
                        }, shots, meta);
                    }
                    controller.ResetAll();
                    CaptureKit.StepExpressions(controller);

                    var gaze = (JObject)m.Json["video"]?["gaze"];
                    var look = new[] { "lookLeft", "lookRight", "lookUp", "lookDown" }
                        .Select((d, k) => gaze?[d] != null ? (string)gaze[d] : d).ToArray();
                    var lookSet = (m.Expect["expression_mapping_sets"] as JObject)?.Properties()
                        .FirstOrDefault(p => look.All(k => m.Strings(p.Value).Contains(k)))?.Name;
                    if (!look.All(names.Contains) && lookSet != null && c.TryGetRole("head", out var mappedHead))
                    {
                        // GazeSolver writes native weights only; drive the mapped look keys with its 90° response instead.
                        controller.SelectVocabularyInputSet(lookSet);
                        var target = new GameObject("KhrGazeTarget").transform;
                        float reach = Mathf.Max(restBounds.size.y * 0.3f, 0.2f);
                        // glTF bone axes are arbitrary, so measure in the character root frame (+Z forward, +X = character right).
                        var forward = c.Root.transform.forward;
                        var origin = mappedHead.position;
                        yield return Shot(dir, "lookat", 8f, t =>
                        {
                            float w = t / 8f * Mathf.PI * 2f;
                            target.position = origin + forward * reach * 1.5f
                                + new Vector3(Mathf.Sin(w) * reach, Mathf.Sin(2f * w) * reach * 0.5f, 0f);
                            var local = c.Root.transform.InverseTransformDirection((target.position - mappedHead.position).normalized);
                            float yaw = Mathf.Atan2(local.x, local.z), pitch = Mathf.Atan2(local.y, Mathf.Sqrt(local.x * local.x + local.z * local.z));
                            const float half = Mathf.PI * 0.5f;
                            controller.ResetAll();
                            controller.SetWeightByVocabulary(lookSet, look[1], Mathf.Clamp01(Mathf.Max(yaw, 0f) / half));
                            controller.SetWeightByVocabulary(lookSet, look[0], Mathf.Clamp01(Mathf.Max(-yaw, 0f) / half));
                            controller.SetWeightByVocabulary(lookSet, look[2], Mathf.Clamp01(Mathf.Max(pitch, 0f) / half));
                            controller.SetWeightByVocabulary(lookSet, look[3], Mathf.Clamp01(Mathf.Max(-pitch, 0f) / half));
                            CaptureKit.StepExpressions(controller);
                            FrameFace(c);
                        }, shots, meta);
                        Destroy(target.gameObject);
                        controller.UseDirectNativeInputs();
                        controller.ResetAll();
                        CaptureKit.StepExpressions(controller);
                    }
                    else if (look.All(names.Contains) && c.TryGetRole("head", out var head))
                    {
                        var solver = c.Root.AddComponent<GazeSolver>();
                        solver.LookLeft = look[0]; solver.LookRight = look[1]; solver.LookUp = look[2]; solver.LookDown = look[3];
                        solver.Bind(controller, c.Skeleton);
                        var target = new GameObject("KhrGazeTarget").transform;
                        solver.Target = target;
                        solver.Mode = GazeSolver.LookAtMode.CustomTarget;
                        float reach = Mathf.Max(restBounds.size.y * 0.3f, 0.2f);
                        yield return Shot(dir, "lookat", 8f, t =>
                        {
                            float w = t / 8f * Mathf.PI * 2f;
                            target.position = head.position + head.forward * reach * 1.5f
                                + new Vector3(Mathf.Sin(w) * reach, Mathf.Sin(2f * w) * reach * 0.5f, 0f);
                            controller.ResetAll();
                            typeof(GazeSolver).GetMethod("LateUpdate", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
                                ?.Invoke(solver, null);
                            CaptureKit.StepExpressions(controller);
                            FrameFace(c);
                        }, shots, meta);
                        Destroy(solver);
                        Destroy(target.gameObject);
                        controller.ResetAll();
                        CaptureKit.StepExpressions(controller);
                    }
                }

                var hints = c.Hub != null && c.Hub.CameraHints != null ? c.Hub.CameraHints.Hints : new List<CameraHint>();
                foreach (var hint in hints)
                {
                    yield return Shot(dir, "hint_" + hint.Role + "_" + hint.NodeIndex, 3f, t =>
                    {
                        if (idle != null) idle.ApplyTo(c, t % Mathf.Max(idle.Length, 1e-3f), keepHorizontalRoot: false);
                        _kit.ApplyHint(c, hint);
                    }, shots, meta);
                    _kit.Camera.ResetWorldToCameraMatrix();
                    LoadedCharacter.Restore(rest);
                }

                var view = c.Hub != null ? c.Hub.View : null;
                if (view != null)
                    yield return Shot(dir, "visibility_first_third", 6f, t =>
                    {
                        view.Mode = ((int)(t / 2f)) % 2 == 0 ? ViewModeController.ViewMode.ThirdPerson : ViewModeController.ViewMode.FirstPerson;
                        _kit.Frame(restBounds, -20f);
                    }, shots, meta);
                idle?.Dispose();
            }
            finally
            {
                _kit.ClearOverlay();
                Evidence.WriteJson(Path.Combine(dir, "shots.json"), new JObject { ["meta"] = meta, ["shots"] = shots });
                c.Dispose();
            }
        }

        private void FrameFace(LoadedCharacter c)
        {
            var hints = c.Hub != null ? c.Hub.CameraHints : null;
            _kit.Camera.ResetWorldToCameraMatrix();
            if (hints != null && hints.TryGetByRole(CameraHintRoles.Portrait, out var portrait))
            {
                _kit.ApplyHint(c, portrait);
                return;
            }
            var bounds = CharacterGeometry.WorldBounds(c);
            if (c.TryGetRole("head", out var head)) _kit.FrameJoint(head, Mathf.Max(bounds.size.y * 0.09f, 0.05f));
            else _kit.Frame(bounds, 0f);
        }
    }
}
