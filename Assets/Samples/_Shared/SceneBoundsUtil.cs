using System.Collections.Generic;
using UnityEngine;

namespace Samples.Shared
{
    /// <summary>
    /// Shared geometry helpers for the demos: world-bounds aggregation (the
    /// <c>GetComponentsInChildren&lt;Renderer&gt;()</c> + <c>Encapsulate</c> idiom that several controllers used
    /// inline) and a static asset inventory (renderer/mesh/triangle/material/texture/node counts) for the
    /// inspection panel. RP-agnostic; reads only shared assets so it never disturbs the loaded character.
    /// </summary>
    public static class SceneBoundsUtil
    {
        /// <summary>Renderer/mesh/triangle/material/texture/node tally for a loaded hierarchy.</summary>
        public struct AssetCounts
        {
            public int Renderers;
            public int Meshes;
            public int Triangles;
            public int Materials;
            public int Textures;
            public int Nodes;
        }

        /// <summary>
        /// World-space bounds enclosing every active <see cref="Renderer"/> under <paramref name="root"/>. Returns
        /// false (and a default <paramref name="bounds"/>) when the root is null or has no renderers, so callers can
        /// keep their existing "no mesh" fallback/early-return behavior. Active-only (matches the framing idiom).
        /// </summary>
        public static bool TryAggregate(GameObject root, out Bounds bounds)
        {
            bounds = default;
            if (root == null) return false;
            var renderers = root.GetComponentsInChildren<Renderer>();
            if (renderers.Length == 0) return false;
            bounds = renderers[0].bounds;
            for (int i = 1; i < renderers.Length; i++) bounds.Encapsulate(renderers[i].bounds);
            return true;
        }

        /// <summary>
        /// World-space bounds enclosing the renderers under <paramref name="root"/>, or a zero-size bounds at the
        /// root's position when there are none. Prefer <see cref="TryAggregate"/> when the empty case matters.
        /// </summary>
        public static Bounds Aggregate(GameObject root)
        {
            if (TryAggregate(root, out var bounds)) return bounds;
            return new Bounds(root != null ? root.transform.position : Vector3.zero, Vector3.zero);
        }

        /// <summary>
        /// Tally the static asset inventory under <paramref name="root"/> (inactive included, since it is a
        /// content inventory rather than a framing query): renderers, unique meshes, triangles (summed submesh
        /// index counts / 3, read from mesh metadata so it works on non-readable imported meshes), unique
        /// materials (<c>sharedMaterials</c>), unique textures (each material's texture properties), and nodes
        /// (every Transform). All reads are null- and exception-guarded.
        /// </summary>
        public static AssetCounts Count(GameObject root)
        {
            var counts = new AssetCounts();
            if (root == null) return counts;

            counts.Nodes = root.GetComponentsInChildren<Transform>(true).Length;

            var renderers = root.GetComponentsInChildren<Renderer>(true);
            counts.Renderers = renderers.Length;

            var meshes = new HashSet<Mesh>();
            var materials = new HashSet<Material>();
            var textures = new HashSet<Texture>();

            foreach (var filter in root.GetComponentsInChildren<MeshFilter>(true))
                if (filter != null && filter.sharedMesh != null) meshes.Add(filter.sharedMesh);
            foreach (var skinned in root.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                if (skinned != null && skinned.sharedMesh != null) meshes.Add(skinned.sharedMesh);

            counts.Meshes = meshes.Count;
            foreach (var mesh in meshes) counts.Triangles += TriangleCount(mesh);

            foreach (var renderer in renderers)
            {
                if (renderer == null) continue;
                var mats = renderer.sharedMaterials;
                if (mats == null) continue;
                foreach (var material in mats)
                    if (material != null) materials.Add(material);
            }

            counts.Materials = materials.Count;
            foreach (var material in materials) CollectTextures(material, textures);
            counts.Textures = textures.Count;

            return counts;
        }

        // Triangles from submesh index metadata (no read access needed, unlike mesh.triangles which throws on a
        // non-readable imported mesh). Assumes triangle topology, which is what the glTF importer produces.
        private static int TriangleCount(Mesh mesh)
        {
            if (mesh == null) return 0;
            try
            {
                long indices = 0;
                for (int sub = 0; sub < mesh.subMeshCount; sub++) indices += mesh.GetIndexCount(sub);
                return (int)(indices / 3);
            }
            catch (System.Exception e) { Debug.LogException(e); return 0; }
        }

        private static void CollectTextures(Material material, HashSet<Texture> into)
        {
            if (material == null) return;
            try
            {
                foreach (var property in material.GetTexturePropertyNames())
                {
                    if (!material.HasProperty(property)) continue;
                    var texture = material.GetTexture(property);
                    if (texture != null) into.Add(texture);
                }
            }
            catch (System.Exception e) { Debug.LogException(e); }
        }
    }
}
