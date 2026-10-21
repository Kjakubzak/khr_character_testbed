using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using Samples.Shared;
using Samples.VisibilityHints;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.TestTools;
using UnityGLTF.VisibilityHints;

namespace KhrCharacterTestbed.Tests
{
    /// <summary>Actual color/depth proof. Run with a graphics-enabled Editor, separately from -nographics gates.</summary>
    [Category("Graphics")]
    public class SandboxAncestorVisibilityRenderTests
    {
        private readonly List<UnityEngine.Object> _created = new List<UnityEngine.Object>();
        private RenderPipelineAsset _defaultPipeline;
        private RenderPipelineAsset _qualityPipeline;

        [SetUp]
        public void SetUp()
        {
            _defaultPipeline = GraphicsSettings.defaultRenderPipeline;
            _qualityPipeline = QualitySettings.renderPipeline;
            // The measurement shader and Camera.Render route deliberately target Built-in.
            GraphicsSettings.defaultRenderPipeline = null;
            QualitySettings.renderPipeline = null;
        }

        [TearDown]
        public void Cleanup()
        {
            foreach (var value in _created)
                if (value != null) UnityEngine.Object.DestroyImmediate(value);
            _created.Clear();
            GraphicsSettings.defaultRenderPipeline = _defaultPipeline;
            QualitySettings.renderPipeline = _qualityPipeline;
        }

        [UnityTest]
        public IEnumerator HiddenAncestorAndPrimitive_WriteNoColorOrDepth_AndRestoreSceneState()
        {
            Assert.AreNotEqual(GraphicsDeviceType.Null, SystemInfo.graphicsDeviceType,
                "Graphics tests require a real graphics device; do not run this class with -nographics.");
            var load = SandboxTestUtil.LoadSynthetic("VH-Ancestors.glb", _created, CharacterExpressionHostPolicy.Passive);
            yield return load;
            var root = load.Current;
            var view = root.GetComponent<ViewContextController>();
            Assert.IsNotNull(view);
            var target = SandboxTestUtil.FindDeep(root.transform, "AlwaysChild").GetComponent<Renderer>();
            Assert.IsNotNull(target);
            foreach (var renderer in root.GetComponentsInChildren<Renderer>(true))
                renderer.enabled = renderer == target;
            target.gameObject.layer = 31;
            var shader = Shader.Find("Hidden/KhrCharacterTestbed/NormativeVisibilityColor");
            Assert.IsNotNull(shader);
            var red = new Material(shader) { color = Color.red };
            var green = new Material(shader) { color = Color.green };
            _created.Add(red); _created.Add(green);
            target.sharedMaterials = new[] { red, red };
            target.shadowCastingMode = ShadowCastingMode.On;
            var adapter = root.AddComponent<ScopedMaterialVisibilityAdapter>();
            adapter.NoDrawMaterial = InvisibleMaterialInstaller.GetNoDrawMaterial();
            Assert.IsNotNull(adapter.NoDrawMaterial);

            var backdrop = GameObject.CreatePrimitive(PrimitiveType.Quad);
            _created.Add(backdrop);
            backdrop.name = "DepthWitness";
            backdrop.layer = 31;
            backdrop.transform.position = target.bounds.center + Vector3.forward * 0.5f;
            backdrop.transform.localScale = new Vector3(4, 3, 1);
            backdrop.GetComponent<Renderer>().sharedMaterial = green;
            var cameraObject = new GameObject("VisibilityMeasurementCamera");
            _created.Add(cameraObject);
            var camera = cameraObject.AddComponent<Camera>();
            camera.enabled = false;
            camera.orthographic = true;
            camera.orthographicSize = 1.3f;
            camera.transform.position = target.bounds.center - Vector3.forward * 4;
            camera.transform.rotation = Quaternion.identity;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = Color.black;
            camera.cullingMask = 1 << 31;
            camera.nearClipPlane = 0.1f;
            camera.farClipPlane = 10;

            var transforms = root.GetComponentsInChildren<Transform>(true);
            var matrices = new Matrix4x4[transforms.Length];
            var active = new bool[transforms.Length];
            for (int i = 0; i < transforms.Length; i++)
            {
                matrices[i] = transforms[i].localToWorldMatrix;
                active[i] = transforms[i].gameObject.activeSelf;
            }
            var nestedCamera = SandboxTestUtil.FindDeep(root.transform, "NestedCamera").GetComponent<Camera>();
            Assert.IsNotNull(nestedCamera);
            bool cameraEnabled = nestedCamera.enabled;
            var animation = root.GetComponentInChildren<Animation>(true);
            Assert.IsNotNull(animation, "fixture contains ordinary ancestor animation, not an expression clip");
            bool animationEnabled = animation.enabled;
            var mesh = target.GetComponent<MeshFilter>().sharedMesh;
            int vertices = mesh.vertexCount;

            var third = Capture(camera, adapter, target, new[] { "third_person" }, "third-person");
            var first = Capture(camera, adapter, target, new[] { "first_person" }, "first-person");
            var both = Capture(camera, adapter, target, new[] { "first_person", "third_person" }, "both-tags");
            var thirdAgain = Capture(camera, adapter, target, new[] { "third_person" }, "third-person-restored");

            Assert.Greater(third.Red, 100, "the actual nested mesh must render in its matching view");
            Assert.AreEqual(0, first.Red, "always child/primitive cannot reopen a hidden ancestor");
            Assert.Greater(first.Green, third.Green + 100,
                "hidden geometry must not write depth that occludes the green witness behind it");
            Assert.Greater(both.Red, third.Red + 100, "the first-person primitive is additionally visible with both tags");
            Assert.AreEqual(third.Red, thirdAgain.Red, "another view must not leave stale visibility state");
            CollectionAssert.AreEqual(new[] { red, red }, target.sharedMaterials);
            Assert.AreEqual(ShadowCastingMode.On, target.shadowCastingMode);
            Assert.IsTrue(target.enabled);
            Assert.AreEqual(vertices, mesh.vertexCount);
            Assert.AreEqual(cameraEnabled, nestedCamera.enabled);
            Assert.AreEqual(animationEnabled, animation.enabled);
            for (int i = 0; i < transforms.Length; i++)
            {
                Assert.AreEqual(active[i], transforms[i].gameObject.activeSelf, transforms[i].name);
                Assert.AreEqual(matrices[i], transforms[i].localToWorldMatrix, transforms[i].name);
            }
        }

