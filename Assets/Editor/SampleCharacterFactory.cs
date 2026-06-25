using System.Collections.Generic;
using System.IO;
using System.Text;
using Newtonsoft.Json.Linq;
using Samples.Shared;
using UnityEditor;
using UnityEngine;
using UnityGLTF;
using UnityGLTF.KhrCharacter;
using UnityGLTF.Plugins;

namespace Samples.Editor
{
    /// <summary>
    /// Builds the synthetic SC-* sample characters entirely in code and dogfood-exports them to GLB through the KHR
    /// Character export plugin. Fully license-clean (CC0/synthetic). Each generator is a plain static method so CI
    /// and tests can call it headlessly; export of the animation channels requires the editor, so this lives in the
    /// editor assembly.
    ///
    /// - SC-Face       : head mesh + 6 morph expressions (incl. lookL/R/U/D) + a jaw joint.
    /// - SC-FacePlus   : SC-Face + a texture expression (UV offset + 2-texture index-swap) on a distinct material.
    /// - SC-Body       : a T-pose humanoid-mappable skeleton + skeleton mapping + TPose reference pose + a camera hint.
    /// - SC-LookAt     : a KHR_character root + GazeSolver AuthoredTargets that mark nodes as KHR_node_lookat_target.
    /// - SC-Partial    : a KHR_character root with ONLY a morph expression (graceful-degradation: no skeleton/camera/lookat).
    /// - SC-PseudoVRM  : SC-Partial post-processed to carry synthetic VRMC_* vendor tokens (always-on neutralization gate).
    /// - SC-ExprEdge   : two morph expressions where one BLOCK-masks the other (mask-domain edge: block vs blend).
    /// </summary>
    public static class SampleCharacterFactory
    {
        private const string DefaultOutputDirectory = "Assets/SampleAssets/Synthetic";

        // Blendshape order defines the blendshape index each MorphDriver targets. "smile" (index 6) is added by
        // BuildHeadMesh after these six and is driven by the custom "happy" expression (not one of these auto-tracks).
        private static readonly string[] BlendShapeNames =
            { "jawOpen", "blink", "lookLeft", "lookRight", "lookUp", "lookDown" };
        private const int SmileBlendShapeIndex = 6;

        // ── Public entry points ──────────────────────────────────────────────

        /// <summary>Build SC-Face and export it to <paramref name="outputDirectory"/>/SC-Face.glb.</summary>
        public static string GenerateSCFace(string outputDirectory)
        {
            outputDirectory = Normalize(outputDirectory);
            var temps = new List<Object>();
            try
            {
                var mesh = BuildHeadMesh();
                temps.Add(mesh);
                var material = CreateSkinMaterial();
                if (material != null) temps.Add(material);

                var root = AssembleFaceCharacter("SC-Face", mesh, material, includeTexture: false, temps);
                temps.Add(root);

                return ExportAndImport(root, outputDirectory, "SC-Face");
            }
            finally { Cleanup(temps); }
        }

        /// <summary>Build SC-FacePlus (SC-Face + joint + texture) and export it to SC-FacePlus.glb.</summary>
        public static string GenerateSCFacePlus(string outputDirectory)
        {
            outputDirectory = Normalize(outputDirectory);
            var temps = new List<Object>();
            try
            {
                var mesh = BuildHeadMesh();
                temps.Add(mesh);
                var material = CreateSkinMaterial();
                if (material != null) temps.Add(material);

                var root = AssembleFaceCharacter("SC-FacePlus", mesh, material, includeTexture: true, temps);
                temps.Add(root);

                return ExportAndImport(root, outputDirectory, "SC-FacePlus");
            }
            finally { Cleanup(temps); }
        }

        /// <summary>Build SC-Body (humanoid skeleton + reference pose + camera hint) and export it to SC-Body.glb.</summary>
        public static string GenerateSCBody(string outputDirectory)
        {
            outputDirectory = Normalize(outputDirectory);
            var temps = new List<Object>();
            try
            {
                var root = AssembleBodyCharacter(temps);
                temps.Add(root);

                return ExportAndImport(root, outputDirectory, "SC-Body");
            }
            finally { Cleanup(temps); }
        }

        /// <summary>Build SC-LookAt (KHR_node_lookat_target authored focus points) and export it to SC-LookAt.glb.</summary>
        public static string GenerateSCLookAt(string outputDirectory)
        {
            outputDirectory = Normalize(outputDirectory);
            var temps = new List<Object>();
            try
            {
                var root = AssembleLookAtCharacter(temps);
                temps.Add(root);

                return ExportAndImport(root, outputDirectory, "SC-LookAt");
            }
            finally { Cleanup(temps); }
        }

        /// <summary>Build SC-Partial (KHR_character + a single morph expression only) and export it to SC-Partial.glb.</summary>
        public static string GenerateSCPartial(string outputDirectory)
        {
            outputDirectory = Normalize(outputDirectory);
            var temps = new List<Object>();
            try
            {
                var root = AssemblePartialCharacter(temps);
                temps.Add(root);

                return ExportAndImport(root, outputDirectory, "SC-Partial");
            }
            finally { Cleanup(temps); }
        }

