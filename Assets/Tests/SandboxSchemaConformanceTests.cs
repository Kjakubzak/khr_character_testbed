using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.TestTools;
using UnityGLTF.KhrCharacter;
using Samples.Shared;

namespace KhrCharacterTestbed.Tests
{
    /// <summary>
    /// JSON-schema conformance gate (04 neutral lens, Phase 4-P3, bug B4). A dependency-free, license-clean checker
    /// (KhrSchemaCheck) encodes the hard constraints from the PR #2512 schemas - required fields, string minLength,
    /// integer/number ranges. POSITIVE: export the real SC-* wire and assert every KHR_character_* payload conforms.
    /// NEGATIVE: feed malformed payloads (one per required/minLength/range vector) and assert the checker rejects them,
    /// so a regression that emits an out-of-spec wire would be caught. Anti-hollow via real plugin types.
    /// </summary>
    public class SandboxSchemaConformanceTests
    {
        private readonly List<Object> _created = new List<Object>();

        [TearDown]
        public void TearDown()
        {
            foreach (var o in _created)
                if (o != null) Object.DestroyImmediate(o);
            _created.Clear();
        }

        // ── The checker: spec constraints as code (PR #2512). Each returns the list of violations (empty == OK). ──
        private static class KhrSchemaCheck
        {
            public static List<string> Character(JObject ext)
            {
                var e = new List<string>();
                var rootNode = ext["rootNode"];
                if (rootNode == null || rootNode.Type != JTokenType.Integer) e.Add("KHR_character.rootNode is required (integer).");
                else if ((int)rootNode < 0) e.Add("KHR_character.rootNode must be >= 0.");
                return e;
            }

            public static List<string> CameraHint(JObject ext)
            {
                var e = new List<string>();
                var role = ext["role"];
                if (role == null || role.Type != JTokenType.String || ((string)role).Length < 1)
                    e.Add("KHR_node_camera_hint.role is required (string, minLength 1).");
                NonEmptyOptionalString(ext, "label", "KHR_node_camera_hint", e);
                NonNegativeOptionalInt(ext, "camera", "KHR_node_camera_hint", e);
                NonNegativeOptionalInt(ext, "targetNode", "KHR_node_camera_hint", e);
                return e;
            }

            public static List<string> LookatTarget(JObject ext)
            {
                var e = new List<string>();
                NonEmptyOptionalString(ext, "hint", "KHR_node_lookat_target", e);
                return e;
            }

            public static List<string> Mask(JObject mask)
            {
                var e = new List<string>();
                var target = mask["target"];
                if (target == null || target.Type != JTokenType.String || ((string)target).Length < 1)
                    e.Add("mask.target is required (non-empty string).");
                UnitRangeOptionalNumber(mask, "amount", e);
                UnitRangeOptionalNumber(mask, "threshold", e);
                return e;
            }

            private static void NonEmptyOptionalString(JObject o, string key, string ctx, List<string> e)
            {
                var v = o[key];
                if (v != null && (v.Type != JTokenType.String || ((string)v).Length < 1))
                    e.Add($"{ctx}.{key} must be a non-empty string (minLength 1).");
            }

            private static void NonNegativeOptionalInt(JObject o, string key, string ctx, List<string> e)
            {
                var v = o[key];
                if (v != null && (v.Type != JTokenType.Integer || (int)v < 0))
                    e.Add($"{ctx}.{key} must be an integer >= 0.");
            }

            private static void UnitRangeOptionalNumber(JObject o, string key, List<string> e)
            {
                var v = o[key];
                if (v == null) return;
                if (v.Type != JTokenType.Float && v.Type != JTokenType.Integer) { e.Add($"mask.{key} must be a number."); return; }
                double d = (double)v;
                if (d < 0.0 || d > 1.0) e.Add($"mask.{key} must be in [0,1].");
            }
        }

