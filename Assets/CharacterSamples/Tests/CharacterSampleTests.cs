using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityGLTF.KhrCharacter;

namespace KhrCharacterSamples.Tests
{
    /// <summary>
    /// End-to-end checks of each character under Assets/SampleAssets/Characters/&lt;char&gt;/character.json through
    /// UnityGLTF's KHR_character consumer. Every name and expected value comes from the manifests; results also go to
    /// JSON under KHR_EVIDENCE_DIR/tests. Run in PlayMode so the ExpressionController's LateUpdate writes the scene.
    /// </summary>
    [Category("Characters")]
    public class CharacterSampleTests
    {
        private static readonly Dictionary<string, CharacterManifest> Manifests = new Dictionary<string, CharacterManifest>();
        private static readonly Dictionary<string, LoadedCharacter> Loaded = new Dictionary<string, LoadedCharacter>();
        private static readonly Dictionary<string, JObject> Reports = new Dictionary<string, JObject>();
        private static readonly Dictionary<string, string> LoadErrors = new Dictionary<string, string>();

        public static IEnumerable<string> CharacterNames() => CharacterManifest.All().Select(m => m.Name);

        /// <summary>"character|clip" pairs; a plain string keeps NUnit case names readable and filterable.</summary>
        public static IEnumerable<string> RetargetCases()
        {
            foreach (var m in CharacterManifest.All())
            {
                if (!((bool?)m.Expect["humanoid"] ?? false)) continue;
                IReadOnlyList<ClipEntry> clips;
                string error = null;
                try { clips = m.RetargetClips(); }
                catch (Exception e) { clips = Array.Empty<ClipEntry>(); error = e.Message; }
                // A broken clip index (e.g. KHR_CLIPS_DIR unset) becomes one failing case instead of a discovery error.
                if (error != null) yield return m.Name + "|!" + error.Replace('|', '/');
                foreach (var clip in clips) yield return m.Name + "|" + clip.Name;
            }
        }

        [TearDown]
        public void RecordOutcome()
        {
            // Unity's NUnit has no Test.Arguments; the single string argument is quoted in the case name.
            var context = TestContext.CurrentContext;
            string caseName = context.Test.Name ?? "";
            int open = caseName.IndexOf('"'), close = caseName.LastIndexOf('"');
            if (open < 0 || close <= open) return;
            var parts = caseName.Substring(open + 1, close - open - 1).Split('|');
            string check = context.Test.MethodName + (parts.Length > 1 ? "/" + parts[1] : "");
            Record(parts[0], check, context.Result.Outcome.Status.ToString(), context.Result.Message);
        }

        [OneTimeTearDown]
        public void TearDownAll()
        {
            foreach (var c in Loaded.Values) c?.Dispose();
            Loaded.Clear();
            WriteSummary();
        }

        // ── import ────────────────────────────────────────────────────────────

        [UnityTest]
        public IEnumerator Import([ValueSource(nameof(CharacterNames))] string name)
        {
            yield return Load(name);
            var m = Manifests[name];
            var c = Require(name);
            var section = Section(name, "import");
            Assert.IsNotNull(c, $"{name}: UnityGLTF returned no scene");
            int renderers = c.Root.GetComponentsInChildren<Renderer>(true).Length;
            int skinned = c.Root.GetComponentsInChildren<SkinnedMeshRenderer>(true).Length;
            int blendShapes = c.Root.GetComponentsInChildren<SkinnedMeshRenderer>(true)
                .Sum(r => r.sharedMesh != null ? r.sharedMesh.blendShapeCount : 0);
            section["renderers"] = renderers;
            section["skinned_renderers"] = skinned;
            section["blend_shapes"] = blendShapes;
            section["transforms"] = c.Root.GetComponentsInChildren<Transform>(true).Length;
            section["clips"] = new JArray(CharacterImport.Clips(c.Root).Select(k => k.name));
            section["khr_hub"] = c.Hub != null;
            section["capabilities"] = c.Hub != null ? new JArray(c.Hub.Capabilities.Select(x => x.ToString())) : new JArray();
            if ((int?)m.Expect["mesh_count"] > 0) Assert.Greater(renderers, 0, $"{name}: meshes declared but no renderers");
            if ((bool?)m.Expect["skinned"] ?? false) Assert.Greater(skinned, 0, $"{name}: skins declared but no SkinnedMeshRenderer");
            if (m.IsKhrCharacter)
            {
                Assert.IsNotNull(c.Hub, $"{name}: KHR_character present but no KhrCharacter hub");
                Assert.IsTrue(c.Hub.IsReady, $"{name}: hub not ready after import");
            }
            else
            {
                Assert.IsNull(c.Hub, $"{name}: no KHR_character in the file, yet a hub was attached");
            }
#if UNITY_EDITOR
            var assetPath = ProjectRelative(m.GlbPath);
            var main = UnityEditor.AssetDatabase.LoadMainAssetAtPath(assetPath) as GameObject;
            section["editor_import"] = main != null;
            Assert.IsNotNull(main, $"{name}: editor import of {assetPath} produced no prefab");
#endif
            Pass(name, "import");
        }

        // ── humanoid ──────────────────────────────────────────────────────────

