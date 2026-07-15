using System.Collections.Generic;
using UnityEngine;

namespace Samples.VisibilityHints
{
    /// <summary>
    /// Tracks the runtime <see cref="Material"/>s and <see cref="Mesh"/> that
    /// <see cref="VisibilityHintsController.BuildSampleFigure"/> creates, and destroys them when the figure's
    /// GameObject is destroyed. Destroying a GameObject frees its components but NOT the <c>new Material()</c> /
    /// <c>new Mesh()</c> objects they reference, so without this the procedural figure leaks ~5 materials + 1 mesh
    /// on every swap (the demo's default and most-exercised path, plus the smoke test that reuses the builder).
    /// Built-in primitive meshes (from <c>GameObject.CreatePrimitive</c>) are shared and deliberately NOT tracked.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class ProceduralFigureResources : MonoBehaviour
    {
        private readonly List<Object> _owned = new List<Object>();

        /// <summary>Register a runtime-created object to destroy on teardown (no-op for null).</summary>
        public void Track(Object obj)
        {
            if (obj != null) _owned.Add(obj);
        }

        private void OnDestroy()
        {
            foreach (var obj in _owned)
                if (obj != null) Destroy(obj);
            _owned.Clear();
        }
    }
}