        /// <summary>
        /// Build SC-PseudoVRM: a real, importable KHR_character (the SC-Partial body) whose exported GLB is then
        /// post-processed to inject synthetic <c>VRMC_*</c> vendor tokens into extensionsUsed + a stub root extension,
        /// so it reads like a VRM-origin asset. CC0/synthetic - NOT a real VRM. Used by the always-on neutralization
        /// gate (source carries VRMC_*; a KHR re-export drops them).
        /// </summary>
        public static string GenerateSCPseudoVRM(string outputDirectory)
        {
            outputDirectory = Normalize(outputDirectory);
            var temps = new List<Object>();
            try
            {
                var root = AssemblePartialCharacter(temps);
                temps.Add(root);

                string path = Export(root, outputDirectory, "SC-PseudoVRM");
                InjectVendorExtensions(path, "VRMC_vrm", "VRMC_springBone");
                ImportIfUnderAssets(path);
                return path;
            }
            finally { Cleanup(temps); }
        }

        /// <summary>Build SC-ExprEdge (two morph expressions where one BLOCK-masks the other) and export to SC-ExprEdge.glb.</summary>
        public static string GenerateSCExprEdge(string outputDirectory)
        {
            outputDirectory = Normalize(outputDirectory);
            var temps = new List<Object>();
            try
            {
                var root = AssembleExprEdgeCharacter(temps);
                temps.Add(root);

                return ExportAndImport(root, outputDirectory, "SC-ExprEdge");
            }
            finally { Cleanup(temps); }
        }

        // ── Face assembly (SC-Face / SC-FacePlus) ────────────────────────────

        private static GameObject AssembleFaceCharacter(string rootName, Mesh mesh, Material material, bool includeTexture, List<Object> temps)
        {
            var root = new GameObject(rootName) { hideFlags = HideFlags.HideAndDontSave };
            root.AddComponent<KhrCharacter>();

            var faceGo = new GameObject("Face") { hideFlags = HideFlags.HideAndDontSave };
            faceGo.transform.SetParent(root.transform, false);
            var smr = faceGo.AddComponent<SkinnedMeshRenderer>();
            smr.sharedMesh = mesh;
            if (material != null) smr.sharedMaterial = material;
            smr.localBounds = mesh.bounds;

            var jawGo = new GameObject("Jaw") { hideFlags = HideFlags.HideAndDontSave };
            jawGo.transform.SetParent(root.transform, false);
            jawGo.transform.localPosition = new Vector3(0f, -0.2f, 0.2f);

            // N8 (eye-aim): named eye bones for an EyeAimConstraint demo to rotate (rig markers; no skinned eye geometry).
            AddEyeBone("LeftEye", new Vector3(-0.15f, 0.22f, 0.35f), root.transform);
            AddEyeBone("RightEye", new Vector3(0.15f, 0.22f, 0.35f), root.transform);

            var tracks = BuildFaceTracks(smr, jawGo.transform);
            if (includeTexture)
                tracks.Add(BuildTextureExpression(root.transform, temps));

            var set = new CharacterExpressionSet
            {
                Expressions = tracks.ToArray(),
                MappingSets = BuildMappingSets(tracks),
            };
            root.AddComponent<ExpressionController>().Initialize(set);
            return root;
        }

        // One morph driver per blendshape; the "jawOpen" track additionally drives the jaw bone (Morph | Joint).
        private static List<ExpressionTrack> BuildFaceTracks(SkinnedMeshRenderer smr, Transform jaw)
        {
            var tracks = new List<ExpressionTrack>();
            for (int i = 0; i < BlendShapeNames.Length; i++)
                tracks.Add(MorphTrack(BlendShapeNames[i], smr, i));

            tracks[0].Domains = ExpressionDomain.Morph | ExpressionDomain.Joint;
            tracks[0].JointDrivers = new[]
            {
                new JointDriver
                {
                    Target = jaw,
                    Channel = TrsChannel.Rotation,
                    Sampler = new Sampler { Times = new[] { 0f }, Interp = Interp.Step, SingleKey = true },
                    DeltaQuat = new[] { Quaternion.Euler(18f, 0f, 0f) },
                    BaseQuat = Quaternion.identity,
                },
            };

            // N1 (additive vs override): "aa" drives the SAME blendshape as "jawOpen" (index 0), so a demo can show
            // their additive sum vs the override winner. A higher Priority makes "aa" win in Override mode.
            int jawOpenIndex = tracks.FindIndex(t => t.Name == "jawOpen");
            var aa = MorphTrack("aa", smr, jawOpenIndex >= 0 ? jawOpenIndex : 0);
            aa.MorphDrivers[0].Priority = 1;
            // Half-strength + LINEAR so the overlap with "jawOpen" is demonstrable: Additive sums (and saturates)
            // while Override takes the winner (aa, higher Priority). With equal STEP drivers the two are identical.
            aa.MorphDrivers[0].Sampler = new Sampler { Times = new[] { 0f, 1f }, Interp = Interp.Linear, SingleKey = false };
            aa.MorphDrivers[0].DeltaValues = new[] { 0f, 0.6f };
            int aaIndex = tracks.Count;
            tracks.Add(aa);

            // N2 (mask): "happy" drives the "smile" blendshape (index 6) AND blend-masks "aa" -> emits
            // KHR_character_expression_mask on the "happy" expression item.
            var happy = MorphTrack("happy", smr, SmileBlendShapeIndex);
            int happyIndex = tracks.Count;
            happy.Masks = new[]
            {
                new MaskEntry { TargetIndex = aaIndex, SourceIndex = happyIndex, Type = MaskType.Blend, Amount = 1f },
            };
            tracks.Add(happy);

            return tracks;
        }

