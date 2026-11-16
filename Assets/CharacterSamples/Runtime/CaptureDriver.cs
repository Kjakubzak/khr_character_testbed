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
    /// Play-mode still capture for every character manifest: rest and reference-pose stills, the character's own takes
    /// and the Humanoid-retargeted clips at fixed times from several views, one frame per KHR camera hint, and an
    /// expression sweep grid. Output: KHR_EVIDENCE_DIR/captures/&lt;char&gt;/. Started by CharacterCapture (editor).
    /// </summary>
    public sealed class CaptureDriver : MonoBehaviour
    {
        public static bool Done { get; private set; }
        public static int Failures { get; private set; }
        public static readonly List<string> Messages = new List<string>();

        private CaptureKit _kit;

        public static CaptureDriver Launch()
        {
            Done = false;
            Failures = 0;
            Messages.Clear();
            var driver = new GameObject("KhrCaptureDriver").AddComponent<CaptureDriver>();
            driver.StartCoroutine(driver.RunAll());
            return driver;
        }

        private void Note(string message, bool failure = false)
        {
            if (failure) Failures++;
            Messages.Add(message);
            if (failure) Debug.LogError("[KhrCapture] " + message); else Debug.Log("[KhrCapture] " + message);
        }

        private IEnumerator RunAll()
        {
            _kit = new CaptureKit(new Color(0.18f, 0.18f, 0.2f));
            var manifests = CharacterManifest.All();
            if (manifests.Count == 0) Note("no character manifests under " + CharacterManifest.RootDirectory, true);
            foreach (var m in manifests)
            {
                var routine = CaptureCharacter(m);
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
                        break;
                    }
                    yield return current;
                }
            }
            _kit.Dispose();
            Evidence.WriteJson(Path.Combine(Evidence.Dir("captures"), "capture_log.json"),
                new JObject { ["utc"] = DateTime.UtcNow.ToString("o"), ["failures"] = Failures, ["messages"] = new JArray(Messages) });
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

        private IEnumerator CaptureCharacter(CharacterManifest m)
        {
            string outDir = Evidence.Dir("captures", Evidence.SafeName(m.Name));
            var index = new JObject
            {
                ["character"] = m.Name, ["glb_sha256"] = Evidence.Sha256(m.GlbPath), ["unity"] = Application.unityVersion,
                ["utc"] = DateTime.UtcNow.ToString("o"), ["files"] = new JArray(),
            };
            var files = (JArray)index["files"];
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

                var views = m.Strings(m.Capture["views"]).DefaultIfEmpty("front").ToList();
                var fractions = ((JArray)m.Capture["fractions"] ?? new JArray(0f, 0.25f, 0.5f, 0.75f)).Select(t => (float)t).ToList();
                int res = (int?)m.Capture["resolution"] ?? 1024;
                var rest = c.Snapshot();

                void Shot(string relative, int size = 0)
                {
                    var path = Path.Combine(outDir, relative);
                    _kit.RenderPng(path, size > 0 ? size : res, size > 0 ? size : res);
                    files.Add(relative);
                }

                void Views(string prefix)
                {
                    var bounds = CharacterGeometry.WorldBounds(c);
                    foreach (var view in views)
                    {
                        _kit.Frame(bounds, CaptureKit.ViewYaw(view));
                        Shot($"{prefix}_{view}.png");
                    }
                }

                Views("rest/rest");
                if (c.Skeleton != null)
                    foreach (var pose in c.Skeleton.ReferencePoses.Where(CharacterGeometry.IsUsable))
                    {
                        c.Skeleton.ApplyReferencePose(pose.AnimationIndex);
                        Views($"refpose/{Evidence.SafeName(pose.PoseType)}");
                        LoadedCharacter.Restore(rest);
                    }

                var takes = ((JArray)m.Expect["animations"] ?? new JArray()).Where(a => (string)a["kind"] == "take").Select(a => (string)a["name"]);
                foreach (var takeName in takes)
                {
                    var clip = CharacterImport.FindClip(c.Root, takeName);
                    if (clip == null) { Note($"{m.Name}: take {takeName} missing", true); continue; }
                    foreach (var f in fractions)
                    {
                        float t = f * clip.length;
                        clip.SampleAnimation(c.Root, t);
                        Views($"own/{Evidence.SafeName(takeName)}/t{t:000.000}");
                    }
                    LoadedCharacter.Restore(rest);
                }
                yield return null;

                if (c.Avatar != null)
                    foreach (var entry in m.RetargetClips())
                    {
                        var load = RetargetSource.LoadAsync(entry);
                        yield return Wait(load, 120f);
                        using (var source = Completed(load, entry.Name + " import"))
                        {
                            if (source?.Take == null || source.Character.Avatar == null)
                            {
                                Note($"{m.Name}: retarget source {entry.Name} unusable", true);
                                continue;
                            }
                            foreach (var f in fractions)
                            {
                                float t = f * source.Length;
                                source.ApplyTo(c, t, keepHorizontalRoot: false);
                                Views($"retarget/{Evidence.SafeName(entry.Name)}/t{t:000.000}");
                            }
                        }
                        LoadedCharacter.Restore(rest);
                        yield return null;
                    }

                var hintDiag = new JArray();
                index["views"] = hintDiag;
                var hints = c.Hub != null && c.Hub.CameraHints != null ? c.Hub.CameraHints.Hints : new List<CameraHint>();
                int hintRes = (int?)m.Capture["hint_resolution"] ?? res;
                for (int i = 0; i < hints.Count; i++)
                {
                    _kit.Frame(CharacterGeometry.WorldBounds(c), 0f);
                    var projectionSource = _kit.ApplyHint(c, hints[i]);
                    var relative = $"hints/{i:00}_{Evidence.SafeName(hints[i].Role)}.png";
                    var described = DescribeView(c, relative);
                    described["projection_source"] = projectionSource;
                    hintDiag.Add(described);
                    _kit.RenderPng(Path.Combine(outDir, relative), hintRes, hintRes);
                    files.Add(relative);
                    _kit.Camera.ResetWorldToCameraMatrix();
                }

                var responses = c.Hub != null ? c.Hub.ExpressionResponses : null;
                if (controller != null && responses != null && responses.Count > 0)
                {
                    FrameFace(c);
                    hintDiag.Add(DescribeView(c, "expressions (face framing)"));
                    var tiles = new List<string>();
                    controller.ResetAll();
                    CaptureKit.StepExpressions(controller);
                    var neutral = "expressions/000_neutral.png";
                    int exprRes = (int?)m.Capture["expression_resolution"] ?? 512;
                    Shot(neutral, exprRes);
                    tiles.Add(Path.Combine(outDir, neutral));
                    var names = responses.Entries.Select(e => e.Name).ToList();
                    for (int i = 0; i < names.Count; i++)
                    {
                        controller.ResetAll();
                        controller.SetWeight(i, 1f);
                        CaptureKit.StepExpressions(controller);
                        var relative = $"expressions/{i + 1:000}_{Evidence.SafeName(names[i])}.png";
                        Shot(relative, exprRes);
                        tiles.Add(Path.Combine(outDir, relative));
                    }
                    foreach (JObject s in (JArray)m.Expect["suppressions"] ?? new JArray())
                    {
                        controller.ResetAll();
                        controller.SetWeight((string)s["source"], 1f);
                        controller.SetWeight((string)s["target"], 1f);
                        CaptureKit.StepExpressions(controller);
                        Shot($"expressions/suppress_{Evidence.SafeName((string)s["source"])}_{Evidence.SafeName((string)s["target"])}.png", exprRes);
                    }
                    controller.ResetAll();
                    CaptureKit.StepExpressions(controller);
                    // Mapping-set presets (e.g. vrm1_expressions) and their suppression pairs, through the controller's
                    // selected input mapping.
                    var mappingTiles = new List<string>();
                    foreach (var set in (m.Expect["expression_mapping_sets"] as JObject)?.Properties() ?? Enumerable.Empty<JProperty>())
                    {
                        if (!controller.SelectVocabularyInputSet(set.Name)) { Note($"{m.Name}: mapping set {set.Name} not selectable", true); continue; }
                        foreach (var key in m.Strings(set.Value))
                        {
                            controller.ResetAll();
                            controller.SetWeightByVocabulary(set.Name, key, 1f);
                            CaptureKit.StepExpressions(controller);
                            var relative = $"expressions/{Evidence.SafeName(set.Name)}/{Evidence.SafeName(key)}.png";
                            Shot(relative, exprRes);
                            mappingTiles.Add(Path.Combine(outDir, relative));
                        }
                        foreach (JObject check in (JArray)m.Expect["mapping_suppressions"] ?? new JArray())
                        {
                            if ((string)check["set"] != set.Name) continue;
                            controller.ResetAll();
                            controller.SetWeightByVocabulary(set.Name, (string)check["source"], 1f);
                            controller.SetWeightByVocabulary(set.Name, (string)check["target"], 1f);
                            CaptureKit.StepExpressions(controller);
                            Shot($"expressions/{Evidence.SafeName(set.Name)}/suppress_{Evidence.SafeName((string)check["source"])}_{Evidence.SafeName((string)check["target"])}.png", exprRes);
                        }
                        controller.UseDirectNativeInputs();
                        CaptureKit.WriteGrid(mappingTiles, 6, Path.Combine(outDir, $"expressions/{Evidence.SafeName(set.Name)}/grid.png"));
                        files.Add($"expressions/{Evidence.SafeName(set.Name)}/grid.png");
                        mappingTiles.Clear();
                    }
                    controller.ResetAll();
                    CaptureKit.StepExpressions(controller);

                    // Native grids in pages of 64 tiles, so no grid exceeds the maximum texture size.
                    int columns = (int?)m.Capture["grid_columns"] ?? 8;
                    for (int page = 0; page * 64 < tiles.Count; page++)
                    {
                        var relative = $"expressions/grid_{page:00}.png";
                        CaptureKit.WriteGrid(tiles.Skip(page * 64).Take(64).ToList(), columns, Path.Combine(outDir, relative));
                        files.Add(relative);
                    }
                }
                Note($"{m.Name}: {files.Count} images");
            }
            finally
            {
                _kit.ClearOverlay();
                Evidence.WriteJson(Path.Combine(outDir, "index.json"), index);
                c.Dispose();
            }
        }

        /// <summary>Portrait hint if the character has one, else the mapped head at close range, else full body.</summary>
        /// <summary>Camera placement and which renderers its frustum contains, for diagnosing empty stills.</summary>
        private JObject DescribeView(LoadedCharacter c, string what)
        {
            var cam = _kit.Camera;
            Vector3 position = cam.cameraToWorldMatrix.GetColumn(3), forward = -cam.cameraToWorldMatrix.GetColumn(2);
            var planes = GeometryUtility.CalculateFrustumPlanes(cam.projectionMatrix * cam.worldToCameraMatrix);
            var renderers = c.Root.GetComponentsInChildren<Renderer>();
            int inside = renderers.Count(r => GeometryUtility.TestPlanesAABB(planes, r.bounds));
            var bounds = CharacterGeometry.WorldBounds(c);
            var o = new JObject
            {
                ["what"] = what, ["position"] = new JArray(position.x, position.y, position.z),
                ["forward"] = new JArray(forward.x, forward.y, forward.z), ["near"] = cam.nearClipPlane, ["far"] = cam.farClipPlane,
                ["fov"] = cam.fieldOfView, ["orthographic"] = cam.orthographic,
                ["renderers_in_frustum"] = inside, ["renderers"] = renderers.Length,
                ["character_center"] = new JArray(bounds.center.x, bounds.center.y, bounds.center.z),
                ["character_size"] = new JArray(bounds.size.x, bounds.size.y, bounds.size.z),
            };
            if (c.TryGetRole("head", out var head))
            {
                var v = (cam.projectionMatrix * cam.worldToCameraMatrix).MultiplyPoint(head.position);
                o["head_world"] = new JArray(head.position.x, head.position.y, head.position.z);
                o["head_ndc"] = new JArray(v.x, v.y, v.z);
            }
            return o;
        }

        private void FrameFace(LoadedCharacter c)
        {
            var hints = c.Hub != null ? c.Hub.CameraHints : null;
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
