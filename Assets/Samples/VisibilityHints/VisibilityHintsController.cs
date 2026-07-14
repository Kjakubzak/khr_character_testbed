using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.UI;
using Samples.Shared;
using UnityGLTF;
using UnityGLTF.VisibilityHints;

namespace Samples.VisibilityHints
{
    /// <summary>
    /// VisibilityHints demo. Demonstrates view-context visibility (KHR_node_visibility_hint +
    /// KHR_mesh_primitive_visibility_hint) driven by a <see cref="ViewContextController"/>, and lets you SWAP the
    /// shown asset between the built-in procedural figure and the khr_character example glbs (the per-role
    /// variants under <c>SampleAssets/VRM_KHR_Examples</c>). A "First-person view" toggle flips the active asset's
    /// view context so you can watch renderers enable/disable and the per-primitive material swap live.
    /// <list type="bullet">
    /// <item>Procedural figure — Head: node <c>third_person</c>; Arms: node <c>first_person</c>; Mask shell:
    /// primitive <c>third_person</c> on one sub-mesh (the faceplate hides in first person via the invisible-material
    /// swap; the strap sub-mesh stays).</item>
    /// <item>Example glbs — each variant isolates one role on the real avatar (its imported ViewContextController
    /// drives the same enable/swap behaviour).</item>
    /// </list>
    /// <para>The procedural figure is the default (self-contained + deterministic; the scene smoke test asserts
    /// against it). <see cref="BuildSampleFigure"/> stays public/static so a test builds the same hierarchy.</para>
    /// </summary>
    public class VisibilityHintsController : MonoBehaviour
    {
        private DemoUiBuilder _ui;
        private Text _status;
        private Dropdown _assetDropdown;
        private readonly List<string> _assetPaths = new List<string>(); // parallel to the dropdown; null = procedural
        private Transform _contentRoot;
        private GameObject _current;
        private ViewContextController _view;
        private bool _firstPerson;

        private void Start()
        {
            EnableVisibilityHintImportPlugin();

            _ui = DemoUiBuilder.Create("Visibility Hints");
            _ui.AddLabel("KHR_node_visibility_hint + KHR_mesh_primitive_visibility_hint — view-context visibility. " +
                         "Swap between the built-in figure and the khr_character example assets, then toggle first-person.");

            var content = new GameObject("LoadedContent");
            content.transform.SetParent(transform, false);
            _contentRoot = content.transform;

            _assetDropdown = _ui.AddDropdown("Asset", BuildAssetOptions(), i => { _ = SwapTo(i); }, 0);
            _ui.AddToggle("First-person view", OnFirstPersonToggled, false);
            _status = _ui.AddLabel(string.Empty);

            var back = gameObject.AddComponent<BackToHubButton>();
            _ui.AddButton("Back to Hub", back.GoToHub);

            // Default = the built-in procedural figure: synchronous + deterministic, and what the scene smoke test
            // asserts against. Swap the dropdown to a khr_character example glb to see the hints on a real avatar.
            ShowProceduralFigure();
        }

        // ── Asset list + swapping ────────────────────────────────────────

        private List<string> BuildAssetOptions()
        {
            var options = new List<string> { "Procedural figure (built-in)" };
            _assetPaths.Clear();
            _assetPaths.Add(null); // sentinel: the procedural figure

            foreach (var rel in CharacterLoader.HeroVariantRelativePaths)
            {
                string abs = Path.Combine(Application.dataPath, rel);
                if (!File.Exists(abs)) continue;
                options.Add($"Example: {RoleFromFileName(abs)}");
                _assetPaths.Add(abs);
            }
            return options;
        }

        private static string RoleFromFileName(string path)
        {
            const string prefix = "khr-character-example-";
            string n = Path.GetFileNameWithoutExtension(path);
            return n.StartsWith(prefix) ? n.Substring(prefix.Length) : n; // e.g. "third-person"
        }

        private async System.Threading.Tasks.Task SwapTo(int index)
        {
            if (index < 0 || index >= _assetPaths.Count) return;
            string path = _assetPaths[index];
            if (path == null) { ShowProceduralFigure(); return; }
            if (!File.Exists(path)) { SetStatus($"Missing: {Path.GetFileName(path)}"); return; }

            ClearCurrent();
            SetStatus($"Loading {Path.GetFileName(path)} …");
            GameObject go;
            try { go = await CharacterLoader.LoadAsync(path, _contentRoot); }
            catch (System.Exception e) { Debug.LogException(e); SetStatus("Load failed: " + e.Message); return; }
            if (go == null) { SetStatus("Load failed (no scene)."); return; }

            _current = go;
            _view = go.GetComponentInChildren<ViewContextController>(true);
            ApplyViewMode();
            FrameScene(go);
            SetStatus(_view != null
                ? $"Loaded {Path.GetFileName(path)}. Toggle first-person to see the hint applied."
                : $"Loaded {Path.GetFileName(path)} — this asset carries no visibility hints.");
        }

