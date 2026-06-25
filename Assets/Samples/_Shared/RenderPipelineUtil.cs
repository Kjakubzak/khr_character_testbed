using System;
using UnityEngine;
using UnityEngine.Rendering;

namespace Samples.Shared
{
    /// <summary>
    /// Picks shaders for the ACTIVE render pipeline so the same code renders correctly under Built-in (Standard) and
    /// URP (Universal Render Pipeline/Lit). It keys on <see cref="GraphicsSettings.currentRenderPipeline"/> (the active
    /// pipeline asset), NOT on package presence - so merely adding the URP package leaves Built-in behaviour (Standard)
    /// unchanged unless a URP asset is actually active. That keeps the Built-in goldens byte-stable while enabling the
    /// URP nightly cell. Uses a string/reflection check, so this assembly needs no URP package reference.
    /// </summary>
    public static class RenderPipelineUtil
    {
        /// <summary>True when a Universal Render Pipeline asset is the active pipeline.</summary>
        public static bool IsUniversalActive
        {
            get
            {
                var rp = GraphicsSettings.currentRenderPipeline;
                return rp != null && rp.GetType().Name.StartsWith("Universal", StringComparison.Ordinal);
            }
        }

        /// <summary>
        /// The lit shader for the active pipeline: URP Lit when URP is active, otherwise Built-in Standard (with
        /// Unlit/Color as a last resort). Returns null only on a project with none of those shaders available.
        /// </summary>
        public static Shader LitShader()
        {
            if (IsUniversalActive)
            {
                var urp = Shader.Find("Universal Render Pipeline/Lit");
                if (urp != null) return urp;
            }
            return Shader.Find("Standard")
                ?? Shader.Find("Universal Render Pipeline/Lit")
                ?? Shader.Find("Unlit/Color");
        }
    }
}
