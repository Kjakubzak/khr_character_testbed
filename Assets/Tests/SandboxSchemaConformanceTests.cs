using System.Collections.Generic;
using System.Text.RegularExpressions;
using NUnit.Framework;
using Newtonsoft.Json.Linq;
using UnityEngine;
using Samples.Shared;

namespace KhrCharacterTestbed.Tests
{
    /// <summary>
    /// Wire conformance gate. A dependency-free, license-clean checker (<see cref="KhrSchemaCheck"/> plus the
    /// cross-reference walkers below) encodes the schema constraints and the structural validation rules of the
    /// KHR_character extension family: marker placement, required entry names, channel listings, single-writer
    /// conflicts, suppression and mapping references. POSITIVE: the committed SC-* and FromBlender wire passes.
    /// NEGATIVE: one malformed payload per rule must be rejected, so a regression that emits an out-of-spec wire is
    /// caught. Rules the specifications make warnings (a blend threshold, a skeleton name mismatch) are collected
    /// separately and never fail the wire.
    /// </summary>
    public class SandboxSchemaConformanceTests
    {
        private const string UnityHumanoidVocabulary =
            "https://example.com/skeleton-vocabularies/unity-humanoid/v1";
        private const string DefaultSkeletonVocabulary =
            "https://example.com/skeleton-vocabularies/default/v1";
        private const string Vrm10HumanoidVocabulary =
            "https://github.com/vrm-c/vrm-specification/blob/70ae16e93abd6da727fdf641b67aa41010c6d933/specification/VRMC_vrm-1.0/humanoid.md";

        private const string JointExtension = "KHR_character_expression_joint";
        private const string MorphExtension = "KHR_character_expression_morphtarget";
        private const string TextureTransformExtension = "KHR_character_expression_texture_transform";
        private const string SuppressionExtension = "KHR_character_expression_suppression";

        private static readonly Regex VendorToken = new Regex("^[A-Z0-9]+_[a-z0-9]+(?:_[a-z0-9]+)*$");
        private static readonly Regex NodeTrsPointer =
            new Regex("^/nodes/(0|[1-9][0-9]*)/(translation|rotation|scale)$");
        private static readonly Regex NodeWeightsPointer = new Regex("^/nodes/(0|[1-9][0-9]*)/weights(/(0|[1-9][0-9]*))?$");
        private static readonly Regex TextureTransformPointer =
            new Regex("^/materials/(0|[1-9][0-9]*)/.+/extensions/KHR_texture_transform/(offset|scale|rotation)$");

        // ── The checker: per-object schema constraints. Each returns the list of violations (empty == OK). ──
        private static class KhrSchemaCheck
        {
            // The marker defines no properties; unknown members, including a legacy rootNode, are not errors.
            public static List<string> Character(JToken ext)
            {
                var e = new List<string>();
                if (!(ext is JObject)) e.Add("KHR_character must be a JSON object.");
                return e;
            }

            public static List<string> ExpressionEntry(JObject entry)
            {
                var e = new List<string>();
                var name = entry["name"];
                if (name == null || name.Type != JTokenType.String || ((string)name).Length < 1)
                    e.Add("expression entry name is required (string, minLength 1).");
                var animation = entry["animation"];
                if (animation == null || animation.Type != JTokenType.Integer || (int)animation < 0)
                    e.Add("expression entry animation is required (integer >= 0).");
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

            // A suppression object has only target, type, amount and threshold; any other member, such as a legacy
            // name, is ignored. A threshold on a blend or omitted type is a warning, never an error.
            public static List<string> Suppression(JObject suppression, List<string> warnings = null)
            {
                var e = new List<string>();
                var target = suppression["target"];
                if (target == null || target.Type != JTokenType.Integer || (int)target < 0)
                    e.Add("suppression.target is required (integer >= 0).");
                var type = suppression["type"];
                string value = null;
                if (type != null)
                {
                    value = type.Type == JTokenType.String ? (string)type : null;
                    if (string.IsNullOrEmpty(value))
                        e.Add("suppression.type must be a non-empty string.");
                    else if (value != "blend" && value != "block" && !VendorToken.IsMatch(value))
                        e.Add("a custom suppression.type must be shaped like a glTF vendor extension name.");
                }
                UnitRangeOptionalNumber(suppression, "amount", e);
                UnitRangeOptionalNumber(suppression, "threshold", e);
                if (suppression["threshold"] != null && (type == null || value == "blend"))
                    warnings?.Add("suppression.threshold has no effect on a blend suppression and should be omitted.");
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
                if (v.Type != JTokenType.Float && v.Type != JTokenType.Integer) { e.Add($"suppression.{key} must be a number."); return; }
                double d = (double)v;
                if (d < 0.0 || d > 1.0) e.Add($"suppression.{key} must be in [0,1].");
            }
        }

        private sealed class WireReport
        {
            public readonly List<string> Errors = new List<string>();
            public readonly List<string> Warnings = new List<string>();
            public readonly List<string> Incomplete = new List<string>();
            public int Character, CameraHint, Lookat, Suppression, JointAssociation, Entries;
        }