        [UnityTest]
        public IEnumerator HumanoidAvatar([ValueSource(nameof(CharacterNames))] string name)
        {
            yield return Load(name);
            var m = Manifests[name];
            var c = Require(name);
            RequireDeclared(m, "humanoid");
            var section = Section(name, "humanoid");
            Assert.IsNotNull(c.Skeleton, $"{name}: no SkeletonMap");
            Assert.IsNotNull(c.Avatar, $"{name}: SkeletonMap.BuildHumanoidAvatar returned null (generic fallback)");
            section["runtime_avatar_valid"] = c.Avatar.isValid;
            section["runtime_avatar_human"] = c.Avatar.isHuman;
            Assert.IsTrue(c.Avatar.isValid && c.Avatar.isHuman, $"{name}: runtime Avatar invalid or not human");
#if UNITY_EDITOR
            var avatar = UnityEditor.AssetDatabase.LoadAllAssetsAtPath(ProjectRelative(m.GlbPath))
                .OfType<Avatar>().FirstOrDefault(a => a.name == "KhrCharacterAvatar");
            section["editor_avatar"] = avatar != null;
            section["editor_avatar_valid"] = avatar != null && avatar.isValid && avatar.isHuman;
            Assert.IsNotNull(avatar, $"{name}: editor import did not persist the KhrCharacterAvatar sub-asset");
            Assert.IsTrue(avatar.isValid && avatar.isHuman, $"{name}: editor KhrCharacterAvatar invalid or not human");
#endif
            Pass(name, "humanoid");
        }

        [UnityTest]
        public IEnumerator MappedRoles([ValueSource(nameof(CharacterNames))] string name)
        {
            yield return Load(name);
            var m = Manifests[name];
            var c = Require(name);
            RequireDeclared(m, "mapped_roles");
            var section = Section(name, "roles");
            var result = c.Skeleton.Result;
            string expectedSet = (string)m.Expect["mapping_set"];
            section["mapping_sets"] = new JArray(result.MappingSets.Select(s => s.Identifier));
            section["selected"] = result.SelectedRig;
            Assert.IsTrue(result.MappingSets.Any(s => s.Identifier == expectedSet), $"{name}: mapping set '{expectedSet}' not exposed");
            var set = result.MappingSets.First(s => s.Identifier == expectedSet);
            var expected = m.Strings(m.Expect["mapped_roles"]).ToList();
            var missing = expected.Where(r => !set.Associations.ContainsKey(r) || set.Associations[r] == null).ToList();
            var humanoidRoles = set.Associations.Keys.Where(r => KhrCharacterSkeletonBakerProxy.IsHumanoidRole(r)).ToList();
            section["associations"] = set.Associations.Count;
            section["unity_humanoid_roles"] = new JArray(humanoidRoles.OrderBy(r => r));
            section["missing"] = new JArray(missing);
            section["missing_required"] = new JArray(result.Report?.MissingRequiredBones ?? new List<string>());
            int min = (int?)m.Expect["min_mapped_roles"] ?? 15;
            Assert.IsEmpty(missing, $"{name}: roles declared in the file did not resolve: {string.Join(", ", missing)}");
            Assert.IsEmpty(result.Report?.MissingRequiredBones ?? new List<string>(), $"{name}: HumanTrait-required roles missing");
            Assert.GreaterOrEqual(humanoidRoles.Count, min, $"{name}: fewer than {min} roles Unity's Humanoid adapter recognises");
            Pass(name, "roles");
        }

        [UnityTest]
        public IEnumerator ReferencePose([ValueSource(nameof(CharacterNames))] string name)
        {
            yield return Load(name);
            var m = Manifests[name];
            var c = Require(name);
            RequireDeclared(m, "reference_pose");
            var section = Section(name, "reference_pose");
            var expectedPoses = (JArray)m.Expect["reference_poses"];
            var poses = c.Skeleton.ReferencePoses;
            section["poses"] = new JArray(poses.Select(p => new JObject { ["animation"] = p.AnimationIndex, ["pose_type"] = p.PoseType, ["valid"] = p.IsValid }));
            Assert.AreEqual(expectedPoses.Count, poses.Count, $"{name}: reference pose count");
            for (int i = 0; i < poses.Count; i++)
            {
                Assert.IsTrue(poses[i].IsValid, $"{name}: reference pose {i} invalid: {string.Join("; ", poses[i].Errors ?? Array.Empty<string>())}");
                Assert.AreEqual((string)expectedPoses[i]["pose_type"], poses[i].PoseType, $"{name}: pose {i} type");
            }
            var rp = (JObject)m.Expect["reference_pose"];
            if ((bool?)rp["check_arms"] ?? false)
            {
                float tol = m.Float(rp, "arm_tolerance_deg", 2f);
                var snapshot = c.Snapshot();
                try
                {
                    c.Skeleton.ApplyReferencePose();
                    float left = ArmAngle(c, "leftUpperArm", "leftLowerArm", Vector3.right);
                    float right = ArmAngle(c, "rightUpperArm", "rightLowerArm", Vector3.left);
                    section["left_upper_arm_deg_from_+X"] = left;
                    section["right_upper_arm_deg_from_-X"] = right;
                    Assert.LessOrEqual(left, tol, $"{name}: left upper arm {left:F2}° from glTF +X in the reference pose");
                    Assert.LessOrEqual(right, tol, $"{name}: right upper arm {right:F2}° from glTF -X in the reference pose");
                }
                finally
                {
                    LoadedCharacter.Restore(snapshot);
                }
            }
            Pass(name, "reference_pose");
        }

