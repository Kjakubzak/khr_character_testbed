Shader "Hidden/KhrCharacterTestbed/InvisiblePrimitive"
{
    // Invisible material for KHR_mesh_primitive_visibility_hint. UnityGLTF swaps this onto a sub-mesh that must
    // be hidden in the current view context (a single Renderer can't disable one sub-mesh, so the material is
    // swapped instead). ColorMask 0 writes no color channels and ZWrite Off avoids occluding, so the primitive
    // renders nothing. It contains NO HLSL program, so it can never fail to compile into the magenta error
    // shader (the failure mode of Shader.Find-built runtime materials in player builds); it degrades to
    // "renders nothing" under both the Built-in pipeline and URP.
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
