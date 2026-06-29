using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using Samples.Shared;

namespace KhrCharacterTestbed.Tests
{
    /// <summary>
    /// Unit coverage for <see cref="SceneBoundsUtil"/> on a deterministic synthetic hierarchy (two quad meshes with
    /// distinct meshes/materials at known positions): aggregate world bounds enclose both, and the asset inventory
    /// (renderers / unique meshes / triangles / unique materials / textures / nodes) matches the construction.
    /// State/asset-only, so render-pipeline-agnostic. Anti-hollow via the real helper + real Unity assets.
    /// </summary>
    public class SceneBoundsUtilTests
    {
        private readonly List<Object> _created = new List<Object>();

        [TearDown]
        public void TearDown()
        {
            foreach (var o in _created)
                if (o != null) Object.DestroyImmediate(o);
            _created.Clear();
        }

        // 4-vertex / 2-triangle quad in the XY plane spanning the unit square.
        private Mesh Quad()
        {
            var mesh = new Mesh
            {
                vertices = new[]
                {
                    new Vector3(0f, 0f, 0f), new Vector3(1f, 0f, 0f),
                    new Vector3(1f, 1f, 0f), new Vector3(0f, 1f, 0f),
                },
                triangles = new[] { 0, 1, 2, 0, 2, 3 },
            };
            mesh.RecalculateBounds();
            _created.Add(mesh);
            return mesh;
        }

        private GameObject MeshNode(string name, Transform parent, Vector3 position)
        {
            var go = new GameObject(name, typeof(MeshFilter), typeof(MeshRenderer));
            go.transform.SetParent(parent, false);
            go.transform.position = position;
            go.GetComponent<MeshFilter>().sharedMesh = Quad();
            var shader = RenderPipelineUtil.LitShader();
            var material = shader != null ? new Material(shader) : null;
            if (material != null) _created.Add(material);
            go.GetComponent<MeshRenderer>().sharedMaterial = material;
            return go;
        }

        private GameObject BuildFixture()
        {
            var root = new GameObject("BoundsFixtureRoot");
            _created.Add(root);
            MeshNode("A", root.transform, new Vector3(0f, 0f, 0f));
            MeshNode("B", root.transform, new Vector3(2f, 0f, 0f));
            return root;
        }

        [Test]
        public void TryAggregate_EnclosesAllRenderers()
        {
            var root = BuildFixture();

            Assert.IsTrue(SceneBoundsUtil.TryAggregate(root, out var bounds), "two mesh renderers should aggregate.");
            // A spans x[0,1], B spans x[2,3]; both span y[0,1] at z=0 -> center (1.5, 0.5, 0), width.x == 3.
            Assert.AreEqual(1.5f, bounds.center.x, 1e-3f, "aggregate should be centered between the two quads.");
            Assert.AreEqual(0.5f, bounds.center.y, 1e-3f);
            Assert.AreEqual(3f, bounds.size.x, 1e-3f, "aggregate width should span both quads.");
            Assert.IsTrue(bounds.Contains(new Vector3(0f, 0f, 0f)) && bounds.Contains(new Vector3(3f, 1f, 0f)),
                "aggregate must contain both quads' extents.");
        }

        [Test]
        public void TryAggregate_ReturnsFalse_WhenNoRenderers()
        {
            var empty = new GameObject("Empty");
            _created.Add(empty);
            Assert.IsFalse(SceneBoundsUtil.TryAggregate(empty, out _), "a hierarchy with no renderers has no bounds.");
            Assert.IsFalse(SceneBoundsUtil.TryAggregate(null, out _), "a null root has no bounds.");
        }

        [Test]
        public void Count_TalliesTheInventory()
        {
            var root = BuildFixture();

            var counts = SceneBoundsUtil.Count(root);
            Assert.AreEqual(2, counts.Renderers, "two mesh renderers.");
            Assert.AreEqual(2, counts.Meshes, "two distinct quad meshes.");
            Assert.AreEqual(4, counts.Triangles, "two triangles per quad over two quads.");
            Assert.AreEqual(2, counts.Materials, "two distinct materials.");
            Assert.AreEqual(0, counts.Textures, "no textures were assigned.");
            Assert.AreEqual(3, counts.Nodes, "root + two mesh children.");
        }

        [Test]
        public void Count_NullRoot_IsZero()
        {
            var counts = SceneBoundsUtil.Count(null);
            Assert.AreEqual(0, counts.Renderers);
            Assert.AreEqual(0, counts.Nodes);
        }
    }
}