        private void ShowProceduralFigure()
        {
            ClearCurrent();
            _current = BuildSampleFigure(_contentRoot);
            _view = _current.GetComponent<ViewContextController>();
            ApplyViewMode();
            FrameScene(_current);
            SetStatus("Built-in figure — Head: node third_person; Arms: node first_person; Mask shell: primitive " +
                      "third_person (the faceplate swaps to an invisible material in first person; the strap stays).");
        }

        private void ClearCurrent()
        {
            if (_current != null) Destroy(_current);
            _current = null;
            _view = null;
        }

        // ── View-context toggle ──────────────────────────────────────────

        private void OnFirstPersonToggled(bool firstPerson)
        {
            _firstPerson = firstPerson;
            ApplyViewMode();
        }

        private void ApplyViewMode()
        {
            if (_view != null)
                _view.Mode = _firstPerson
                    ? ViewContextController.ViewContext.FirstPerson
                    : ViewContextController.ViewContext.ThirdPerson;
        }

        private void FrameScene(GameObject go)
        {
            var rig = Object.FindFirstObjectByType<OrbitCameraRig>();
            if (rig == null || go == null) return;
            Bounds bounds = SceneBoundsUtil.TryAggregate(go, out var aggregated)
                ? aggregated
                : new Bounds(new Vector3(0f, 1f, 0f), new Vector3(1.5f, 2f, 1f));
            rig.FrameAndFace(bounds, go.transform);
        }

        private void SetStatus(string s) { if (_status != null) _status.text = s; }

        // Enable UnityGLTF's VisibilityHint import plugin on the shared runtime settings so a loaded example glb
        // gets its ViewContextController + hint-set components (the plugin is disabled by default / non-ratified).
        private static void EnableVisibilityHintImportPlugin()
        {
            var settings = GLTFSettings.GetOrCreateSettings();
            if (settings == null || settings.ImportPlugins == null) return;
            foreach (var plugin in settings.ImportPlugins)
                if (plugin is VisibilityHintImportPlugin vh) vh.Enabled = true;
        }

        /// <summary>
        /// Builds the hinted figure (parts + <see cref="NodeVisibilityHintSet"/> + <see cref="PrimitiveVisibilityHintSet"/>
        /// + <see cref="ViewContextController"/>) under <paramref name="parent"/> and returns its root. Public/static so
        /// a test can build and assert the same hierarchy the demo shows. Binding resolves and applies the initial
        /// (ThirdPerson) visibility immediately.
        /// </summary>
        public static GameObject BuildSampleFigure(Transform parent)
        {
            var root = new GameObject("VisibilityHintsFigure");
            if (parent != null) root.transform.SetParent(parent, false);

            root.AddComponent<ViewContextController>();

            // Torso: no hint authored, so the controller leaves it unmanaged — always visible.
            MakePart(root.transform, "Torso", PrimitiveType.Capsule,
                new Vector3(0f, 1.0f, 0f), new Vector3(0.6f, 0.7f, 0.6f), new Color(0.30f, 0.55f, 0.85f));

            var head = MakePart(root.transform, "Head", PrimitiveType.Sphere,
                new Vector3(0f, 1.75f, 0f), Vector3.one * 0.45f, new Color(0.90f, 0.75f, 0.60f));

            var arms = MakePart(root.transform, "Arms", PrimitiveType.Cube,
                new Vector3(0f, 1.15f, 0.5f), new Vector3(0.7f, 0.18f, 0.18f), new Color(0.85f, 0.55f, 0.30f));

            // Mask: a 2-sub-mesh renderer (one shared mesh) so a per-primitive hint can target ONE sub-mesh.
            // sub-mesh 0 = a strap that stays visible; sub-mesh 1 = the faceplate shell, hinted third_person, so
            // it hides in first person (the classic "don't render the inside of your own head/mask" case).
            var maskMesh = MakeMaskMesh();
            var mask = new GameObject("Mask", typeof(MeshFilter), typeof(MeshRenderer));
            mask.transform.SetParent(root.transform, false);
            mask.transform.localPosition = new Vector3(0f, 1.75f, 0f);
            mask.transform.localScale = Vector3.one * 0.5f;
            mask.GetComponent<MeshFilter>().sharedMesh = maskMesh;
            mask.GetComponent<MeshRenderer>().sharedMaterials = new[]
            {
                NewMaterial("MaskStrap", new Color(0.20f, 0.20f, 0.22f)),
                NewMaterial("MaskShell", new Color(0.85f, 0.20f, 0.25f)),
            };

            root.AddComponent<NodeVisibilityHintSet>().Bind(new List<NodeVisibilityHintSet.NodeVisibilityEntry>
            {
                new NodeVisibilityHintSet.NodeVisibilityEntry
                    { Node = head.transform, Role = VisibilityHintExtensionNames.RoleThirdPerson, Label = "Head" },
                new NodeVisibilityHintSet.NodeVisibilityEntry
                    { Node = arms.transform, Role = VisibilityHintExtensionNames.RoleFirstPerson, Label = "Arms" },
            });

            root.AddComponent<PrimitiveVisibilityHintSet>().Bind(new List<PrimitiveVisibilityHintSet.PrimitiveVisibilityEntry>
            {
                new PrimitiveVisibilityHintSet.PrimitiveVisibilityEntry
                    { Mesh = maskMesh, SubMesh = 1, Role = VisibilityHintExtensionNames.RoleThirdPerson, Label = "Mask Shell" },
            });

            return root;
        }

