Shader "Hidden/KhrCharacterTestbed/InvisiblePrimitive"
{
    // No-draw material for hint-hidden content. The sample's scoped adapter swaps this onto every sub-mesh whose
    // node or primitive hint is false for the camera's view tags (a single Renderer can't disable one sub-mesh, so
    // the material is swapped instead). ColorMask 0 writes no color, ZWrite Off writes no depth, and the shader has
    // no ShadowCaster or DepthOnly pass, so the content adds nothing to that view's color, depth or shadow output.
    // It contains NO HLSL program, so it can never fail to compile into the magenta error shader (the failure mode
    // of Shader.Find-built runtime materials in player builds); it degrades to "renders nothing" under both the
    // Built-in pipeline and URP.
    SubShader
    {
        Tags { "RenderType" = "Opaque" "Queue" = "Geometry" }
        Pass
        {
            ColorMask 0
            ZWrite Off
            Cull Off
        }
    }
    Fallback Off
}