        private static (int Red, int Green) Capture(Camera camera, ScopedMaterialVisibilityAdapter adapter,
            Renderer target, string[] tags, string name)
        {
            var previousActive = RenderTexture.active;
            var previousTarget = camera.targetTexture;
            var texture = RenderTexture.GetTemporary(256, 128, 24, RenderTextureFormat.ARGB32);
            var image = new Texture2D(256, 128, TextureFormat.RGB24, false);
            try
            {
                camera.targetTexture = texture;
                using (adapter.ApplyForView(target, tags, true)) camera.Render();
                RenderTexture.active = texture;
                image.ReadPixels(new Rect(0, 0, 256, 128), 0, 0);
                image.Apply();
                int red = 0, green = 0;
                foreach (var pixel in image.GetPixels32())
                {
                    if (pixel.r > 180 && pixel.g < 70 && pixel.b < 70) red++;
                    if (pixel.g > 180 && pixel.r < 70 && pixel.b < 70) green++;
                }
                string directory = Path.Combine(Directory.GetParent(Application.dataPath).FullName, "Artifacts", "normative-render");
                Directory.CreateDirectory(directory);
                File.WriteAllBytes(Path.Combine(directory, name + ".png"), image.EncodeToPNG());
                File.WriteAllText(Path.Combine(directory, name + ".json"),
                    $"{{\"redPixels\":{red},\"greenPixels\":{green},\"viewTags\":\"{string.Join(",", tags)}\"}}\n");
                return (red, green);
            }
            finally
            {
                camera.targetTexture = previousTarget;
                RenderTexture.active = previousActive;
                RenderTexture.ReleaseTemporary(texture);
                UnityEngine.Object.DestroyImmediate(image);
            }
        }
    }
}