        private static ExpressionTrack MorphTrack(string name, SkinnedMeshRenderer smr, int blendShapeIndex)
        {
            return new ExpressionTrack
            {
                Name = name,
                Domains = ExpressionDomain.Morph,
                MorphDrivers = new[]
                {
                    new MorphDriver
                    {
                        Smr = smr,
                        BlendShapeIndex = blendShapeIndex,
                        BaseValue = 0f,
                        Sampler = new Sampler { Times = new[] { 0f, 1f }, Interp = Interp.Step, SingleKey = false },
                        DeltaValues = new[] { 0f, 1f },
                    },
                },
            };
        }

        private static void AddEyeBone(string name, Vector3 localPosition, Transform parent)
        {
            var go = new GameObject(name) { hideFlags = HideFlags.HideAndDontSave };
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPosition;
        }

        // N2 (vocabulary mapping): a "demoVocabulary" set whose "Smile" target maps to two morph expressions
        // (happy + aa) -> emits KHR_character_expression_mapping keyed by the set name.
        private static ExpressionMappingSet[] BuildMappingSets(List<ExpressionTrack> tracks)
        {
            int happyIndex = tracks.FindIndex(t => t.Name == "happy");
            int aaIndex = tracks.FindIndex(t => t.Name == "aa");
            if (happyIndex < 0 || aaIndex < 0) return null;
            return new[]
            {
                new ExpressionMappingSet
                {
                    SetName = "demoVocabulary",
                    Targets = new[]
                    {
                        new MappingTarget
                        {
                            TargetName = "Smile",
                            Contributions = new[]
                            {
                                new MappingContribution { SourceIndex = happyIndex, Weight = 1f },
                                new MappingContribution { SourceIndex = aaIndex, Weight = 0.5f },
                            },
                        },
                    },
                },
            };
        }

        // Builds a separate quad renderer with its OWN material (distinct material per renderer, caveat C6) and a
        // texture expression carrying BOTH a UV-offset driver and a 2-texture index-swap driver (STEP, snaps at
        // 0.5) on that material's base-color slot. Mirrors the plugin's export-test texture-driver authoring.
        private static ExpressionTrack BuildTextureExpression(Transform parent, List<Object> temps)
        {
            var quad = new GameObject("TexturePanel", typeof(MeshFilter), typeof(MeshRenderer))
            { hideFlags = HideFlags.HideAndDontSave };
            quad.transform.SetParent(parent, false);
            quad.transform.localPosition = new Vector3(0f, 0f, 0.55f);
            quad.transform.localScale = Vector3.one * 0.4f;

            var quadMesh = BuildQuadMesh();
            temps.Add(quadMesh);
            quad.GetComponent<MeshFilter>().sharedMesh = quadMesh;

            var swapA = MakeReadableTexture("swapA", new Color(0.9f, 0.3f, 0.3f, 1f), temps);
            var swapB = MakeReadableTexture("swapB", new Color(0.3f, 0.7f, 0.9f, 1f), temps);

            var renderer = quad.GetComponent<MeshRenderer>();
            // Use a LIT material (URP Lit or Built-in Standard) rather than Unlit so UnityGLTF does NOT emit
            // KHR_materials_unlit as a REQUIRED extension -> SC-FacePlus extensionsRequired stays empty (neutral).
            // The base-color texture property name differs per pipeline (_BaseMap on URP, _MainTex on Built-in),
            // so detect it and point the texture drivers at it.
            string baseProperty = "_MainTex";
            var shader = RenderPipelineUtil.LitShader();
            if (shader != null)
            {
                var material = new Material(shader) { name = "SC-FacePlus-Tex", hideFlags = HideFlags.HideAndDontSave };
                baseProperty = material.HasProperty("_BaseMap") ? "_BaseMap" : "_MainTex";
                if (material.HasProperty(baseProperty)) material.SetTexture(baseProperty, swapA);
                renderer.sharedMaterial = material;
                temps.Add(material);
            }

            var uvDriver = new TextureDriver
            {
                Renderer = renderer,
                SubmeshSlot = 0,
                Kind = TexKind.UvTransform,
                PropertyId = Shader.PropertyToID(baseProperty + "_ST"),
                PropertyName = baseProperty,
                GltfTextureSlot = "pbrMetallicRoughness/baseColorTexture",
                Sampler = new Sampler { Times = new[] { 0f, 1f }, Interp = Interp.Linear, SingleKey = false },
                StValues = new[] { Vector4.zero, new Vector4(0f, 0f, 1f, 0f) },
                BaseSt = new Vector4(1f, 1f, 0f, 0f),
            };

            var swapDriver = new TextureDriver
            {
                Renderer = renderer,
                SubmeshSlot = 0,
                Kind = TexKind.IndexSwap,
                PropertyId = Shader.PropertyToID(baseProperty),
                PropertyName = baseProperty,
                GltfTextureSlot = "pbrMetallicRoughness/baseColorTexture",
                Sampler = new Sampler { Times = new[] { 0f, 1f }, Interp = Interp.Step, SingleKey = false },
                SwapTextures = new Texture[] { swapA, swapB },
            };

            return new ExpressionTrack
            {
                Name = "texFx",
                Domains = ExpressionDomain.Texture,
                TextureDrivers = new[] { uvDriver, swapDriver },
            };
        }