        // ── expressions ───────────────────────────────────────────────────────

        [UnityTest]
        public IEnumerator Expressions([ValueSource(nameof(CharacterNames))] string name)
        {
            yield return Load(name);
            var m = Manifests[name];
            var c = Require(name);
            RequireDeclared(m, "expressions");
            var section = Section(name, "expressions");
            var responses = c.Hub.ExpressionResponses;
            Assert.IsNotNull(responses, $"{name}: no ExpressionResponseSet");
            Assert.IsTrue(responses.Validation.IsComplete, $"{name}: expression data incomplete: {string.Join("; ", responses.Validation.IncompleteReasons)}");
            var names = responses.Entries.Select(e => e.Name).ToList();
            var expected = ((JArray)m.Expect["expressions"]).Cast<JObject>().ToList();
            section["names"] = new JArray(names);
            if ((bool?)m.Expect["expressions_exact"] ?? false)
                CollectionAssert.AreEqual(expected.Select(e => (string)e["name"]).ToList(), names, $"{name}: expression names/order");

            var bindings = c.Hub.Expressions != null
                ? c.Hub.Expressions.Bindings.Where(b => b?.Property != null).GroupBy(b => b.Property).ToDictionary(g => g.Key, g => g.First())
                : new Dictionary<string, ExpressionTargetBinding>();
            float tol = m.Float(m.Expect, "expression_tolerance", 1e-3f);
            float neutralTol = m.Float(m.Expect, "neutral_tolerance", 1e-5f);
            var neutral = responses.Compose(responses.BuildSnapshot(Drivers(responses.Count)));
            var failures = new List<string>();
            foreach (var record in neutral.Records)
                if (bindings.TryGetValue(record.Property, out var binding) && binding.AuthoredInitial != null
                    && Distance(record.Value, binding.AuthoredInitial, record.Kind) > neutralTol)
                    failures.Add($"t=0 record {record.Property} differs from its initial value");
            section["neutral_records"] = neutral.Records.Count;

            var perExpression = new JArray();
            foreach (var e in expected)
            {
                string expr = (string)e["name"];
                int index = names.IndexOf(expr);
                var entry = new JObject { ["name"] = expr };
                perExpression.Add(entry);
                if (index < 0) { failures.Add($"{expr}: not imported"); continue; }
                var drivers = Drivers(responses.Count);
                drivers[index] = 1.0;
                var composed = responses.Compose(responses.BuildSnapshot(drivers));
                entry["failures"] = composed.Failures.Count;
                entry["records"] = new JArray(composed.Records.Select(r => r.Property));
                foreach (JObject target in (JArray)e["targets"] ?? new JArray())
                {
                    string property = (string)target["property"];
                    if (!TryRecord(composed, property, (int?)target["element"], out var value, out var full))
                    {
                        failures.Add($"{expr}: no composed record for {property}");
                        continue;
                    }
                    TryRecord(neutral, property, (int?)target["element"], out var neutralValue, out var neutralFull);
                    if (target["value"] != null)
                    {
                        if (Math.Abs(value - (double)target["value"]) > tol)
                            failures.Add($"{expr}: {property}[{target["element"]}] = {value:G6}, expected {(double)target["value"]:G6}");
                    }
                    else if (neutralFull != null && Distance(full, neutralFull, ExpressionPropertyKind.Other) <= 1e-6)
                    {
                        failures.Add($"{expr}: {property} does not move at weight 1");
                    }
                }
            }
            section["per_expression"] = perExpression;

            // expressionSetMappings (e.g. vrm1_expressions): every external key drives at least one native expression.
            if (m.Expect["expression_mapping_sets"] is JObject mappingSets)
            {
                var mappingRows = new JObject();
                foreach (var set in mappingSets.Properties())
                {
                    var keys = new JObject();
                    foreach (var key in m.Strings(set.Value))
                    {
                        IReadOnlyList<double> native;
                        try
                        {
                            native = responses.EvaluateExpressionSetMapping(set.Name, new Dictionary<string, double> { [key] = 1.0 });
                        }
                        catch (ArgumentException ex)
                        {
                            failures.Add($"mapping set '{set.Name}': {ex.Message}");
                            break;
                        }
                        var driven = Enumerable.Range(0, native.Count).Where(i => native[i] > 0).Select(i => names[i]).ToList();
                        keys[key] = new JArray(driven);
                        if (driven.Count == 0) failures.Add($"mapping '{set.Name}' key '{key}' drives no expression");
                    }
                    mappingRows[set.Name] = keys;
                }
                section["mapping_sets"] = mappingRows;
            }

            // Scene writes through the optional Unity applicator (LateUpdate).
            var controller = c.Hub.Expressions;
            if (controller != null)
            {
                var sceneChecks = new JArray();
                foreach (var e in expected.Take(64))
                {
                    string expr = (string)e["name"];
                    var targets = ((JArray)e["targets"] ?? new JArray()).Cast<JObject>().ToList();
                    if (targets.Count == 0) continue;
                    controller.ResetAll();
                    yield return null;
                    yield return null;
                    var before = targets.Select(t => SceneValue(bindings, t)).ToList();
                    controller.SetWeight(expr, 1f);
                    yield return null;
                    yield return null;
                    var after = targets.Select(t => SceneValue(bindings, t)).ToList();
                    bool moved = before.Zip(after, (a, b) => a.HasValue && b.HasValue && Math.Abs(a.Value - b.Value) > 1e-5).Any(x => x);
                    sceneChecks.Add(new JObject { ["name"] = expr, ["moved"] = moved });
                    if (!moved) failures.Add($"{expr}: ExpressionController wrote no visible change at weight 1");
                }
                controller.ResetAll();
                yield return null;
                section["scene"] = sceneChecks;
            }
            section["failures"] = new JArray(failures);
            Assert.IsEmpty(failures, $"{name}: " + string.Join("\n", failures.Take(40)));
            Pass(name, "expressions");
        }