        private static WireReport CheckWire(JObject root)
        {
            var report = new WireReport();
            WalkExtensions(root, root, "$", report);
            report.Errors.AddRange(ExpressionEntries(root, report));
            report.Errors.AddRange(ExpressionChannelReferences(root, report.Incomplete));
            report.Errors.AddRange(SingleWriterConflicts(root));
            report.Errors.AddRange(ExpressionObjectReferences(root));
            report.Errors.AddRange(SkeletonObjectReferences(root, report.Warnings));
            return report;
        }

        private static WireReport CheckWire(string json) => CheckWire(JObject.Parse(json));

        // ── POSITIVE: the committed wire conforms ──
        [Test]
        public void RealWire_ConformsToSchemaConstraints(
            [Values("SC-Body.glb", "SC-Degraded.glb", "SC-LookAt.glb", "SC-Face.glb", "SC-FacePlus.glb",
                "SC-ExprEdge.glb", "SC-Partial.glb", "SC-PseudoVRM.glb")] string fixture)
        {
            byte[] glb = System.IO.File.ReadAllBytes(CharacterLoader.SyntheticPath(fixture));
            string json = CharacterLoader.ExtractGltfJson(glb);
            Assert.IsNotNull(json, $"{fixture} should yield a JSON chunk.");
            var root = JObject.Parse(json);
            var report = CheckWire(root);

            Assert.IsEmpty(report.Errors,
                $"{fixture} wire must conform to the KHR_character constraints: {string.Join(" | ", report.Errors)}");
            Assert.IsEmpty(report.Warnings, $"{fixture} should raise no validator warning: {string.Join(" | ", report.Warnings)}");
            Assert.IsEmpty(report.Incomplete, $"{fixture} should be completely checkable: {string.Join(" | ", report.Incomplete)}");
            // Anti-trivial: the relevant fixture must actually carry the payload we claim to have validated.
            Assert.AreEqual(1, report.Character, $"{fixture} must carry exactly one top-level KHR_character marker.");
            if (fixture == "SC-Body.glb") Assert.Greater(report.CameraHint, 0, "SC-Body must carry a camera hint to validate.");
            if (fixture == "SC-LookAt.glb") Assert.Greater(report.Lookat, 0, "SC-LookAt must carry a look-at target to validate.");
            if (fixture == "SC-Face.glb" || fixture == "SC-ExprEdge.glb")
                Assert.Greater(report.Suppression, 0, $"{fixture} must carry an expression suppression to validate.");
            if (fixture == "SC-Face.glb" || fixture == "SC-FacePlus.glb" || fixture == "SC-ExprEdge.glb")
                Assert.Greater(report.Entries, 1, $"{fixture} must carry expression entries to validate.");
            if (fixture == "SC-Body.glb" || fixture == "SC-Degraded.glb")
            {
                Assert.Greater(report.JointAssociation, 0, $"{fixture} must carry skeleton joint associations to validate.");
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

        [TestCase("SampleAssets/FromBlender/minimal.glb", null)]
        [TestCase("SampleAssets/FromBlender/expressions_morph.glb", null)]
        [TestCase("SampleAssets/FromBlender/expressions_joint.glb", null)]
        [TestCase("SampleAssets/FromBlender/expressions_mask.glb", null)]
        [TestCase("SampleAssets/FromBlender/expressions_mapping.glb", null)]
        [TestCase("SampleAssets/FromBlender/node_hints.glb", null)]
        [TestCase("SampleAssets/FromBlender/visibility_hints.glb", null)]
        [TestCase("SampleAssets/FromBlender/full.glb", DefaultSkeletonVocabulary)]
        [TestCase("SampleAssets/FromBlender/skeleton.glb", DefaultSkeletonVocabulary)]
        [TestCase("SampleAssets/FromBlender/skeleton_refpose.glb", DefaultSkeletonVocabulary)]
        [TestCase("SampleAssets/FromBlender/starter.glb", DefaultSkeletonVocabulary)]
        [TestCase("SampleAssets/VRM_KHR_Examples/khr-character-example.glb", Vrm10HumanoidVocabulary)]
        [TestCase("SampleAssets/VRM_KHR_Examples/khr-character-example-always.glb", Vrm10HumanoidVocabulary)]
        [TestCase("SampleAssets/VRM_KHR_Examples/khr-character-example-first-person.glb", Vrm10HumanoidVocabulary)]
        [TestCase("SampleAssets/VRM_KHR_Examples/khr-character-example-third-person.glb", Vrm10HumanoidVocabulary)]
        public void CommittedFixtures_PassWireChecks(string relativePath, string expectedSkeletonVocabulary)
        {
            string path = System.IO.Path.Combine(Application.dataPath, relativePath);
            var root = JObject.Parse(CharacterLoader.ExtractGltfJson(System.IO.File.ReadAllBytes(path)));
            var report = CheckWire(root);
            if (IsHeroFixture(relativePath) && CharacterLoader.PendingReexportDefects(root).Count > 0)
            {
                // D7: until 0b5vr re-exports the hero, its entries carry only the legacy `expression` label (so no
                // diagnostic mapping name can match an entry name) and its look expressions all rotate the two eye
                // nodes. Assert exactly those pending defects; never skip.
                string joined = string.Join(" | ", report.Errors);
                StringAssert.Contains("expression entry name is required", joined, $"{relativePath}: {joined}");
                StringAssert.Contains("single-writer property /nodes/11/rotation", joined, $"{relativePath}: {joined}");
                StringAssert.Contains("single-writer property /nodes/12/rotation", joined, $"{relativePath}: {joined}");
                foreach (var error in report.Errors)
                    Assert.IsTrue(
                        error.Contains("expression entry name is required")
                        || error.Contains("single-writer property")
                        || error.Contains("mapping name does not match"),
                        $"{relativePath}: unexpected wire error beyond the pending hero defects: {error}");
            }
            else
            {
                Assert.IsEmpty(report.Errors, $"{relativePath}: {string.Join(" | ", report.Errors)}");
            }
            Assert.AreEqual(1, report.Character, $"{relativePath} must carry exactly one top-level KHR_character marker.");
            if (expectedSkeletonVocabulary != null)
            {
                var rigs = root["extensions"]?["KHR_character_skeleton_mapping"]?["skeletalRigMappings"] as JObject;
                Assert.IsNotNull(rigs?[expectedSkeletonVocabulary],
                    $"{relativePath} must use the expected version-stable skeleton vocabulary URI.");
            }
        }

        // ── NEGATIVE / boundary: the checker rejects each malformed vector and accepts each valid one ──
        [Test]
        public void Character_EmptyObject_Accepted()
        {
            Assert.IsEmpty(KhrSchemaCheck.Character(new JObject()));
            var report = CheckWire("{ 'extensions': { 'KHR_character': {} } }");
            Assert.IsEmpty(report.Errors);
            Assert.AreEqual(1, report.Character);
        }

        [Test]
        public void Character_LegacyRootNode_NotAnError()
            => Assert.IsEmpty(CheckWire("{ 'extensions': { 'KHR_character': { 'rootNode': 99 } } }").Errors,
                "a legacy rootNode is an unknown member, not an error.");

        [Test]
        public void Character_NonRootPlacement_Rejected()
        {
            var report = CheckWire(@"{
                'extensions': { 'KHR_character': {} },
                'nodes': [ { 'name': 'body', 'extensions': { 'KHR_character': {} } } ]
            }");
            StringAssert.Contains("top-level glTF object", string.Join(" | ", report.Errors));
            Assert.AreEqual(1, report.Character, "only the top-level marker counts.");
        }

        [Test]
        public void Character_NotAnObject_Rejected()
            => Assert.IsNotEmpty(KhrSchemaCheck.Character(new JValue(true)));

        [Test]
        public void Entry_MissingName_Rejected()
        {
            Assert.IsNotEmpty(KhrSchemaCheck.ExpressionEntry(new JObject { ["expression"] = "smile", ["animation"] = 0 }),
                "a legacy expression label is not a name.");
            Assert.IsNotEmpty(KhrSchemaCheck.ExpressionEntry(new JObject { ["name"] = "", ["animation"] = 0 }),
                "an empty name must be rejected.");
        }

        [Test]
        public void Entry_DuplicateName_Accepted()
            => Assert.IsEmpty(CheckWire(ExpressionAsset(@"[
                    { 'name': 'smile', 'animation': 0 },
                    { 'name': 'smile', 'animation': 0 } ]", Animation("{ 'node': 0, 'path': 'translation' }"))).Errors,
                "names need not be unique; array indices are the identities.");

        [Test]
        public void Entry_UnknownMembers_Accepted()
            => Assert.IsEmpty(KhrSchemaCheck.ExpressionEntry(new JObject
            {
                ["name"] = "smile", ["animation"] = 0, ["expression"] = "smile",
                ["extensions"] = new JObject(), ["extras"] = new JObject { ["note"] = 1 },
            }));

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
        public void Suppression_MissingTarget_Rejected()
            => Assert.IsNotEmpty(KhrSchemaCheck.Suppression(new JObject { ["amount"] = 0.5 }),
                "a suppression without a target must be rejected.");

        [Test]
        public void Suppression_AmountAboveRange_Rejected()
            => Assert.IsNotEmpty(KhrSchemaCheck.Suppression(new JObject { ["target"] = 0, ["amount"] = 1.5 }),
                "amount > 1 must be rejected.");

        [Test]
        public void Suppression_ThresholdBelowRange_Rejected()
            => Assert.IsNotEmpty(KhrSchemaCheck.Suppression(new JObject { ["target"] = 0, ["type"] = "block", ["threshold"] = -0.1 }),
                "threshold < 0 must be rejected.");

        [Test]
        public void Suppression_EmptyType_Rejected()
            => Assert.IsNotEmpty(KhrSchemaCheck.Suppression(new JObject { ["target"] = 0, ["type"] = "" }),
                "an explicitly empty custom suppression type must be rejected.");

        [Test]
        public void Suppression_InvalidCustomType_Rejected()
            => Assert.IsNotEmpty(KhrSchemaCheck.Suppression(new JObject { ["target"] = 0, ["type"] = "soft_block" }),
                "custom suppression types must use the glTF vendor-extension token shape.");

        [Test]
        public void Suppression_VendorShapedCustomType_Accepted()
            => Assert.IsEmpty(KhrSchemaCheck.Suppression(
                    new JObject { ["target"] = 0, ["type"] = "ACME_expression_suppression_curve", ["amount"] = 0.1, ["threshold"] = 0.3 }),
                "a vendor-shaped custom token is valid without a companion extension; it then has factor 1.");

        [Test]
        public void Suppression_Valid_Accepted()
        {
            var warnings = new List<string>();
            Assert.IsEmpty(KhrSchemaCheck.Suppression(new JObject { ["target"] = 0, ["type"] = "blend", ["amount"] = 0.5 }, warnings));
            Assert.IsEmpty(KhrSchemaCheck.Suppression(
                new JObject { ["target"] = 0, ["type"] = "block", ["amount"] = 1.0, ["threshold"] = 0.25 }, warnings));
            Assert.IsEmpty(warnings);
        }

        [Test]
        public void Suppression_ThresholdOnBlend_WarnsNotError()
        {
            var warnings = new List<string>();
            Assert.IsEmpty(KhrSchemaCheck.Suppression(
                new JObject { ["target"] = 0, ["type"] = "blend", ["threshold"] = 0.0 }, warnings));
            Assert.IsEmpty(KhrSchemaCheck.Suppression(new JObject { ["target"] = 0, ["threshold"] = 0.4 }, warnings),
                "an omitted type is blend.");
            Assert.AreEqual(2, warnings.Count, "a threshold on a blend or omitted type should be reported as a warning.");
        }

        [Test]
        public void Suppression_NameIsIgnored()
        {
            var report = CheckWire(ExpressionAsset(@"[
                { 'name': 'smile', 'animation': 0 },
                { 'name': 'frown', 'animation': 0, 'extensions': { 'KHR_character_expression_suppression': {
                    'suppressions': [ { 'target': 0, 'name': 'not-smile' } ] } } } ]",
                Animation("{ 'node': 0, 'path': 'translation' }")));
            Assert.IsEmpty(report.Errors, "a suppression name is not a defined member and is never compared.");
            Assert.AreEqual(1, report.Suppression);
        }

        [Test]
        public void Suppression_StringTarget_Rejected()
            => Assert.IsNotEmpty(KhrSchemaCheck.Suppression(new JObject { ["target"] = "aa" }),
                "suppression targets must use expression indices, not labels.");

        [Test]
        public void MappingSet_NonUriIdentifier_Accepted()
        {
            var report = CheckWire(MappingAsset("'ARKit': { 'Smile': [ { 'source': 0, 'weight': 1.0 } ] }, " +
                                                "'relative/v1': { 'Smile': [ { 'source': 0, 'weight': 1.0 } ] }"));
            Assert.IsEmpty(report.Errors, "any nonempty mapping-set identifier is valid; it need not be a URI.");
        }

        [Test]
        public void MappingSet_EmptyIdentifier_Rejected()
            => StringAssert.Contains("identifier must be nonempty",
                string.Join(" | ", CheckWire(MappingAsset("'': { 'Smile': [ { 'source': 0, 'weight': 1.0 } ] }")).Errors));

        [Test]
        public void MappingEndpoint_EmptyKey_Rejected()
            => StringAssert.Contains("endpoint key must be nonempty",
                string.Join(" | ", CheckWire(MappingAsset("'vrm': { '': [ { 'source': 0, 'weight': 1.0 } ] }")).Errors));

        [Test]
        public void MappingName_MismatchWithEntryName_Rejected()
            => StringAssert.Contains("mapping name does not match",
                string.Join(" | ", CheckWire(MappingAsset(
                    "'vrm': { 'Smile': [ { 'source': 0, 'name': 'Smile', 'weight': 1.0 } ] }")).Errors),
                "diagnostic names compare by code point, so 'Smile' does not match the entry name 'smile'.");

        [Test]
        public void MappingName_MatchesEntryName_Accepted()
            => Assert.IsEmpty(CheckWire(MappingAsset(
                "'vrm': { 'Smile': [ { 'source': 0, 'name': 'smile', 'weight': 1.0 } ] }")).Errors);

        [Test]
        public void InputMapping_OutOfRangeTarget_Rejected()
        {
            var root = JObject.Parse(@"{
                'extensions': {
                    'KHR_character_expression': {
                        'expressions': [ { 'name': 'smile', 'animation': 0 } ]
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

        [Test]
        public void SkeletonAssociation_NameMismatch_IsWarningNotError()
        {
            var report = CheckWire(@"{
                'extensions': {
                    'KHR_character': {},
                    'KHR_character_skeleton_mapping': { 'skeletalRigMappings': { 'vrm': {
                        'hips': { 'node': 0, 'name': 'Hips' },
                        'spine': { 'node': 1, 'name': 'Spine' } } } }
                },
                'nodes': [ { 'name': 'Pelvis' }, {} ]
            }");
            Assert.IsEmpty(report.Errors, "a name mismatch, even against an unnamed node, does not invalidate the association.");
            Assert.AreEqual(2, report.Warnings.Count, "both mismatches should be reported as warnings.");
        }

        [Test]
        public void JointChannel_PointerForm_Accepted()
        {
            string asset = ExpressionAsset(
                "[ { 'name': 'nod', 'animation': 0, 'extensions': { 'KHR_character_expression_joint': { 'channels': [0] } } } ]",
                Animation(PointerTarget("/nodes/0/rotation")),
                "'KHR_animation_pointer'");
            Assert.IsEmpty(CheckWire(asset).Errors);

            string undeclared = ExpressionAsset(
                "[ { 'name': 'nod', 'animation': 0, 'extensions': { 'KHR_character_expression_joint': { 'channels': [0] } } } ]",
                Animation(PointerTarget("/nodes/0/rotation")));
            StringAssert.Contains("KHR_animation_pointer", string.Join(" | ", CheckWire(undeclared).Errors),
                "a pointer-form joint channel requires KHR_animation_pointer in extensionsUsed.");
        }

        [Test]
        public void JointChannel_PointerToWeights_Rejected()
            => StringAssert.Contains("joint channel 0",
                string.Join(" | ", CheckWire(ExpressionAsset(
                    "[ { 'name': 'nod', 'animation': 0, 'extensions': { 'KHR_character_expression_joint': { 'channels': [0] } } } ]",
                    Animation(PointerTarget("/nodes/0/weights")),
                    "'KHR_animation_pointer'")).Errors));

        [Test]
        public void Entry_ChannelInTwoDerivedExtensions_Rejected()
            => StringAssert.Contains("more than one derived expression extension",
                string.Join(" | ", CheckWire(ExpressionAsset(
                    "[ { 'name': 'smile', 'animation': 0, 'extensions': { " +
                    "'KHR_character_expression_joint': { 'channels': [0] }, " +
                    "'KHR_character_expression_morphtarget': { 'channels': [0] } } } ]",
                    Animation("{ 'node': 0, 'path': 'weights' }"))).Errors));

        [Test]
        public void Entry_UnknownExtension_MakesValidationIncomplete()
        {
            var report = CheckWire(ExpressionAsset(
                "[ { 'name': 'fx', 'animation': 0, 'extensions': { 'KHR_character_expression_texture': { 'channels': [0] } } } ]",
                Animation("{ 'node': 0, 'path': 'translation' }")));
            Assert.IsEmpty(report.Errors, "an unrecognized entry extension is not a rejection.");
            Assert.IsNotEmpty(report.Incomplete, "its listing and conflict checks are incomplete.");
        }

        [Test]
        public void SingleWriter_RotationOnTwoEntries_Rejected()
        {
            // Entry 0 lists the core form and entry 1 the pointer form of node 0's rotation: one property.
            var report = CheckWire(ExpressionAsset(@"[
                { 'name': 'lookLeft', 'animation': 0, 'extensions': { 'KHR_character_expression_joint': { 'channels': [0] } } },
                { 'name': 'lookRight', 'animation': 1, 'extensions': { 'KHR_character_expression_joint': { 'channels': [0] } } } ]",
                Animation("{ 'node': 0, 'path': 'rotation' }") + ", " + Animation(PointerTarget("/nodes/0/rotation")),
                "'KHR_animation_pointer'"));
            StringAssert.Contains("single-writer property /nodes/0/rotation is listed by expression entries 0, 1",
                string.Join(" | ", report.Errors));
        }

        [Test]
        public void SingleWriter_TextureScaleOnTwoEntries_Rejected()
        {
            const string pointer = "/materials/0/pbrMetallicRoughness/baseColorTexture/extensions/KHR_texture_transform/scale";
            var report = CheckWire(ExpressionAsset(@"[
                { 'name': 'a', 'animation': 0, 'extensions': { 'KHR_character_expression_texture_transform': { 'channels': [0] } } },
                { 'name': 'b', 'animation': 1, 'extensions': { 'KHR_character_expression_texture_transform': { 'channels': [0] } } } ]",
                Animation(PointerTarget(pointer)) + ", " + Animation(PointerTarget(pointer)),
                "'KHR_animation_pointer', 'KHR_texture_transform'"));
            StringAssert.Contains($"single-writer property {pointer}", string.Join(" | ", report.Errors));
        }

        [Test]
        public void SingleWriter_TranslationOverlap_Accepted()
        {
            var report = CheckWire(ExpressionAsset(@"[
                { 'name': 'up', 'animation': 0, 'extensions': { 'KHR_character_expression_joint': { 'channels': [0] } } },
                { 'name': 'left', 'animation': 1, 'extensions': { 'KHR_character_expression_joint': { 'channels': [0] } } },
                { 'name': 'smile', 'animation': 2 },
                { 'name': 'frown', 'animation': 2 } ]",
                Animation("{ 'node': 0, 'path': 'translation' }") + ", " +
                Animation("{ 'node': 0, 'path': 'translation' }") + ", " +
                Animation("{ 'node': 0, 'path': 'rotation' }")));
            Assert.IsEmpty(report.Errors,
                "translation is additive, and an unlisted rotation channel has no possible response records.");
        }

        // ── Cross-reference walkers ──

        private static void WalkExtensions(JToken token, JObject root, string path, WireReport report)
        {
            if (token is JObject o)
            {
                if (o["extensions"] is JObject exts)
                {
                    var marker = exts["KHR_character"];
                    if (marker != null)
                    {
                        if (ReferenceEquals(o, root))
                        {
                            report.Character++;
                            report.Errors.AddRange(KhrSchemaCheck.Character(marker));
                        }
                        else
                        {
                            report.Errors.Add($"KHR_character must be attached to the top-level glTF object, not {path}.");
                        }
                    }
                    if (exts["KHR_node_camera_hint"] is JObject ch)
                    {
                        report.CameraHint++;
                        report.Errors.AddRange(KhrSchemaCheck.CameraHint(ch));
                    }
                    if (exts["KHR_node_lookat_target"] is JObject lt)
                    {
                        report.Lookat++;
                        report.Errors.AddRange(KhrSchemaCheck.LookatTarget(lt));
                    }
                    if (exts[SuppressionExtension] != null)
                    {
                        if (!(exts[SuppressionExtension]?["suppressions"] is JArray suppressions) || suppressions.Count == 0)
                            report.Errors.Add($"{SuppressionExtension}.suppressions must be a nonempty array.");
                        else
                            foreach (var s in suppressions)
                                if (s is JObject so)
                                {
                                    report.Suppression++;
                                    report.Errors.AddRange(KhrSchemaCheck.Suppression(so, report.Warnings));
                                }
                                else report.Errors.Add("a suppression must be an object.");
                    }
                    if (exts["KHR_character_skeleton_mapping"]?["skeletalRigMappings"] is JObject rigs)
                        foreach (var rig in rigs.Properties())
                            if (rig.Value is JObject joints)
                                foreach (var joint in joints.Properties())
                                    if (joint.Value is JObject association)
                                    {
                                        report.JointAssociation++;
                                        report.Errors.AddRange(KhrSchemaCheck.JointAssociation(association));
                                    }
                }
                // Extras hold application data, not extension placements.
                foreach (var p in o.Properties())
                    if (p.Name != "extras") WalkExtensions(p.Value, root, $"{path}.{p.Name}", report);
            }
            else if (token is JArray a)
            {
                for (int i = 0; i < a.Count; i++) WalkExtensions(a[i], root, $"{path}[{i}]", report);
            }
        }

        private static List<string> ExpressionEntries(JObject root, WireReport report)
        {
            var errors = new List<string>();
            var extension = root["extensions"]?["KHR_character_expression"];
            if (extension == null) return errors;
            if (!(extension["expressions"] is JArray expressions) || expressions.Count == 0)
            {
                errors.Add("KHR_character_expression.expressions must be a nonempty array.");
                return errors;
            }
            for (int index = 0; index < expressions.Count; index++)
            {
                if (!(expressions[index] is JObject entry))
                {
                    errors.Add($"expression entry {index} must be an object.");
                    continue;
                }
                report.Entries++;
                foreach (var error in KhrSchemaCheck.ExpressionEntry(entry))
                    errors.Add($"{error} (entry {index})");
            }
            return errors;
        }

        private static List<string> ExpressionChannelReferences(JObject root, List<string> incomplete)
        {
            var errors = new List<string>();
            var expressions = root["extensions"]?["KHR_character_expression"]?["expressions"] as JArray;
            var animations = root["animations"] as JArray;
            if (expressions == null) return errors;

            bool pointerFormJoint = false;
            for (int entryIndex = 0; entryIndex < expressions.Count; entryIndex++)
            {
                if (!(expressions[entryIndex] is JObject expression) || expression["animation"]?.Type != JTokenType.Integer)
                    continue;
                int animationIndex = (int)expression["animation"];
                if (animations == null || animationIndex < 0 || animationIndex >= animations.Count)
                {
                    errors.Add($"expression animation index {animationIndex} does not resolve in animations.");
                    continue;
                }

                var channels = animations[animationIndex]?["channels"] as JArray;
                var extensions = expression["extensions"] as JObject;
                var listedBy = new Dictionary<int, string>();
                pointerFormJoint |= CheckTypedChannels(extensions?[JointExtension], "joint", channels, listedBy, entryIndex, errors);
                CheckTypedChannels(extensions?[MorphExtension], "morph", channels, listedBy, entryIndex, errors);
                CheckTypedChannels(extensions?[TextureTransformExtension], "texture", channels, listedBy, entryIndex, errors);
                if (extensions == null) continue;
                foreach (var property in extensions.Properties())
                    if (property.Name != JointExtension && property.Name != MorphExtension
                        && property.Name != TextureTransformExtension && property.Name != SuppressionExtension)
                        incomplete.Add($"entry {entryIndex} carries the unrecognized extension '{property.Name}', so its " +
                                       "listing and conflict checks are incomplete.");
            }

            var used = root["extensionsUsed"] as JArray;
            bool declaresPointer = false;
            if (used != null)
                foreach (var token in used)
                    declaresPointer |= token.Type == JTokenType.String && (string)token == "KHR_animation_pointer";
            if (pointerFormJoint && !declaresPointer)
                errors.Add("a pointer-form joint channel requires KHR_animation_pointer in extensionsUsed.");
            return errors;
        }

        // Validates one derived extension's listing and records which extension listed each channel. Returns true when
        // a joint listing uses the KHR_animation_pointer form.
        private static bool CheckTypedChannels(
            JToken extension,
            string kind,
            JArray animationChannels,
            Dictionary<int, string> listedBy,
            int entryIndex,
            List<string> errors)
        {
            if (extension == null) return false;
            if (!(extension["channels"] is JArray indices) || indices.Count == 0)
            {
                errors.Add($"{kind} channels must be a nonempty array (entry {entryIndex}).");
                return false;
            }
            bool pointerForm = false;
            var seen = new HashSet<int>();
            foreach (var indexToken in indices)
            {
                if (indexToken.Type != JTokenType.Integer || animationChannels == null)
                {
                    errors.Add($"{kind} channel index is invalid (entry {entryIndex}).");
                    continue;
                }
                int index = (int)indexToken;
                if (!seen.Add(index))
                {
                    errors.Add($"{kind} channel {index} is listed twice (entry {entryIndex}).");
                    continue;
                }
                if (listedBy.TryGetValue(index, out var other))
                    errors.Add($"channel {index} is listed by more than one derived expression extension ({other} and {kind}, entry {entryIndex}).");
                else
                    listedBy.Add(index, kind);
                if (index < 0 || index >= animationChannels.Count)
                {
                    errors.Add($"{kind} channel {index} is outside the selected animation (entry {entryIndex}).");
                    continue;
                }

                var target = animationChannels[index]?["target"] as JObject;
                string path = (string)target?["path"];
                string pointer = PointerOf(target);
                if (kind == "joint")
                {
                    bool core = path == "translation" || path == "rotation" || path == "scale";
                    bool viaPointer = path == "pointer" && pointer != null && NodeTrsPointer.IsMatch(pointer);
                    if (!core && !viaPointer)
                        errors.Add($"joint channel {index} must target node translation, rotation or scale (entry {entryIndex}).");
                    pointerForm |= viaPointer;
                }
                else if (kind == "morph" && path != "weights" && !(path == "pointer" && pointer != null && NodeWeightsPointer.IsMatch(pointer)))
                    errors.Add($"morph channel {index} must target weights or a node-weight pointer (entry {entryIndex}).");
                else if (kind == "texture" && !(path == "pointer" && pointer != null && TextureTransformPointer.IsMatch(pointer)))
                    errors.Add($"texture channel {index} must target KHR_texture_transform offset, scale, or rotation (entry {entryIndex}).");
            }
            return pointerForm;
        }

        // Single-writer properties covered by two entries' listed channels make the asset invalid: a node's rotation
        // and its scale (core and pointer forms are one property) and a KHR_texture_transform scale.
        private static List<string> SingleWriterConflicts(JObject root)
        {
            var errors = new List<string>();
            var expressions = root["extensions"]?["KHR_character_expression"]?["expressions"] as JArray;
            var animations = root["animations"] as JArray;
            if (expressions == null || animations == null) return errors;

            var writers = new SortedDictionary<string, List<int>>(System.StringComparer.Ordinal);
            for (int entryIndex = 0; entryIndex < expressions.Count; entryIndex++)
            {
                if (!(expressions[entryIndex] is JObject expression) || expression["animation"]?.Type != JTokenType.Integer)
                    continue;
                int animationIndex = (int)expression["animation"];
                if (animationIndex < 0 || animationIndex >= animations.Count) continue;
                var channels = animations[animationIndex]?["channels"] as JArray;
                if (channels == null) continue;

                var properties = new HashSet<string>(System.StringComparer.Ordinal);
                foreach (var name in new[] { JointExtension, MorphExtension, TextureTransformExtension })
                {
                    if (!(expression["extensions"]?[name]?["channels"] is JArray indices)) continue;
                    foreach (var token in indices)
                    {
                        if (token.Type != JTokenType.Integer || (int)token < 0 || (int)token >= channels.Count) continue;
                        string property = SingleWriterProperty(channels[(int)token]?["target"] as JObject);
                        if (property != null) properties.Add(property);
                    }
                }
                foreach (var property in properties)
                {
                    if (!writers.TryGetValue(property, out var entries)) writers.Add(property, entries = new List<int>());
                    entries.Add(entryIndex);
                }
            }
            foreach (var pair in writers)
                if (pair.Value.Count > 1)
                    errors.Add($"single-writer property {pair.Key} is listed by expression entries {string.Join(", ", pair.Value)}.");
            return errors;
        }

        private static string SingleWriterProperty(JObject target)
        {
            if (target == null) return null;
            string path = (string)target["path"];
            if ((path == "rotation" || path == "scale") && target["node"]?.Type == JTokenType.Integer)
                return $"/nodes/{(int)target["node"]}/{path}";
            string pointer = path == "pointer" ? PointerOf(target) : null;
            if (pointer == null) return null;
            var trs = NodeTrsPointer.Match(pointer);
            if (trs.Success) return trs.Groups[2].Value == "translation" ? null : pointer;
            var texture = TextureTransformPointer.Match(pointer);
            return texture.Success && texture.Groups[2].Value == "scale" ? pointer : null;
        }

        private static string PointerOf(JObject target)
            => target?["extensions"]?["KHR_animation_pointer"]?["pointer"] is JValue value && value.Type == JTokenType.String
                ? (string)value
                : null;

        private static List<string> ExpressionObjectReferences(JObject root)
        {
            var errors = new List<string>();
            var expressions = root["extensions"]?["KHR_character_expression"]?["expressions"] as JArray;
            if (expressions == null) return errors;

            foreach (var token in expressions)
            {
                var suppressions = token?["extensions"]?[SuppressionExtension]?["suppressions"] as JArray;
                if (suppressions == null) continue;
                foreach (var suppression in suppressions)
                {
                    var target = suppression?["target"];
                    if (target?.Type != JTokenType.Integer || (int)target < 0 || (int)target >= expressions.Count)
                        errors.Add("suppression target does not resolve in KHR_character_expression.expressions.");
                }
            }

            var mappingExtension = root["extensions"]?["KHR_character_expression_mapping"];
            CheckMappingSets(mappingExtension?["expressionSetMappings"] as JObject, "source", "mapping", expressions, errors);
            CheckMappingSets(mappingExtension?["expressionSetInputMappings"] as JObject, "target", "input mapping", expressions, errors);
            return errors;
        }

        private static void CheckMappingSets(
            JObject mappingSets, string indexKey, string label, JArray expressions, List<string> errors)
        {
            CheckMappingSetIdentifiers(mappingSets, errors);
            if (mappingSets == null) return;
            foreach (var set in mappingSets.Properties())
            {
                if (!(set.Value is JObject endpoints) || !endpoints.HasValues)
                {
                    errors.Add($"{label} set '{set.Name}' must contain at least one endpoint key.");
                    continue;
                }
                foreach (var endpoint in endpoints.Properties())
                {
                    if (endpoint.Name.Length == 0) errors.Add($"{label} endpoint key must be nonempty.");
                    if (!(endpoint.Value is JArray entries)) continue;
                    foreach (var entry in entries)
                    {
                        var index = entry?[indexKey];
                        if (index?.Type != JTokenType.Integer || (int)index < 0 || (int)index >= expressions.Count)
                            errors.Add($"{label} {indexKey} does not resolve in KHR_character_expression.expressions.");
                        else if (entry?["name"] != null && !NameMatchesEntry(entry["name"], expressions[(int)index]))
                            errors.Add($"{label} name does not match the name of expression entry {(int)index}.");
                    }
                }
            }
        }

        // Diagnostic names compare by code point with the entry name; a legacy `expression` label is not a name.
        private static bool NameMatchesEntry(JToken name, JToken expression)
            => name.Type == JTokenType.String
               && expression is JObject entry
               && entry["name"]?.Type == JTokenType.String
               && string.Equals((string)name, (string)entry["name"], System.StringComparison.Ordinal);

        private static bool IsHeroFixture(string relativePath)
            => relativePath == CharacterLoader.HeroRelativePath
               || System.Array.IndexOf(CharacterLoader.HeroVariantRelativePaths, relativePath) >= 0;

        private static void CheckMappingSetIdentifiers(JObject mappingSets, List<string> errors)
        {
            if (mappingSets == null) return;
            foreach (var set in mappingSets.Properties())
                if (set.Name.Length == 0)
                    errors.Add("mapping-set identifier must be nonempty.");
        }

        private static List<string> SkeletonObjectReferences(JObject root, List<string> warnings = null)
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
                    if (joint.Name.Length == 0) errors.Add("skeleton role key must be nonempty.");
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
                    if (association["name"]?.Type == JTokenType.String
                        && !string.Equals((string)association["name"], (string)nodes[(int)node]?["name"], System.StringComparison.Ordinal))
                        warnings?.Add($"skeleton association '{joint.Name}' name does not match the name of node {(int)node}.");
                }
            }
            return errors;
        }

