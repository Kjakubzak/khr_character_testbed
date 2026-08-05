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
    /// JSON-schema conformance gate (Phase 4-P3, bug B4). A dependency-free, license-clean checker
    /// (KhrSchemaCheck) encodes the hard constraints from the PR #2512 schemas - required fields, string minLength,
    /// integer/number ranges. POSITIVE: read the committed SC-* wire and assert every KHR_character_* payload conforms.
    /// NEGATIVE: feed malformed payloads (one per required/minLength/range vector) and assert the checker rejects them,
    /// so a regression that emits an out-of-spec wire would be caught. Anti-hollow via real plugin types.
    /// </summary>
    public class SandboxSchemaConformanceTests
    {
        private const string UnityHumanoidVocabulary =
            "https://example.com/skeleton-vocabularies/unity-humanoid/v1";
        private const string DefaultSkeletonVocabulary =
            "https://example.com/skeleton-vocabularies/default/v1";
        private const string Vrm10HumanoidVocabulary =
            "https://github.com/vrm-c/vrm-specification/blob/70ae16e93abd6da727fdf641b67aa41010c6d933/specification/VRMC_vrm-1.0/humanoid.md";

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
                if (target == null || target.Type != JTokenType.Integer || (int)target < 0)
                    e.Add("mask.target is required (integer >= 0).");
                var type = mask["type"];
                if (type != null)
                {
                    string value = type.Type == JTokenType.String ? (string)type : null;
                    if (string.IsNullOrEmpty(value))
                        e.Add("mask.type must be a non-empty string.");
                    else if (value != "blend" && value != "block"
                        && !System.Text.RegularExpressions.Regex.IsMatch(
                            value,
                            "^[A-Z0-9]+_[a-z0-9]+(?:_[a-z0-9]+)*$"))
                        e.Add("a custom mask.type must be shaped like a glTF vendor extension name.");
                }
                UnitRangeOptionalNumber(mask, "amount", e);
                UnitRangeOptionalNumber(mask, "threshold", e);
                return e;
            }

            public static List<string> JointAssociation(JObject association)
            {
                var e = new List<string>();
                var node = association["node"];
                if (node == null || node.Type != JTokenType.Integer || (int)node < 0)
                    e.Add("joint association.node is required (integer >= 0).");
                var name = association["name"];
                if (name != null && name.Type != JTokenType.String)
                    e.Add("joint association.name must be a string.");
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

        // ── POSITIVE: the committed wire conforms (recursive walk finds every payload by extension name) ──
        [Test]
        public void RealWire_ConformsToSchemaConstraints(
            [Values("SC-Body.glb", "SC-Degraded.glb", "SC-LookAt.glb", "SC-Face.glb", "SC-FacePlus.glb")] string fixture)
        {
            byte[] glb = System.IO.File.ReadAllBytes(CharacterLoader.SyntheticPath(fixture));
            string json = CharacterLoader.ExtractGltfJson(glb);
            Assert.IsNotNull(json, $"{fixture} should yield a JSON chunk.");
            var root = JObject.Parse(json);

            var errors = new List<string>();
            int character = 0, cameraHint = 0, lookat = 0, mask = 0, jointAssociation = 0;
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
                        if (exts["KHR_character_skeleton_mapping"] is JObject sm
                            && sm["skeletalRigMappings"] is JObject rigs)
                            foreach (var rig in rigs.Properties())
                                if (rig.Value is JObject joints)
                                    foreach (var joint in joints.Properties())
                                        if (joint.Value is JObject association)
                                        {
                                            jointAssociation++;
                                            errors.AddRange(KhrSchemaCheck.JointAssociation(association));
                                        }
                                        else errors.Add("skeleton joint association must be an object.");
                    }
                    foreach (var p in o.Properties()) Walk(p.Value);
                }
                else if (node is JArray a) foreach (var c in a) Walk(c);
            }
            Walk(root);
            errors.AddRange(ExpressionChannelReferences(root));
            errors.AddRange(ExpressionObjectReferences(root));
            errors.AddRange(SkeletonObjectReferences(root));

            string joined = string.Join(" | ", errors);
            Assert.IsEmpty(errors, $"{fixture} wire must conform to the KHR_character schema constraints: {joined}");
            // Anti-trivial: the relevant fixture must actually carry the payload we claim to have validated.
            Assert.Greater(character, 0, $"{fixture} must declare the root KHR_character extension.");
            if (fixture == "SC-Body.glb") Assert.Greater(cameraHint, 0, "SC-Body must carry a camera hint to validate.");
            if (fixture == "SC-LookAt.glb") Assert.Greater(lookat, 0, "SC-LookAt must carry a look-at target to validate.");
            if (fixture == "SC-Face.glb") Assert.Greater(mask, 0, "SC-Face must carry an expression mask to validate.");
            if (fixture == "SC-Body.glb" || fixture == "SC-Degraded.glb")
            {
                Assert.Greater(jointAssociation, 0, $"{fixture} must carry skeleton joint associations to validate.");
                var rigs = root["extensions"]?["KHR_character_skeleton_mapping"]?["skeletalRigMappings"] as JObject;
                Assert.IsNotNull(rigs?[UnityHumanoidVocabulary],
                    $"{fixture} must use the versioned Unity Humanoid vocabulary URI.");
            }
            if (fixture == "SC-Degraded.glb")
            {
                var rigs = root["extensions"]?["KHR_character_skeleton_mapping"]?["skeletalRigMappings"] as JObject;
                Assert.IsNotNull(rigs, "SC-Degraded must carry a skeleton mapping.");
                foreach (var rig in rigs.Properties())
                    Assert.IsNull(rig.Value?["leftFoot"],
                        "SC-Degraded must omit leftFoot instead of carrying an invalid node reference.");
            }
        }

        [TestCase("SampleAssets/FromBlender/expressions_mapping.glb", null)]
        [TestCase("SampleAssets/FromBlender/full.glb", DefaultSkeletonVocabulary)]
        [TestCase("SampleAssets/FromBlender/skeleton.glb", DefaultSkeletonVocabulary)]
        [TestCase("SampleAssets/FromBlender/skeleton_refpose.glb", DefaultSkeletonVocabulary)]
        [TestCase("SampleAssets/FromBlender/starter.glb", DefaultSkeletonVocabulary)]
        [TestCase("SampleAssets/VRM_KHR_Examples/khr-character-example.glb", Vrm10HumanoidVocabulary)]
        [TestCase("SampleAssets/VRM_KHR_Examples/khr-character-example-always.glb", Vrm10HumanoidVocabulary)]
        [TestCase("SampleAssets/VRM_KHR_Examples/khr-character-example-first-person.glb", Vrm10HumanoidVocabulary)]
        [TestCase("SampleAssets/VRM_KHR_Examples/khr-character-example-third-person.glb", Vrm10HumanoidVocabulary)]
        public void CommittedMappingFixtures_UseStableIdentifiersAndResolvableIndices(
            string relativePath, string expectedSkeletonVocabulary)
        {
            string path = System.IO.Path.Combine(Application.dataPath, relativePath);
            var root = JObject.Parse(CharacterLoader.ExtractGltfJson(System.IO.File.ReadAllBytes(path)));
            var errors = ExpressionObjectReferences(root);
            errors.AddRange(SkeletonObjectReferences(root));
            Assert.IsEmpty(errors, $"{relativePath}: {string.Join(" | ", errors)}");
            if (expectedSkeletonVocabulary != null)
            {
                var rigs = root["extensions"]?["KHR_character_skeleton_mapping"]?["skeletalRigMappings"] as JObject;
                Assert.IsNotNull(rigs?[expectedSkeletonVocabulary],
                    $"{relativePath} must use the expected version-stable skeleton vocabulary URI.");
            }
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
            => Assert.IsNotEmpty(KhrSchemaCheck.Mask(new JObject { ["target"] = 0, ["amount"] = 1.5 }), "amount > 1 must be rejected.");

        [Test]
        public void Mask_ThresholdBelowRange_Rejected()
            => Assert.IsNotEmpty(KhrSchemaCheck.Mask(new JObject { ["target"] = 0, ["threshold"] = -0.1 }), "threshold < 0 must be rejected.");

        [Test]
        public void Mask_EmptyType_Rejected()
            => Assert.IsNotEmpty(KhrSchemaCheck.Mask(new JObject { ["target"] = 0, ["type"] = "" }),
                "an explicitly empty custom mask type must be rejected.");

        [Test]
        public void Mask_InvalidCustomType_Rejected()
            => Assert.IsNotEmpty(KhrSchemaCheck.Mask(new JObject { ["target"] = 0, ["type"] = "soft_block" }),
                "custom mask types must use the glTF vendor-extension token shape.");

        [Test]
        public void Mask_VendorShapedCustomType_Accepted()
            => Assert.IsEmpty(KhrSchemaCheck.Mask(
                    new JObject { ["target"] = 0, ["type"] = "ACME_expression_mask_curve" }),
                "a vendor-shaped custom mask token has defined identity behavior without a companion extension.");

        [Test]
        public void Mask_Valid_Accepted()
            => Assert.IsEmpty(KhrSchemaCheck.Mask(new JObject { ["target"] = 0, ["type"] = "blend", ["amount"] = 0.5, ["threshold"] = 0.0 }));

        [Test]
        public void Mask_StringTarget_Rejected()
            => Assert.IsNotEmpty(KhrSchemaCheck.Mask(new JObject { ["target"] = "aa" }),
                "mask targets must use expression indices, not labels.");

        [Test]
        public void MappingSet_RelativeIdentifier_Rejected()
        {
            var root = JObject.Parse(@"{
                'extensions': {
                    'KHR_character_expression': {
                        'expressions': [ { 'expression': 'smile', 'animation': 0 } ]
                    },
                    'KHR_character_expression_mapping': {
                        'expressionSetMappings': {
                            'ARKit': { 'Smile': [ { 'source': 0, 'weight': 1.0 } ] }
                        }
                    }
                }
            }");

            StringAssert.Contains("not an absolute URI", string.Join(" | ", ExpressionObjectReferences(root)));
        }

        [Test]
        public void InputMapping_OutOfRangeTarget_Rejected()
        {
            var root = JObject.Parse(@"{
                'extensions': {
                    'KHR_character_expression': {
                        'expressions': [ { 'expression': 'smile', 'animation': 0 } ]
                    },
                    'KHR_character_expression_mapping': {
                        'expressionSetInputMappings': {
                            'https://example.com/vocabulary/v1': {
                                'Smile': [ { 'target': 1, 'weight': 1.0 } ]
                            }
                        }
                    }
                }
            }");

            StringAssert.Contains("does not resolve", string.Join(" | ", ExpressionObjectReferences(root)));
        }

        [Test]
        public void JointAssociation_MissingNode_Rejected()
            => Assert.IsNotEmpty(KhrSchemaCheck.JointAssociation(new JObject()),
                "a skeleton joint association must contain a node index.");

        [Test]
        public void JointAssociation_Valid_Accepted()
            => Assert.IsEmpty(KhrSchemaCheck.JointAssociation(
                new JObject { ["node"] = 0, ["name"] = "Hips" }));

        private static List<string> ExpressionChannelReferences(JObject root)
        {
            var errors = new List<string>();
            var expressions = root["extensions"]?["KHR_character_expression"]?["expressions"] as JArray;
            var animations = root["animations"] as JArray;
            if (expressions == null) return errors;

            foreach (var token in expressions)
            {
                if (!(token is JObject expression) || expression["animation"]?.Type != JTokenType.Integer)
                    continue;
                int animationIndex = (int)expression["animation"];
                if (animations == null || animationIndex < 0 || animationIndex >= animations.Count)
                {
                    errors.Add($"expression animation index {animationIndex} does not resolve in animations.");
                    continue;
                }

                var channels = animations[animationIndex]?["channels"] as JArray;
                var extensions = expression["extensions"] as JObject;
                CheckTypedChannels(extensions?["KHR_character_expression_joint"], "joint", channels, errors);
                CheckTypedChannels(extensions?["KHR_character_expression_morphtarget"], "morph", channels, errors);
                CheckTypedChannels(extensions?["KHR_character_expression_texture"], "texture", channels, errors);
            }
            return errors;
        }

        private static List<string> ExpressionObjectReferences(JObject root)
        {
            var errors = new List<string>();
            var expressions = root["extensions"]?["KHR_character_expression"]?["expressions"] as JArray;
            if (expressions == null) return errors;

            foreach (var token in expressions)
            {
                var masks = token?["extensions"]?["KHR_character_expression_mask"]?["masks"] as JArray;
                if (masks == null) continue;
                foreach (var mask in masks)
                {
                    var target = mask?["target"];
                    if (target?.Type != JTokenType.Integer || (int)target < 0 || (int)target >= expressions.Count)
                        errors.Add("mask target does not resolve in KHR_character_expression.expressions.");
                    else if (mask?["name"] != null
                        && (mask["name"].Type != JTokenType.String
                            || (string)mask["name"] != (string)expressions[(int)target]?["expression"]))
                        errors.Add("mask name does not match the target expression label.");
                }
            }

            var mappingExtension = root["extensions"]?["KHR_character_expression_mapping"];
            var mappingSets = mappingExtension?["expressionSetMappings"] as JObject;
            CheckMappingSetIdentifiers(mappingSets, errors);
            if (mappingSets != null)
            {
                foreach (var set in mappingSets.Properties())
                {
                    if (!(set.Value is JObject targets)) continue;
                    foreach (var target in targets.Properties())
                    {
                        if (!(target.Value is JArray sources)) continue;
                        foreach (var source in sources)
                        {
                            var index = source?["source"];
                            if (index?.Type != JTokenType.Integer || (int)index < 0 || (int)index >= expressions.Count)
                                errors.Add("mapping source does not resolve in KHR_character_expression.expressions.");
                            else if (source?["name"] != null
                                && (source["name"].Type != JTokenType.String
                                    || (string)source["name"] != (string)expressions[(int)index]?["expression"]))
                                errors.Add("mapping name does not match the source expression label.");
                        }
                    }
                }
            }

            var inputMappingSets = mappingExtension?["expressionSetInputMappings"] as JObject;
            CheckMappingSetIdentifiers(inputMappingSets, errors);
            if (inputMappingSets != null)
            {
                foreach (var set in inputMappingSets.Properties())
                {
                    if (!(set.Value is JObject commands)) continue;
                    foreach (var command in commands.Properties())
                    {
                        if (!(command.Value is JArray targets)) continue;
                        foreach (var target in targets)
                        {
                            var index = target?["target"];
                            if (index?.Type != JTokenType.Integer || (int)index < 0 || (int)index >= expressions.Count)
                                errors.Add("input mapping target does not resolve in KHR_character_expression.expressions.");
                            else if (target?["name"] != null
                                && (target["name"].Type != JTokenType.String
                                    || (string)target["name"] != (string)expressions[(int)index]?["expression"]))
                                errors.Add("input mapping name does not match the target expression label.");
                        }
                    }
                }
            }
            return errors;
        }

        private static void CheckMappingSetIdentifiers(JObject mappingSets, List<string> errors)
        {
            if (mappingSets == null) return;
            foreach (var set in mappingSets.Properties())
            {
                if (!System.Uri.TryCreate(set.Name, System.UriKind.Absolute, out _))
                    errors.Add($"mapping-set identifier '{set.Name}' is not an absolute URI.");
            }
        }

        private static List<string> SkeletonObjectReferences(JObject root)
        {
            var errors = new List<string>();
            var nodes = root["nodes"] as JArray;
            var rigs = root["extensions"]?["KHR_character_skeleton_mapping"]?["skeletalRigMappings"] as JObject;
            if (rigs == null) return errors;
            CheckMappingSetIdentifiers(rigs, errors);
            foreach (var rig in rigs.Properties())
            {
                if (!(rig.Value is JObject joints)) continue;
                foreach (var joint in joints.Properties())
                {
                    if (!(joint.Value is JObject association))
                    {
                        errors.Add("skeleton joint association must be an object.");
                        continue;
                    }
                    var node = association["node"];
                    if (node?.Type != JTokenType.Integer || nodes == null || (int)node < 0 || (int)node >= nodes.Count)
                    {
                        errors.Add("skeleton association node does not resolve in nodes.");
                        continue;
                    }
                    if (association["name"] != null
                        && (association["name"].Type != JTokenType.String
                            || (string)association["name"] != (string)nodes[(int)node]?["name"]))
                        errors.Add("skeleton association name does not match the referenced node name.");
                }
            }
            return errors;
        }

        private static void CheckTypedChannels(JToken extension, string kind, JArray animationChannels, List<string> errors)
        {
            var indices = extension?["channels"] as JArray;
            if (indices == null) return;
            foreach (var indexToken in indices)
            {
                if (indexToken.Type != JTokenType.Integer || animationChannels == null)
                {
                    errors.Add($"{kind} channel index is invalid.");
                    continue;
                }
                int index = (int)indexToken;
                if (index < 0 || index >= animationChannels.Count)
                {
                    errors.Add($"{kind} channel {index} is outside the selected animation.");
                    continue;
                }

                var target = animationChannels[index]?["target"] as JObject;
                string path = (string)target?["path"];
                string pointer = (string)target?["extensions"]?["KHR_animation_pointer"]?["pointer"];
                if (kind == "joint" && path != "translation" && path != "rotation" && path != "scale")
                    errors.Add($"joint channel {index} must target node TRS.");
                else if (kind == "morph" && path != "weights" && !IsWeightPointer(path, pointer))
                    errors.Add($"morph channel {index} must target weights or a node-weight pointer.");
                else if (kind == "texture" && !IsTextureTransformPointer(path, pointer))
                    errors.Add($"texture channel {index} must target KHR_texture_transform offset, scale, or rotation.");
            }
        }

        private static bool IsWeightPointer(string path, string pointer)
        {
            if (path != "pointer" || string.IsNullOrEmpty(pointer) || !pointer.StartsWith("/nodes/")) return false;
            int weights = pointer.IndexOf("/weights", System.StringComparison.Ordinal);
            if (weights < 0) return false;
            string suffix = pointer.Substring(weights + "/weights".Length);
            if (suffix.Length == 0) return true;
            if (!suffix.StartsWith("/") || suffix.Length == 1) return false;
            return int.TryParse(suffix.Substring(1), out int weightIndex) && weightIndex >= 0;
        }

        private static bool IsTextureTransformPointer(string path, string pointer)
        {
            if (path != "pointer" || string.IsNullOrEmpty(pointer)) return false;
            const string marker = "/extensions/KHR_texture_transform/";
            int start = pointer.LastIndexOf(marker, System.StringComparison.Ordinal);
            if (start < 0) return false;
            string property = pointer.Substring(start + marker.Length);
            return property == "offset" || property == "scale" || property == "rotation";
        }
    }
}