        // ── Body assembly (SC-Body) ──────────────────────────────────────────

        // Builds a minimal but complete humanoid T-pose skeleton (all Unity-required bones plus chest/neck/
        // shoulders), authors KHR_character_skeleton_mapping (canonical-joint -> bone) with a TPose reference pose,
        // and adds a "portrait" camera hint. Bone node names are unique + neutral (caveats C7/C11). No skinned mesh
        // is attached (bones only); the wire still carries the skeleton/pose/hint extensions.
        private static GameObject AssembleBodyCharacter(List<Object> temps)
        {
            var root = new GameObject("SC-Body") { hideFlags = HideFlags.HideAndDontSave };
            root.AddComponent<KhrCharacter>();

            var bones = new Dictionary<string, Transform>();

            Transform Bone(string vocab, string nodeName, Transform parent, Vector3 localPos)
            {
                var go = new GameObject(nodeName) { hideFlags = HideFlags.HideAndDontSave };
                go.transform.SetParent(parent, false);
                go.transform.localPosition = localPos;
                go.transform.localRotation = Quaternion.identity;
                go.transform.localScale = Vector3.one;
                bones[vocab] = go.transform;
                return go.transform;
            }

            // Up = +Y, forward = +Z, character-right = +X. Arms extend along +/-X (T-pose), legs down -Y.
            var hips = Bone("hips", "Hips", root.transform, new Vector3(0f, 1.0f, 0f));
            var spine = Bone("spine", "Spine", hips, new Vector3(0f, 0.12f, 0f));
            var chest = Bone("chest", "Chest", spine, new Vector3(0f, 0.12f, 0f));
            var neck = Bone("neck", "Neck", chest, new Vector3(0f, 0.18f, 0f));
            Bone("head", "Head", neck, new Vector3(0f, 0.10f, 0f));

            var leftShoulder = Bone("leftShoulder", "LeftShoulder", chest, new Vector3(-0.05f, 0.10f, 0f));
            var leftUpperArm = Bone("leftUpperArm", "LeftUpperArm", leftShoulder, new Vector3(-0.12f, 0f, 0f));
            var leftLowerArm = Bone("leftLowerArm", "LeftLowerArm", leftUpperArm, new Vector3(-0.26f, 0f, 0f));
            Bone("leftHand", "LeftHand", leftLowerArm, new Vector3(-0.24f, 0f, 0f));

            var rightShoulder = Bone("rightShoulder", "RightShoulder", chest, new Vector3(0.05f, 0.10f, 0f));
            var rightUpperArm = Bone("rightUpperArm", "RightUpperArm", rightShoulder, new Vector3(0.12f, 0f, 0f));
            var rightLowerArm = Bone("rightLowerArm", "RightLowerArm", rightUpperArm, new Vector3(0.26f, 0f, 0f));
            Bone("rightHand", "RightHand", rightLowerArm, new Vector3(0.24f, 0f, 0f));

            var leftUpperLeg = Bone("leftUpperLeg", "LeftUpperLeg", hips, new Vector3(-0.09f, -0.06f, 0f));
            var leftLowerLeg = Bone("leftLowerLeg", "LeftLowerLeg", leftUpperLeg, new Vector3(0f, -0.42f, 0f));
            Bone("leftFoot", "LeftFoot", leftLowerLeg, new Vector3(0f, -0.42f, 0.08f));

            var rightUpperLeg = Bone("rightUpperLeg", "RightUpperLeg", hips, new Vector3(0.09f, -0.06f, 0f));
            var rightLowerLeg = Bone("rightLowerLeg", "RightLowerLeg", rightUpperLeg, new Vector3(0f, -0.42f, 0f));
            Bone("rightFoot", "RightFoot", rightLowerLeg, new Vector3(0f, -0.42f, 0.08f));

            // Reference pose (TPose) captured from the authored local transforms.
            var ordered = new List<Transform>(bones.Values);
            var referencePose = new ReferencePose
            {
                PoseType = "TPose",
                Bones = ordered.ToArray(),
                LocalPositions = ordered.ConvertAll(t => t.localPosition).ToArray(),
                LocalRotations = ordered.ConvertAll(t => t.localRotation).ToArray(),
                LocalScales = ordered.ConvertAll(t => t.localScale).ToArray(),
            };

            var skeleton = root.AddComponent<SkeletonMap>();
            skeleton.Bind(new SkeletonMappingResult
            {
                Bones = bones,
                SelectedRig = "unityHumanoid",
                ReferencePose = referencePose,
            });

            // A portrait camera hint node, looking at the head.
            var portrait = new GameObject("PortraitCamera") { hideFlags = HideFlags.HideAndDontSave };
            portrait.transform.SetParent(root.transform, false);
            portrait.transform.localPosition = new Vector3(0f, 1.5f, 1.2f);
            root.AddComponent<CameraHintSet>().Bind(new List<CameraHint>
            {
                new CameraHint { Role = "portrait", Label = "Portrait", Node = portrait.transform, Target = bones["head"] },
            });

            return root;
        }