        [UnityTest]
        public IEnumerator Suppression([ValueSource(nameof(CharacterNames))] string name)
        {
            yield return Load(name);
            var m = Manifests[name];
            var c = Require(name);
            RequireDeclared(m, "suppressions");
            var section = Section(name, "suppression");
            var responses = c.Hub.ExpressionResponses;
            var names = responses.Entries.Select(e => e.Name).ToList();
            var cases = new JArray();
            var failures = new List<string>();
            foreach (JObject s in (JArray)m.Expect["suppressions"])
            {
                int source = names.IndexOf((string)s["source"]), target = names.IndexOf((string)s["target"]);
                if (source < 0 || target < 0) { failures.Add($"{s["source"]}→{s["target"]}: name not imported"); continue; }
                var drivers = Drivers(responses.Count);
                drivers[source] = 1.0;
                drivers[target] = 1.0;
                var snapshot = responses.BuildSnapshot(drivers);
                double v = snapshot.V[target], expected = (double)s["expected_v"];
                cases.Add(new JObject { ["source"] = s["source"], ["target"] = s["target"], ["u"] = snapshot.U[target], ["v"] = v, ["expected_v"] = expected });
                if (Math.Abs(v - expected) > 1e-6) failures.Add($"{s["source"]}=1 should leave {s["target"]} at v={expected}, got {v}");
            }
            section["cases"] = cases;

            // The same suppressions driven through a mapping set, e.g. vrm1_expressions blink=1 with lookUp=1.
            var mappingCases = new JArray();
            foreach (JObject check in (JArray)m.Expect["mapping_suppressions"] ?? new JArray())
            {
                var inputs = new Dictionary<string, double> { [(string)check["source"]] = 1.0, [(string)check["target"]] = 1.0 };
                var native = responses.EvaluateExpressionSetMapping((string)check["set"], inputs);
                var snapshot = responses.BuildSnapshot(native.Select(x => (double?)x).ToArray());
                foreach (var kv in (JObject)check["expected"])
                {
                    int t = names.IndexOf(kv.Key);
                    double v = t >= 0 ? snapshot.V[t] : double.NaN, expected = (double)kv.Value["v"];
                    mappingCases.Add(new JObject { ["set"] = check["set"], ["source"] = check["source"], ["target"] = check["target"],
                        ["native"] = kv.Key, ["u"] = t >= 0 ? snapshot.U[t] : double.NaN, ["v"] = v, ["expected_v"] = expected });
                    if (!(Math.Abs(v - expected) <= 1e-6))
                        failures.Add($"{check["set"]}: {check["source"]}=1 with {check["target"]}=1 should leave {kv.Key} at v={expected}, got {v}");
                }
            }
            section["mapping_cases"] = mappingCases;
            Assert.IsEmpty(failures, $"{name}: " + string.Join("\n", failures));
            Pass(name, "suppression");
        }

        // ── embedded animation ────────────────────────────────────────────────

        [UnityTest]
        public IEnumerator EmbeddedAnimations([ValueSource(nameof(CharacterNames))] string name)
        {
            yield return Load(name);
            var m = Manifests[name];
            var c = Require(name);
            var expected = ((JArray)m.Expect["animations"] ?? new JArray()).Cast<JObject>().ToList();
            if (expected.Count == 0) Assert.Ignore($"{name}: the file has no animations");
            var section = Section(name, "animations");
            var clips = CharacterImport.Clips(c.Root).ToList();
            var animation = c.Root.GetComponent<Animation>();
            if (animation != null) animation.enabled = false;
            var failures = new List<string>();
            var rows = new JArray();
            var snapshot = c.Snapshot();
            foreach (var e in expected)
            {
                string clipName = (string)e["name"];
                var clip = clips.FirstOrDefault(k => k.name == clipName);
                var row = new JObject { ["name"] = clipName, ["kind"] = e["kind"], ["expected"] = e["duration"] };
                rows.Add(row);
                if (clip == null) { failures.Add($"clip '{clipName}' not imported"); continue; }
                row["length"] = clip.length;
                if (Math.Abs(clip.length - (float)e["duration"]) > m.Float(e, "tolerance", 0.05f))
                    failures.Add($"clip '{clipName}' length {clip.length:F3}s, expected {(float)e["duration"]:F3}s");
                if ((string)e["kind"] != "take") continue;
                int nonFinite = 0;
                foreach (var f in new[] { 0f, 0.5f, 1f })
                {
                    clip.SampleAnimation(c.Root, f * clip.length);
                    nonFinite += c.Root.GetComponentsInChildren<Transform>(true)
                        .Count(t => !CharacterGeometry.IsFinite(t.localRotation) || !CharacterGeometry.IsFinite(t.localPosition));
                }
                row["non_finite_transforms"] = nonFinite;
                if (nonFinite > 0) failures.Add($"clip '{clipName}' produced {nonFinite} non-finite transforms");
            }
            LoadedCharacter.Restore(snapshot);
            section["clips"] = rows;
            yield return null;
            Assert.IsEmpty(failures, $"{name}: " + string.Join("\n", failures));
            Pass(name, "animations");
        }