        // ── In-memory fixture builders ──

        private static string ExpressionAsset(string expressions, string animations, string extraUsed = null)
            => "{ 'extensionsUsed': [ 'KHR_character', 'KHR_character_expression'" +
               (extraUsed != null ? ", " + extraUsed : string.Empty) + " ], " +
               "'extensions': { 'KHR_character': {}, 'KHR_character_expression': { 'expressions': " + expressions + " } }, " +
               "'nodes': [ { 'name': 'n0' } ], " +
               "'animations': [ " + animations + " ] }";

        private static string Animation(string target)
            => "{ 'channels': [ { 'sampler': 0, 'target': " + target + " } ], 'samplers': [ { 'input': 0, 'output': 1 } ] }";

        private static string PointerTarget(string pointer)
            => "{ 'path': 'pointer', 'extensions': { 'KHR_animation_pointer': { 'pointer': '" + pointer + "' } } }";

        private static string MappingAsset(string sets)
            => "{ 'extensions': { 'KHR_character': {}, " +
               "'KHR_character_expression': { 'expressions': [ { 'name': 'smile', 'animation': 0 } ] }, " +
               "'KHR_character_expression_mapping': { 'expressionSetMappings': { " + sets + " } } }, " +
               "'animations': [ " + Animation("{ 'node': 0, 'path': 'translation' }") + " ], 'nodes': [ {} ] }";
    }
}