        // ── Look-at assembly (SC-LookAt) ─────────────────────────────────────

        // The minimal carrier for KHR_node_lookat_target (bug B7 had zero coverage): a KHR_character root whose
        // GazeSolver.AuthoredTargets mark two ordinary nodes as look-at targets. One target carries a "hint" string;
        // the other is hint-less (an empty {} target - presence alone marks the node). No expressions or skeleton are
        // needed: the exporter's node-feature gate emits on AuthoredTargets alone, and the importer attaches a
        // GazeSolver and rehydrates the targets whenever any look-at target is present.
        private static GameObject AssembleLookAtCharacter(List<Object> temps)
        {
            var root = new GameObject("SC-LookAt") { hideFlags = HideFlags.HideAndDontSave };
            root.AddComponent<KhrCharacter>();

            var focus = new GameObject("FocusTarget") { hideFlags = HideFlags.HideAndDontSave };
            focus.transform.SetParent(root.transform, false);
            focus.transform.localPosition = new Vector3(0f, 1.5f, 2f);    // in front, head height

            var aux = new GameObject("AuxTarget") { hideFlags = HideFlags.HideAndDontSave };
            aux.transform.SetParent(root.transform, false);
            aux.transform.localPosition = new Vector3(0.6f, 1.5f, 1.6f);

            var gaze = root.AddComponent<GazeSolver>();
            gaze.Bind(new List<LookAtTarget>
            {
                new LookAtTarget { Node = focus.transform, Hint = "primary" },
                new LookAtTarget { Node = aux.transform, Hint = null },
            }, null);

            return root;
        }

        // ── Partial assembly (SC-Partial / SC-PseudoVRM) ────────────────────────

        // The minimal graceful-degradation carrier: a KHR_character root with ONLY a single morph expression - no
        // joint, texture, mask, mapping, skeleton, reference pose, camera hint, or look-at target. Proves the importer
        // surfaces just the present capabilities and leaves the rest cleanly absent. Also the body of SC-PseudoVRM
        // (which then gets VRMC_* injected post-export).
        private static GameObject AssemblePartialCharacter(List<Object> temps)
        {
            var root = new GameObject("SC-Partial") { hideFlags = HideFlags.HideAndDontSave };
            root.AddComponent<KhrCharacter>();

            var mesh = BuildMinimalMorphMesh();
            temps.Add(mesh);
            var material = CreateSkinMaterial();
            if (material != null) temps.Add(material);

            var faceGo = new GameObject("Face") { hideFlags = HideFlags.HideAndDontSave };
            faceGo.transform.SetParent(root.transform, false);
            var smr = faceGo.AddComponent<SkinnedMeshRenderer>();
            smr.sharedMesh = mesh;
            if (material != null) smr.sharedMaterial = material;
            smr.localBounds = mesh.bounds;

            var set = new CharacterExpressionSet
            {
                Expressions = new[] { MorphTrack("blink", smr, 0) },
            };
            root.AddComponent<ExpressionController>().Initialize(set);
            return root;
        }

        // ── Expression-edge assembly (SC-ExprEdge) ────────────────────────

        // Two morph expressions on a two-blendshape mesh: 'edgeA' drives shapeA; 'edgeB' drives shapeB AND BLOCK-masks
        // edgeA (amount 1, threshold 0) - so driving edgeB above the threshold fully zeroes edgeA's output while edgeB
        // still drives its own shape. The block-mask counterpart to SC-Face's blend mask.
        private static GameObject AssembleExprEdgeCharacter(List<Object> temps)
        {
            var root = new GameObject("SC-ExprEdge") { hideFlags = HideFlags.HideAndDontSave };
            root.AddComponent<KhrCharacter>();

            var mesh = BuildTwoShapeMesh();
            temps.Add(mesh);
            var material = CreateSkinMaterial();
            if (material != null) temps.Add(material);

            var faceGo = new GameObject("Face") { hideFlags = HideFlags.HideAndDontSave };
            faceGo.transform.SetParent(root.transform, false);
            var smr = faceGo.AddComponent<SkinnedMeshRenderer>();
            smr.sharedMesh = mesh;
            if (material != null) smr.sharedMaterial = material;
            smr.localBounds = mesh.bounds;

            var edgeA = MorphTrack("edgeA", smr, 0);   // drives shapeA
            var edgeB = MorphTrack("edgeB", smr, 1);   // drives shapeB AND block-masks edgeA
            edgeB.Masks = new[]
            {
                new MaskEntry { TargetIndex = 0, SourceIndex = 1, Type = MaskType.Block, Amount = 1f, Threshold = 0f },
            };
            var set = new CharacterExpressionSet { Expressions = new[] { edgeA, edgeB } };
            root.AddComponent<ExpressionController>().Initialize(set);
            return root;
        }