        // ── hints ─────────────────────────────────────────────────────────────

        [UnityTest]
        public IEnumerator CameraHintsAndLookAt([ValueSource(nameof(CharacterNames))] string name)
        {
            yield return Load(name);
            var m = Manifests[name];
            var c = Require(name);
            var hintsExpected = ((JArray)m.Expect["camera_hints"] ?? new JArray()).Cast<JObject>().ToList();
            int lookExpected = (int?)m.Expect["lookat_targets"] ?? 0;
            if (hintsExpected.Count == 0 && lookExpected == 0) Assert.Ignore($"{name}: no camera hints or look-at targets in the file");
            var section = Section(name, "hints");
            var set = c.Hub != null ? c.Hub.CameraHints : c.Root.GetComponent<CameraHintSet>();
            var hints = set != null ? set.Hints : (IReadOnlyList<CameraHint>)new List<CameraHint>();
            section["hints"] = new JArray(hints.Select(h => new JObject
            {
                ["node"] = h.NodeIndex, ["role"] = h.Role, ["label"] = h.Label,
                ["frame_defined"] = set.TryGetInitialCameraFrame(h, c.Root.transform, out _),
                ["projection"] = h.Projection != null,
                ["target"] = h.Target != null ? h.Target.name : null,
            }));
            Assert.AreEqual(hintsExpected.Count, hints.Count, $"{name}: camera hint count");
            foreach (var e in hintsExpected)
            {
                var hint = hints.FirstOrDefault(h => h.NodeIndex == (int)e["node"]);
                Assert.IsNotNull(hint, $"{name}: no hint on node {e["node"]}");
                Assert.AreEqual((string)e["role"], hint.Role, $"{name}: hint role on node {e["node"]}");
                Assert.IsTrue(set.TryGetInitialCameraFrame(hint, c.Root.transform, out _), $"{name}: hint on node {e["node"]} defines no frame");
                if (e["target_name"] != null)
                {
                    Assert.IsNotNull(hint.Target, $"{name}: hint on node {e["node"]} has targetNode {e["target_node"]} in the file but no subject Transform");
                    Assert.AreEqual((string)e["target_name"], hint.Target.name, $"{name}: hint on node {e["node"]} subject");
                    Assert.IsTrue(CameraHintSet.TryGetSubjectPoint(hint, out _), $"{name}: hint on node {e["node"]} yields no subject point");
                }
            }
            var looks = c.Hub != null ? c.Hub.LookAtTargets : c.Root.GetComponent<LookAtTargetSet>();
            int lookCount = looks != null ? looks.Targets.Count : 0;
            section["lookat_targets"] = lookCount;
            Assert.AreEqual(lookExpected, lookCount, $"{name}: look-at target count");
            // Checked last so the checks above still run: a hint whose file names a camera must carry its projection.
            var missingProjection = hints
                .Where(h => c.Gltf?["nodes"]?[h.NodeIndex]?["extensions"]?["KHR_node_camera_hint"]?["camera"] != null && h.Projection == null)
                .Select(h => $"{h.Role} (node {h.NodeIndex})").ToList();
            section["projection_missing"] = new JArray(missingProjection);
            Assert.IsEmpty(missingProjection,
                $"{name}: UnityGLTF imported no projection for hints whose KHR_node_camera_hint names a camera: {string.Join(", ", missingProjection)}");
            Pass(name, "hints");
        }

        // ── retarget ──────────────────────────────────────────────────────────