        // ── POSITIVE: the real exported wire conforms (recursive walk finds every payload by extension name) ──
        [UnityTest]
        public IEnumerator RealWire_ConformsToSchemaConstraints(
            [Values("SC-Body.glb", "SC-LookAt.glb", "SC-Face.glb", "SC-FacePlus.glb")] string fixture)
        {
            var load = SandboxTestUtil.LoadSynthetic(fixture, _created);
            yield return load;
            var hub = load.Current.GetComponent<KhrCharacter>();
            Assert.IsNotNull(hub, $"{fixture} should import a KhrCharacter hub.");

            byte[] glb = CharacterLoader.ExportToGlb(hub.gameObject, out _);
            string json = CharacterLoader.ExtractGltfJson(glb);
            Assert.IsNotNull(json, $"{fixture} re-export should yield a JSON chunk.");
            var root = JObject.Parse(json);

            var errors = new List<string>();
            int character = 0, cameraHint = 0, lookat = 0, mask = 0;
            void Walk(JToken node)
            {
                if (node is JObject o)
                {
                    if (o["extensions"] is JObject exts)
                    {
                        if (exts["KHR_character"] is JObject c) { character++; errors.AddRange(KhrSchemaCheck.Character(c)); }
                        if (exts["KHR_node_camera_hint"] is JObject ch) { cameraHint++; errors.AddRange(KhrSchemaCheck.CameraHint(ch)); }
                        if (exts["KHR_node_lookat_target"] is JObject lt) { lookat++; errors.AddRange(KhrSchemaCheck.LookatTarget(lt)); }
                        if (exts["KHR_character_expression_mask"] is JObject mk && mk["masks"] is JArray masks)
                            foreach (var m in masks) if (m is JObject mo) { mask++; errors.AddRange(KhrSchemaCheck.Mask(mo)); }
                    }
                    foreach (var p in o.Properties()) Walk(p.Value);
                }
                else if (node is JArray a) foreach (var c in a) Walk(c);
            }
            Walk(root);

            string joined = string.Join(" | ", errors);
            Assert.IsEmpty(errors, $"{fixture} wire must conform to the KHR_character schema constraints: {joined}");
            // Anti-trivial: the relevant fixture must actually carry the payload we claim to have validated.
            Assert.Greater(character, 0, $"{fixture} must declare the root KHR_character extension.");
            if (fixture == "SC-Body.glb") Assert.Greater(cameraHint, 0, "SC-Body must carry a camera hint to validate.");
            if (fixture == "SC-LookAt.glb") Assert.Greater(lookat, 0, "SC-LookAt must carry a look-at target to validate.");
            if (fixture == "SC-Face.glb") Assert.Greater(mask, 0, "SC-Face must carry an expression mask to validate.");
        }

        // ── NEGATIVE: the checker rejects each malformed vector (required / minLength / range) ──
        [Test]
        public void Character_MissingRootNode_Rejected()
            => Assert.IsNotEmpty(KhrSchemaCheck.Character(new JObject()), "missing rootNode must be rejected.");

        [Test]
        public void Character_Valid_Accepted()
            => Assert.IsEmpty(KhrSchemaCheck.Character(new JObject { ["rootNode"] = 0 }));

        [Test]
        public void CameraHint_MissingRole_Rejected()
            => Assert.IsNotEmpty(KhrSchemaCheck.CameraHint(new JObject { ["label"] = "Portrait" }), "missing role must be rejected.");

        [Test]
        public void CameraHint_EmptyRole_Rejected()
            => Assert.IsNotEmpty(KhrSchemaCheck.CameraHint(new JObject { ["role"] = "" }), "empty role (minLength 1) must be rejected.");

        [Test]
        public void CameraHint_NegativeTargetNode_Rejected()
            => Assert.IsNotEmpty(KhrSchemaCheck.CameraHint(new JObject { ["role"] = "portrait", ["targetNode"] = -1 }),
                "a negative targetNode must be rejected.");

        [Test]
        public void CameraHint_Valid_Accepted()
            => Assert.IsEmpty(KhrSchemaCheck.CameraHint(new JObject { ["role"] = "portrait", ["label"] = "Portrait", ["targetNode"] = 3 }));

        [Test]
        public void LookatTarget_EmptyHint_Rejected()
            => Assert.IsNotEmpty(KhrSchemaCheck.LookatTarget(new JObject { ["hint"] = "" }), "empty hint (minLength 1) must be rejected.");

        [Test]
        public void LookatTarget_NoHint_Accepted()
            => Assert.IsEmpty(KhrSchemaCheck.LookatTarget(new JObject()), "an empty {} look-at target is valid (hint optional).");

        [Test]
        public void Mask_MissingTarget_Rejected()
            => Assert.IsNotEmpty(KhrSchemaCheck.Mask(new JObject { ["amount"] = 0.5 }), "a mask without a target must be rejected.");

        [Test]
        public void Mask_AmountAboveRange_Rejected()
            => Assert.IsNotEmpty(KhrSchemaCheck.Mask(new JObject { ["target"] = "aa", ["amount"] = 1.5 }), "amount > 1 must be rejected.");

        [Test]
        public void Mask_ThresholdBelowRange_Rejected()
            => Assert.IsNotEmpty(KhrSchemaCheck.Mask(new JObject { ["target"] = "aa", ["threshold"] = -0.1 }), "threshold < 0 must be rejected.");

        [Test]
        public void Mask_Valid_Accepted()
            => Assert.IsEmpty(KhrSchemaCheck.Mask(new JObject { ["target"] = "aa", ["type"] = "blend", ["amount"] = 0.5, ["threshold"] = 0.0 }));
    }
}
