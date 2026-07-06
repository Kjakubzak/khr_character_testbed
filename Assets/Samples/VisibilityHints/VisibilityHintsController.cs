using System.Collections.Generic;
using UnityEngine;
using Samples.Shared;
using UnityGLTF.VisibilityHints;

namespace Samples.VisibilityHints
{
    /// <summary>
    /// VisibilityHints demo. Builds a small figure whose parts carry the two view-context visibility-hint
    /// extensions and drives it with a <see cref="ViewContextController"/>:
    /// <list type="bullet">
    /// <item>Head — a <b>node</b> hint <c>third_person_only</c> (your own head is hidden in first person).</item>
    /// <item>Arms — a <b>node</b> hint <c>first_person_only</c> (a first-person view-model, shown only in first person).</item>
    /// <item>Visor accent — a <b>primitive</b> hint on one sub-mesh, <c>first_person_only</c> (realized by the
    /// invisible-material swap).</item>
    /// </list>
    /// A single toggle flips the view context so you can watch renderers enable/disable and the sub-mesh swap live.
    /// The figure is built procedurally (no imported asset) so the demo is self-contained and deterministic.
    /// </summary>
    public class VisibilityHintsController : MonoBehaviour
    {
        private ViewContextController _view;

        private void Start()
        {
            var ui = DemoUiBuilder.Create("Visibility Hints");
            ui.AddLabel("KHR_node_visibility_hint + KHR_mesh_primitive_visibility_hint (view-context visibility).");

            var figure = BuildSampleFigure(transform);
            _view = figure.GetComponent<ViewContextController>();

            ui.AddLabel("Head: node hint = third_person_only (hidden in first person).");
            ui.AddLabel("Arms: node hint = first_person_only (shown only in first person).");
            ui.AddLabel("Visor accent: primitive hint on one sub-mesh = first_person_only.");

            ui.AddToggle("First-person view", OnFirstPersonToggled, false);

            ui.AddLabel("A hidden sub-mesh is realized by swapping its material to an invisible one — a " +
                        "representation, not a normative runtime behavior.");

            var rig = Object.FindFirstObjectByType<OrbitCameraRig>();
            if (rig != null)
            {
                Bounds bounds = SceneBoundsUtil.TryAggregate(figure, out var aggregated)
                    ? aggregated
                    : new Bounds(new Vector3(0f, 1f, 0f), new Vector3(1.5f, 2f, 1f));
                rig.FrameAndFace(bounds, figure.transform);
            }

            var back = gameObject.AddComponent<BackToHubButton>();
            ui.AddButton("Back to Hub", back.GoToHub);
        }

        private void OnFirstPersonToggled(bool firstPerson)
        {
            if (_view != null)
                _view.Mode = firstPerson
                    ? ViewContextController.ViewContext.FirstPerson
                    : ViewContextController.ViewContext.ThirdPerson;
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

            // Visor: a 2-sub-mesh renderer so a per-primitive hint can target sub-mesh 1 (the "accent").
            var visorMesh = MakeTwoSubMeshCube();
            var visor = new GameObject("Visor", typeof(MeshFilter), typeof(MeshRenderer));
            visor.transform.SetParent(root.transform, false);
            visor.transform.localPosition = new Vector3(0f, 1.75f, 0.42f);
            visor.transform.localScale = Vector3.one * 0.5f;
            visor.GetComponent<MeshFilter>().sharedMesh = visorMesh;
            visor.GetComponent<MeshRenderer>().sharedMaterials = new[]
            {
                NewMaterial("VisorBase", new Color(0.25f, 0.25f, 0.28f)),
                NewMaterial("VisorAccent", new Color(0.95f, 0.85f, 0.20f)),
            };

            root.AddComponent<NodeVisibilityHintSet>().Bind(new List<NodeVisibilityHintSet.NodeVisibilityEntry>
            {
                new NodeVisibilityHintSet.NodeVisibilityEntry
                    { Node = head.transform, Role = VisibilityHintExtensionNames.RoleThirdPersonOnly, Label = "Head" },
                new NodeVisibilityHintSet.NodeVisibilityEntry
                    { Node = arms.transform, Role = VisibilityHintExtensionNames.RoleFirstPersonOnly, Label = "Arms" },
            });

            root.AddComponent<PrimitiveVisibilityHintSet>().Bind(new List<PrimitiveVisibilityHintSet.PrimitiveVisibilityEntry>
            {
                new PrimitiveVisibilityHintSet.PrimitiveVisibilityEntry
                    { Mesh = visorMesh, SubMesh = 1, Role = VisibilityHintExtensionNames.RoleFirstPersonOnly, Label = "Visor Accent" },
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

        // A cube mesh split into two sub-meshes (its 12 triangles halved) so a per-primitive hint can target one
        // sub-mesh. Copies Unity's built-in cube geometry (correct normals/UVs) instead of hand-rolling it.
        private static Mesh MakeTwoSubMeshCube()
        {
            var temp = GameObject.CreatePrimitive(PrimitiveType.Cube);
            var src = temp.GetComponent<MeshFilter>().sharedMesh;
            var mesh = new Mesh { name = "VisorTwoSubMesh", vertices = src.vertices, normals = src.normals, uv = src.uv };
            var tris = src.triangles;
            Object.DestroyImmediate(temp);

            int half = (tris.Length / 3 / 2) * 3;
            var a = new int[half];
            var b = new int[tris.Length - half];
            System.Array.Copy(tris, 0, a, 0, a.Length);
            System.Array.Copy(tris, half, b, 0, b.Length);

            mesh.subMeshCount = 2;
            mesh.SetTriangles(a, 0);
            mesh.SetTriangles(b, 1);
            mesh.RecalculateBounds();
            return mesh;
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