        [UnityTest]
        public IEnumerator HumanoidRetarget([ValueSource(nameof(RetargetCases))] string testCase)
        {
            string name = testCase.Split('|')[0], clipName = testCase.Split('|')[1];
            if (clipName.StartsWith("!", StringComparison.Ordinal)) Assert.Fail($"{name}: clip index unusable: {clipName.Substring(1)}");
            yield return Load(name);
            var m = Manifests[name];
            var c = Require(name);
            Assert.IsNotNull(c.Avatar, $"{name}: no humanoid Avatar to retarget onto");
            var entry = m.RetargetClips().First(k => k.Name == clipName);
            var task = RetargetSource.LoadAsync(entry);
            yield return Wait(task, 60f);
            Assert.IsTrue(task.IsCompleted && !task.IsFaulted, $"{clipName}: source import did not complete");
            using (var source = task.Result)
            {
                Assert.IsNotNull(source, $"{clipName}: source import failed");
                Assert.IsNotNull(source.Character.Avatar, $"{clipName}: source Avatar not built from its KHR mapping + t_pose");
                Assert.IsTrue(source.Character.Avatar.isHuman, $"{clipName}: source Avatar not human");
                Assert.IsNotNull(source.Take, $"{clipName}: take '{entry.Animation}' not found");
                var section = Section(name, "retarget");
                var row = new JObject
                {
                    ["clip"] = clipName, ["source_sha256"] = entry.Sha256, ["designation_added"] = source.DesignationAdded,
                    ["length"] = source.Length, ["expected_duration"] = entry.Duration,
                };
                section[clipName] = row;
                float tol = m.Float(m.Retarget, "floor_tolerance_m", 0.05f);
                var fractions = ((JArray)m.Retarget["sample_fractions"] ?? new JArray(0f, 0.25f, 0.5f, 0.75f)).Select(t => (float)t).ToList();
                var roles = c.Skeleton.Result.Bones.Where(kv => kv.Value != null).OrderBy(kv => kv.Key).ToList();
                var snapshot = c.Snapshot();
                var samples = new JArray();
                var failures = new List<string>();
                var skinned = c.Root.GetComponentsInChildren<SkinnedMeshRenderer>().OrderByDescending(s => s.sharedMesh != null ? s.sharedMesh.vertexCount : 0).FirstOrDefault();
                Vector3[] restVertices = null;
                if (skinned != null)
                {
                    var baked = new Mesh();
                    skinned.BakeMesh(baked);
                    restVertices = baked.vertices;
                    UnityEngine.Object.Destroy(baked);
                    var mappedSet = new HashSet<Transform>(roles.Select(kv => kv.Value));
                    row["skin_renderer"] = skinned.name;
                    row["skin_bones"] = skinned.bones.Length;
                    row["skin_bones_that_are_mapped"] = skinned.bones.Count(b => b != null && mappedSet.Contains(b));
                    row["skin_null_bones"] = skinned.bones.Count(b => b == null);
                    row["skin_root_bone"] = skinned.rootBone != null ? skinned.rootBone.name : null;
                }
                float bakedMaxDelta = 0f;
                var sourceRest = source.Character.Snapshot();
                var sourceRoles = source.Character.Skeleton.Result.Bones.Where(kv => kv.Value != null).ToList();
                float sourceMaxFromRest = 0f;
                float minFoot = float.PositiveInfinity, maxFromRest = 0f;
                string maxFromRestRole = null;
                foreach (var f in fractions)
                {
                    float t = f * source.Length;
                    source.ApplyTo(c, t);
                    foreach (var kv in sourceRoles)
                        sourceMaxFromRest = Mathf.Max(sourceMaxFromRest, Quaternion.Angle(sourceRest[kv.Value].Item2, kv.Value.localRotation));
                    var humanPose = new HumanPose();
                    c.PoseHandler.GetHumanPose(ref humanPose);
                    float muscleMax = humanPose.muscles.Length > 0 ? humanPose.muscles.Max(Mathf.Abs) : 0f;
                    foreach (var kv in roles)
                    {
                        float a = Quaternion.Angle(snapshot[kv.Value].Item2, kv.Value.localRotation);
                        if (a > maxFromRest) { maxFromRest = a; maxFromRestRole = kv.Key; }
                    }
                    if (restVertices != null)
                    {
                        var baked = new Mesh();
                        skinned.BakeMesh(baked);
                        var v = baked.vertices;
                        for (int i = 0; i < v.Length && i < restVertices.Length; i++) bakedMaxDelta = Mathf.Max(bakedMaxDelta, (v[i] - restVertices[i]).magnitude);
                        UnityEngine.Object.Destroy(baked);
                    }
                    float foot = CharacterGeometry.MinFootHeight(c, out var lowest);
                    minFoot = Mathf.Min(minFoot, foot);
                    var quats = new JObject();
                    int bad = 0;
                    foreach (var kv in roles)
                    {
                        var q = kv.Value.localRotation;
                        if (!CharacterGeometry.IsFinite(q) || !CharacterGeometry.IsFinite(kv.Value.position)) bad++;
                        quats[kv.Key] = new JArray(q.x, q.y, q.z, q.w);
                    }
                    samples.Add(new JObject
                    {
                        ["t"] = t, ["min_foot_y"] = foot, ["lowest"] = lowest, ["non_finite"] = bad, ["muscle_abs_max"] = muscleMax,
                        ["left_arm_dir"] = ArmDirection(c, "left"), ["source_left_arm_dir"] = ArmDirection(source.Character, "left"),
                        ["local_quats"] = quats,
                    });
                    if (bad > 0) failures.Add($"t={t:F2}s: {bad} non-finite mapped bones");
                    if (foot < -tol) failures.Add($"t={t:F2}s: {lowest} at y={foot:F3} m, below the floor by more than {tol} m");
                }
                row["min_foot_y"] = minFoot;
                row["max_deg_from_rest"] = maxFromRest;
                row["max_deg_from_rest_role"] = maxFromRestRole;
                row["source_max_deg_from_import_pose"] = sourceMaxFromRest;
                row["skin_baked_max_vertex_delta"] = bakedMaxDelta;
                // A retarget that leaves every mapped bone at rest is a silent no-op, not a pass.
                float minMotion = m.Float(m.Retarget, "min_motion_deg", 5f);
                if (maxFromRest < minMotion)
                    failures.Add($"no mapped bone moved more than {minMotion}° from rest (max {maxFromRest:F1}° on {maxFromRestRole})");
                row["samples"] = samples;

                // The same take baked to a humanoid muscle clip and sampled through the Animator (reported only).
                var humanoidClip = source.BakeHumanoidClip();
                row["humanoid_clip_is_human_motion"] = humanoidClip.humanMotion;
                LoadedCharacter.Restore(snapshot);
                yield return null;
                LoadedCharacter.Restore(snapshot);
                UnityEngine.Object.Destroy(humanoidClip);
                Pass(name, "retarget/" + clipName, failures);
                Assert.IsEmpty(failures, $"{name} × {clipName}: " + string.Join("\n", failures));
            }
        }