        // A tiny triangle with two blendshapes (shapeA, shapeB) for the block-mask edge fixture.
        private static Mesh BuildTwoShapeMesh()
        {
            var mesh = new Mesh { name = "SC-ExprEdge-Mesh", hideFlags = HideFlags.HideAndDontSave };
            mesh.SetVertices(new List<Vector3>
            {
                new Vector3(-0.3f, 0f, 0f), new Vector3(0.3f, 0f, 0f), new Vector3(0f, 0.6f, 0f),
            });
            mesh.SetTriangles(new List<int> { 0, 1, 2 }, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            mesh.AddBlendShapeFrame("shapeA", 100f,
                new[] { new Vector3(0f, -0.1f, 0f), new Vector3(0f, -0.1f, 0f), new Vector3(0f, -0.05f, 0f) }, null, null);
            mesh.AddBlendShapeFrame("shapeB", 100f,
                new[] { new Vector3(0.1f, 0f, 0f), new Vector3(0.1f, 0f, 0f), new Vector3(0.05f, 0f, 0f) }, null, null);
            return mesh;
        }

        // ── Export ──────────────────────────────────────

        private static string ExportAndImport(GameObject root, string outputDirectory, string fileName)
        {
            string writtenPath = Export(root, outputDirectory, fileName);
            ImportIfUnderAssets(writtenPath);
            return writtenPath;
        }

        // Isolated default settings + the KHR Character and AnimationPointer export plugins enabled (both required),
        // exactly mirroring the plugin's own export-test helper.
        private static string Export(GameObject root, string outputDirectory, string fileName)
        {
            var settings = GLTFSettings.GetDefaultSettings();
            foreach (var plugin in settings.ExportPlugins)
                if (plugin is KhrCharacterExportPlugin || plugin is AnimationPointerExport)
                    plugin.Enabled = true;

            var exporter = new GLTFSceneExporter(new[] { root.transform }, new ExportContext(settings));
            exporter.SaveGLB(outputDirectory, fileName);

            return Path.Combine(outputDirectory, fileName + ".glb").Replace('\\', '/');
        }

        private static void ImportIfUnderAssets(string path)
        {
            if (string.IsNullOrEmpty(path)) return;
            var normalized = path.Replace('\\', '/');
            int idx = normalized.IndexOf("Assets/", System.StringComparison.Ordinal);
            string assetPath = normalized.StartsWith("Assets/", System.StringComparison.Ordinal)
                ? normalized
                : (idx >= 0 ? normalized.Substring(idx) : null);

            if (assetPath != null) AssetDatabase.ImportAsset(assetPath, ImportAssetOptions.ForceUpdate);
            else AssetDatabase.Refresh();
        }

        // Post-process an exported GLB to inject vendor extension tokens into extensionsUsed + a stub root extension
        // object, simulating a VRM-origin asset. Rewrites the JSON chunk (re-padded to 4 bytes) and the GLB + JSON
        // chunk lengths; the BIN chunk is preserved verbatim. Synthetic/CC0 - the tokens carry no real VRM data.
        private static void InjectVendorExtensions(string glbPath, params string[] vendorTokens)
        {
            const uint GlbMagic = 0x46546C67;   // "glTF"
            const uint ChunkJson = 0x4E4F534A;  // "JSON"

            byte[] bytes = File.ReadAllBytes(glbPath);
            if (bytes.Length < 20 || System.BitConverter.ToUInt32(bytes, 0) != GlbMagic)
                throw new System.Exception($"Not a binary glTF: {glbPath}");
            uint jsonLen = System.BitConverter.ToUInt32(bytes, 12);
            if (System.BitConverter.ToUInt32(bytes, 16) != ChunkJson)
                throw new System.Exception($"First GLB chunk is not JSON: {glbPath}");

            int jsonStart = 20;
            int binStart = jsonStart + (int)jsonLen;            // BIN chunk (+ any trailing chunks) preserved as-is
            string json = Encoding.UTF8.GetString(bytes, jsonStart, (int)jsonLen);
            var root = JObject.Parse(json);

            var used = root["extensionsUsed"] as JArray;
            if (used == null) { used = new JArray(); root["extensionsUsed"] = used; }
            var exts = root["extensions"] as JObject;
            if (exts == null) { exts = new JObject(); root["extensions"] = exts; }
            foreach (var token in vendorTokens)
            {
                bool present = false;
                foreach (var t in used) if ((string)t == token) { present = true; break; }
                if (!present) used.Add(token);
                if (exts[token] == null) exts[token] = new JObject();
            }

            byte[] newJson = Encoding.UTF8.GetBytes(root.ToString(Newtonsoft.Json.Formatting.None));
            int pad = (4 - (newJson.Length % 4)) % 4;
            int newJsonChunkLen = newJson.Length + pad;
            byte[] binChunk = new byte[bytes.Length - binStart];
            System.Array.Copy(bytes, binStart, binChunk, 0, binChunk.Length);

            int total = 12 + 8 + newJsonChunkLen + binChunk.Length;
            using (var ms = new MemoryStream(total))
            using (var w = new BinaryWriter(ms))
            {
                w.Write(GlbMagic); w.Write((uint)2); w.Write((uint)total);
                w.Write((uint)newJsonChunkLen); w.Write(ChunkJson);
                w.Write(newJson);
                for (int i = 0; i < pad; i++) w.Write((byte)0x20);   // pad JSON chunk with spaces
                w.Write(binChunk);
                File.WriteAllBytes(glbPath, ms.ToArray());
            }
        }

        private static string Normalize(string outputDirectory)
            => string.IsNullOrEmpty(outputDirectory) ? DefaultOutputDirectory : outputDirectory;

        private static void Cleanup(List<Object> temps)
        {
            for (int i = temps.Count - 1; i >= 0; i--)
                if (temps[i] != null) Object.DestroyImmediate(temps[i]);
        }

        // ── Mesh / texture helpers ───────────────────────────────────────────

        // A small UV sphere stretched slightly on Y (head-ish) with six blendshapes whose deltas visibly move
        // vertices: an opening jaw, a blink, and four eye-region look directions.
        private static Mesh BuildHeadMesh()
        {
            const int rings = 16;
            const int sectors = 24;
            const float radius = 0.5f;
            const float yScale = 1.3f;

            var verts = new List<Vector3>();
            var normals = new List<Vector3>();
            var uvs = new List<Vector2>();

            for (int y = 0; y <= rings; y++)
            {
                float v = (float)y / rings;
                float theta = v * Mathf.PI;
                float sinT = Mathf.Sin(theta);
                float cosT = Mathf.Cos(theta);
                for (int x = 0; x <= sectors; x++)
                {
                    float u = (float)x / sectors;
                    float phi = u * Mathf.PI * 2f;
                    var p = new Vector3(sinT * Mathf.Cos(phi), cosT, sinT * Mathf.Sin(phi)) * radius;
                    p.y *= yScale;
                    verts.Add(p);
                    normals.Add(p.normalized);
                    uvs.Add(new Vector2(u, v));
                }
            }

            var tris = new List<int>();
            int stride = sectors + 1;
            for (int y = 0; y < rings; y++)
            {
                for (int x = 0; x < sectors; x++)
                {
                    int a = y * stride + x;
                    int b = a + 1;
                    int c = a + stride;
                    int d = c + 1;
                    tris.Add(a); tris.Add(c); tris.Add(b);
                    tris.Add(b); tris.Add(c); tris.Add(d);
                }
            }

            var mesh = new Mesh { name = "SC-Face-Mesh", hideFlags = HideFlags.HideAndDontSave };
            mesh.indexFormat = verts.Count > 65535
                ? UnityEngine.Rendering.IndexFormat.UInt32
                : UnityEngine.Rendering.IndexFormat.UInt16;
            mesh.SetVertices(verts);
            mesh.SetNormals(normals);
            mesh.SetUVs(0, uvs);
            mesh.SetTriangles(tris, 0);
            mesh.RecalculateBounds();

            var baseVerts = verts.ToArray();
            mesh.AddBlendShapeFrame("jawOpen", 100f, BuildDelta(baseVerts, JawOpenDelta), null, null);
            mesh.AddBlendShapeFrame("blink", 100f, BuildDelta(baseVerts, BlinkDelta), null, null);
            mesh.AddBlendShapeFrame("lookLeft", 100f, BuildDelta(baseVerts, LookLeftDelta), null, null);
            mesh.AddBlendShapeFrame("lookRight", 100f, BuildDelta(baseVerts, LookRightDelta), null, null);
            mesh.AddBlendShapeFrame("lookUp", 100f, BuildDelta(baseVerts, LookUpDelta), null, null);
            mesh.AddBlendShapeFrame("lookDown", 100f, BuildDelta(baseVerts, LookDownDelta), null, null);
            mesh.AddBlendShapeFrame("smile", 100f, BuildDelta(baseVerts, SmileDelta), null, null); // index 6 -> the "happy" expression
            return mesh;
        }

        private static Mesh BuildQuadMesh()
        {
            var mesh = new Mesh { name = "SC-Quad", hideFlags = HideFlags.HideAndDontSave };
            mesh.SetVertices(new List<Vector3>
            {
                new Vector3(-0.5f, -0.5f, 0f), new Vector3(0.5f, -0.5f, 0f),
                new Vector3(-0.5f, 0.5f, 0f), new Vector3(0.5f, 0.5f, 0f),
            });
            mesh.SetUVs(0, new List<Vector2>
            {
                new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(0f, 1f), new Vector2(1f, 1f),
            });
            mesh.SetTriangles(new List<int> { 0, 2, 1, 1, 2, 3 }, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }

        // A tiny triangle with a single "blink" blendshape - the minimal mesh for a morph-only character.
        private static Mesh BuildMinimalMorphMesh()
        {
            var mesh = new Mesh { name = "SC-Partial-Mesh", hideFlags = HideFlags.HideAndDontSave };
            mesh.SetVertices(new List<Vector3>
            {
                new Vector3(-0.3f, 0f, 0f), new Vector3(0.3f, 0f, 0f), new Vector3(0f, 0.6f, 0f),
            });
            mesh.SetTriangles(new List<int> { 0, 1, 2 }, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            mesh.AddBlendShapeFrame("blink", 100f,
                new[] { new Vector3(0f, -0.1f, 0f), new Vector3(0f, -0.1f, 0f), new Vector3(0f, -0.05f, 0f) }, null, null);
            return mesh;
        }

        private static Texture2D MakeReadableTexture(string name, Color color, List<Object> temps)
        {
            var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false) { name = name, hideFlags = HideFlags.HideAndDontSave };
            var px = new Color[4];
            for (int i = 0; i < px.Length; i++) px[i] = color;
            tex.SetPixels(px);
            tex.Apply();
            temps.Add(tex);
            return tex;
        }

        private static Vector3[] BuildDelta(Vector3[] verts, System.Func<Vector3, Vector3> fn)
        {
            var deltas = new Vector3[verts.Length];
            for (int i = 0; i < verts.Length; i++) deltas[i] = fn(verts[i]);
            return deltas;
        }

        private static float Gauss(float x, float center, float width)
        {
            float t = (x - center) / Mathf.Max(width, 1e-4f);
            return Mathf.Exp(-0.5f * t * t);
        }

        private static float EyeMask(Vector3 v)
        {
            if (v.z <= 0.08f) return 0f;
            float band = Gauss(v.y, 0.22f, 0.13f);
            float front = Mathf.Clamp01((v.z - 0.08f) / 0.42f);
            float side = Mathf.Clamp01(1f - Mathf.Abs(v.x) / 0.45f);
            return band * front * side;
        }

        private static Vector3 JawOpenDelta(Vector3 v)
        {
            if (v.z <= 0f || v.y >= 0.05f) return Vector3.zero;
            float low = Mathf.Clamp01((0.05f - v.y) / 0.6f);
            float front = Mathf.Clamp01(v.z / 0.5f);
            return new Vector3(0f, -0.22f, 0.04f) * (low * front);
        }

        private static Vector3 BlinkDelta(Vector3 v)
        {
            float mask = EyeMask(v);
            if (v.y < 0.22f) mask *= 0.4f;
            return new Vector3(0f, -0.09f, 0f) * mask;
        }

        private static Vector3 LookLeftDelta(Vector3 v) => new Vector3(-0.12f, 0f, 0.02f) * EyeMask(v);
        private static Vector3 LookRightDelta(Vector3 v) => new Vector3(0.12f, 0f, 0.02f) * EyeMask(v);
        private static Vector3 LookUpDelta(Vector3 v) => new Vector3(0f, 0.10f, 0.03f) * EyeMask(v);
        private static Vector3 LookDownDelta(Vector3 v) => new Vector3(0f, -0.10f, 0.03f) * EyeMask(v);

        // A smile: lifts + spreads the lower-front mouth-corner region (a stand-in smile shape for "happy").
        private static Vector3 SmileDelta(Vector3 v)
        {
            if (v.z <= 0.08f || v.y > 0.02f || v.y < -0.28f) return Vector3.zero;
            float corner = Mathf.Clamp01(Mathf.Abs(v.x) / 0.35f);   // strongest at the mouth corners
            float front = Mathf.Clamp01((v.z - 0.08f) / 0.42f);
            float band = Gauss(v.y, -0.12f, 0.10f);                 // around mouth height
            float w = corner * front * band;
            return new Vector3(Mathf.Sign(v.x) * 0.04f, 0.06f, 0f) * w;   // out + up
        }

        // ── Material ─────────────────────────────────────────────────────────

        // A pipeline-aware skin-tone material: the lit shader for the active pipeline (Standard on Built-in, URP Lit
        // under URP). Returns null if no shader is available, in which case the exporter emits a default material.
        private static Material CreateSkinMaterial()
        {
            Shader shader = RenderPipelineUtil.LitShader();
            if (shader == null) return null;

            var material = new Material(shader) { name = "SC-Face-Mat", hideFlags = HideFlags.HideAndDontSave };
            var skin = new Color(0.92f, 0.78f, 0.70f, 1f);
            if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", skin);
            else if (material.HasProperty("_Color")) material.SetColor("_Color", skin);
            return material;
        }
    }
}