        private static GameObject MakePart(Transform parent, string name, PrimitiveType prim, Vector3 pos, Vector3 scale, Color color)
        {
            var go = GameObject.CreatePrimitive(prim);
            go.name = name;
            go.transform.SetParent(parent, false);
            go.transform.localPosition = pos;
            go.transform.localScale = scale;
            go.GetComponent<Renderer>().sharedMaterial = NewMaterial(name + "Mat", color);
            return go;
        }

        // A single mesh with two sub-meshes built from two offset boxes, so a per-primitive hint can target one
        // of them: sub-mesh 0 = a thin strap that stays visible; sub-mesh 1 = the faceplate shell in front of the
        // face. Copies Unity's built-in cube geometry (correct winding/UVs) transformed into each box.
        private static Mesh MakeMaskMesh()
        {
            var temp = GameObject.CreatePrimitive(PrimitiveType.Cube);
            var src = temp.GetComponent<MeshFilter>().sharedMesh;
            var baseVerts = src.vertices;
            var baseUv = src.uv;
            var baseTris = src.triangles;
            Object.DestroyImmediate(temp);

            // Box 0 (strap): a low, thin bar near the neck/collar — remains visible (no hint).
            var strap = TransformBox(baseVerts, new Vector3(0f, -0.55f, 0.15f), new Vector3(0.75f, 0.18f, 0.55f));
            // Box 1 (shell): a wide faceplate covering the front of the face — hinted third_person.
            var shell = TransformBox(baseVerts, new Vector3(0f, 0.10f, 0.55f), new Vector3(1.0f, 0.95f, 0.14f));

            var verts = new Vector3[strap.Length + shell.Length];
            strap.CopyTo(verts, 0);
            shell.CopyTo(verts, strap.Length);

            var uv = new Vector2[baseUv.Length * 2];
            baseUv.CopyTo(uv, 0);
            baseUv.CopyTo(uv, baseUv.Length);

            var sub1 = new int[baseTris.Length];
            for (int i = 0; i < baseTris.Length; i++) sub1[i] = baseTris[i] + strap.Length;

            var mesh = new Mesh { name = "MaskTwoSubMesh", vertices = verts, uv = uv };
            mesh.subMeshCount = 2;
            mesh.SetTriangles(baseTris, 0); // sub-mesh 0 = strap (indexes into the first vertex block)
            mesh.SetTriangles(sub1, 1);     // sub-mesh 1 = shell  (offset into the second block)
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }

        // Scale + translate a copy of the unit cube's vertices (Unity's cube spans -0.5..0.5) into a box.
        private static Vector3[] TransformBox(Vector3[] src, Vector3 offset, Vector3 size)
        {
            var r = new Vector3[src.Length];
            for (int i = 0; i < src.Length; i++)
                r[i] = new Vector3(src[i].x * size.x, src[i].y * size.y, src[i].z * size.z) + offset;
            return r;
        }

        // A lit material for the active render pipeline (URP/Built-in), tinted so parts are visually distinct and
        // never fall back to the magenta error shader.
        private static Material NewMaterial(string name, Color color)
        {
            var mat = new Material(RenderPipelineUtil.LitShader()) { name = name };
            if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", color); // URP Lit
            if (mat.HasProperty("_Color")) mat.SetColor("_Color", color);         // Built-in Standard
            return mat;
        }
    }
}