        // ── helpers ───────────────────────────────────────────────────────────

        private static IEnumerator Load(string name)
        {
            if (Loaded.ContainsKey(name)) yield break;
            var m = CharacterManifest.All().First(x => x.Name == name);
            Manifests[name] = m;
            var task = LoadedCharacter.LoadAsync(m.GlbPath);
            float deadline = Time.realtimeSinceStartup + 180f;
            while (!task.IsCompleted && Time.realtimeSinceStartup < deadline) yield return null;
            // A failed import is cached as null with its error, so later checks fail fast instead of re-importing.
            Loaded[name] = task.IsCompleted && !task.IsFaulted ? task.Result : null;
            if (!task.IsCompleted) LoadErrors[name] = "import did not finish within 180 s";
            else if (task.IsFaulted) LoadErrors[name] = task.Exception.GetBaseException().ToString();
            else if (task.Result == null) LoadErrors[name] = "UnityGLTF returned no scene";
            if (Loaded[name] != null) Loaded[name].Root.transform.position = Vector3.zero;
        }

        private static LoadedCharacter Require(string name)
        {
            var c = Loaded.TryGetValue(name, out var loaded) ? loaded : null;
            if (c == null) Assert.Fail($"{name}: import failed: {(LoadErrors.TryGetValue(name, out var e) ? e : "unknown")}");
            return c;
        }

        private static IEnumerator Wait(Task task, float seconds)
        {
            float deadline = Time.realtimeSinceStartup + seconds;
            while (!task.IsCompleted && Time.realtimeSinceStartup < deadline) yield return null;
            Assert.IsTrue(task.IsCompleted, $"import did not finish within {seconds}s");
            if (task.Exception != null) throw task.Exception;
        }

        private static void RequireDeclared(CharacterManifest m, string key)
        {
            var token = m.Expect[key];
            if (token == null || (token is JArray a && a.Count == 0) || (token.Type == JTokenType.Boolean && !(bool)token))
                Assert.Ignore($"{m.Name}: the manifest declares no '{key}' (not in the file)");
        }

        private static double?[] Drivers(int count)
        {
            var d = new double?[count];
            for (int i = 0; i < count; i++) d[i] = 0.0;
            return d;
        }

        private static bool TryRecord(ExpressionComposition composition, string property, int? element, out double value, out IReadOnlyList<double> full)
        {
            value = 0;
            full = null;
            foreach (var r in composition.Records)
            {
                if (r.Property == property)
                {
                    full = r.Value;
                    if (element.HasValue) { if (element.Value >= r.Value.Count) return false; value = r.Value[element.Value]; }
                    return true;
                }
                if (element.HasValue && r.Property == property + "/" + element.Value)
                {
                    full = r.Value;
                    value = r.Value.Count > 0 ? r.Value[0] : 0;
                    return true;
                }
            }
            return false;
        }

        private static double Distance(IReadOnlyList<double> a, IReadOnlyList<double> b, ExpressionPropertyKind kind)
        {
            if (a == null || b == null || a.Count != b.Count) return double.PositiveInfinity;
            double same = 0, flipped = 0;
            for (int i = 0; i < a.Count; i++)
            {
                same = Math.Max(same, Math.Abs(a[i] - b[i]));
                flipped = Math.Max(flipped, Math.Abs(a[i] + b[i]));
            }
            return kind == ExpressionPropertyKind.NodeRotation ? Math.Min(same, flipped) : same;
        }

        private static double? SceneValue(Dictionary<string, ExpressionTargetBinding> bindings, JObject target)
        {
            string property = (string)target["property"];
            int? element = (int?)target["element"];
            if (!bindings.TryGetValue(property, out var b) && element.HasValue)
                bindings.TryGetValue(property + "/" + element.Value, out b);
            if (b == null) return null;
            switch (b.Kind)
            {
                case ExpressionPropertyKind.NodeWeights:
                    var r = b.MorphRenderers != null ? b.MorphRenderers.FirstOrDefault(x => x != null) : null;
                    return r != null && element.HasValue && element.Value < r.sharedMesh.blendShapeCount
                        ? r.GetBlendShapeWeight(element.Value) : (double?)null;
                case ExpressionPropertyKind.NodeRotation:
                    var q = b.Node.localRotation;
                    return q.x * 1.0 + q.y * 3.0 + q.z * 5.0 + q.w * 7.0;
                case ExpressionPropertyKind.NodeTranslation:
                    var p = b.Node.localPosition;
                    return p.x + p.y * 3.0 + p.z * 5.0;
                case ExpressionPropertyKind.NodeScale:
                    var s = b.Node.localScale;
                    return s.x + s.y * 3.0 + s.z * 5.0;
                case ExpressionPropertyKind.TextureOffset:
                case ExpressionPropertyKind.TextureScale:
                case ExpressionPropertyKind.TextureRotation:
                    // The applicator writes texture transforms to a MaterialPropertyBlock per renderer and slot.
                    if (b.TextureRenderers == null || b.TextureRenderers.Length == 0 || b.TextureRenderers[0] == null) return null;
                    var block = new MaterialPropertyBlock();
                    int slot = b.TextureMaterialSlots != null && b.TextureMaterialSlots.Length > 0 ? b.TextureMaterialSlots[0] : 0;
                    b.TextureRenderers[0].GetPropertyBlock(block, slot);
                    if (b.Kind == ExpressionPropertyKind.TextureRotation && !string.IsNullOrEmpty(b.TextureRotationProperty))
                        return block.GetFloat(b.TextureRotationProperty);
                    if (string.IsNullOrEmpty(b.TextureStProperty)) return null;
                    var st = block.GetVector(b.TextureStProperty);
                    return st.x + st.y * 3.0 + st.z * 5.0 + st.w * 7.0;
                default:
                    return null;
            }
        }

        private static float ArmAngle(LoadedCharacter c, string upper, string lower, Vector3 gltfAxis)
        {
            bool mappedUpper = c.TryGetRole(upper, out var a), mappedLower = c.TryGetRole(lower, out var b);
            Assert.IsTrue(mappedUpper && mappedLower, $"roles {upper}/{lower} not mapped");
            var dir = CharacterGeometry.ToGltf(b.position - a.position).normalized;
            return Vector3.Angle(dir, gltfAxis);
        }

        private static JArray ArmDirection(LoadedCharacter c, string side)
        {
            if (!c.TryGetRole(side + "UpperArm", out var upper) || !c.TryGetRole(side + "LowerArm", out var lower)) return null;
            var d = (lower.position - upper.position).normalized;
            return new JArray(Math.Round(d.x, 3), Math.Round(d.y, 3), Math.Round(d.z, 3));
        }

        private static JObject Section(string name, string key)
        {
            if (!Reports.TryGetValue(name, out var report))
                Reports[name] = report = new JObject { ["character"] = name, ["utc"] = DateTime.UtcNow.ToString("o"), ["results"] = new JObject() };
            // Assigning a token that already has a parent stores a clone (Json.NET), so assign only a new section.
            if (report[key] is JObject existing) return existing;
            var section = new JObject();
            report[key] = section;
            return section;
        }

        // Detail sections are written as each check finishes; the NUnit outcome is recorded in TearDown.
        private static void Pass(string name, string check, List<string> failures = null) => Record(name, null, null, null);

        private static void Record(string name, string check, string outcome, string message)
        {
            var report = Reports.TryGetValue(name, out var r) ? r : (Reports[name] = new JObject { ["character"] = name, ["results"] = new JObject() });
            if (check != null)
                ((JObject)report["results"])[check] = string.IsNullOrEmpty(message) ? outcome : outcome + ": " + message;
            var m = Manifests.TryGetValue(name, out var mm) ? mm : null;
            if (m != null)
            {
                report["glb_sha256"] = m.Json["source_sha256"];
                report["private"] = m.IsPrivate;
            }
            Evidence.WriteJson(System.IO.Path.Combine(Evidence.Dir("tests"), Evidence.SafeName(name) + ".json"), report);
        }

        private static void WriteSummary()
        {
            var summary = new JObject
            {
                ["utc"] = DateTime.UtcNow.ToString("o"),
                ["unity"] = Application.unityVersion,
                ["characters"] = new JObject(Reports.Select(kv => new JProperty(kv.Key, kv.Value["results"]))),
            };
            Evidence.WriteJson(System.IO.Path.Combine(Evidence.Dir("tests"), "summary.json"), summary);
        }

#if UNITY_EDITOR
        private static string ProjectRelative(string absolute)
        {
            var project = System.IO.Directory.GetParent(Application.dataPath).FullName.Replace('\\', '/') + "/";
            return absolute.Replace('\\', '/').Replace(project, "");
        }
#endif
    }

    /// <summary>The role names UnityGLTF's Humanoid adapter maps (KhrCharacterSkeletonBaker.TryGetHumanName).</summary>
    internal static class KhrCharacterSkeletonBakerProxy
    {
        private static readonly HashSet<string> Roles = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "hips", "spine", "chest", "upperChest", "neck", "head", "jaw", "leftEye", "rightEye",
            "leftShoulder", "rightShoulder", "leftUpperArm", "leftLowerArm", "leftHand",
            "rightUpperArm", "rightLowerArm", "rightHand", "leftUpperLeg", "leftLowerLeg", "leftFoot", "leftToes",
            "rightUpperLeg", "rightLowerLeg", "rightFoot", "rightToes",
        };

        public static bool IsHumanoidRole(string role) => role != null && Roles.Contains(role);
    }
}
