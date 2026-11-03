Shader "Old Tech/Retro Cel Transparent"
{
    Properties
    {
        [MainTexture] _BaseMap("Texture", 2D) = "white" {}
        [MainColor] _BaseColor("Texture Tint (Alpha = Opacity)", Color) = (1, 1, 1, 0.35)
        [IntRange] _ShadeSteps("Lighting Bands", Range(2, 8)) = 4
        _AmbientStrength("Ambient Light", Range(0, 1)) = 0.28
        _ShadowFloor("Minimum Shadow Light", Range(0, 0.3)) = 0.035
        _ShadowTint("Shadow Tint", Color) = (0.47, 0.32, 0.34, 1)
        _BandDither("Dither at Band Edges", Range(0, 1)) = 0.32
        [HDR] _EmissionColor("Emission", Color) = (0, 0, 0, 1)
        [NoScaleOffset] _EmissionMap("Emission Map", 2D) = "white" {}
        [Toggle(_ALPHATEST_ON)] _AlphaClip("Alpha Clipping", Float) = 0
        _Cutoff("Alpha Cutoff", Range(0, 1)) = 0.5
        [Enum(UnityEngine.Rendering.CullMode)] _Cull("Cull", Float) = 2
    }
    SubShader
    {
        Tags
        {
            "RenderType" = "Transparent"
            "Queue" = "Transparent"
            "RenderPipeline" = "UniversalPipeline"
            "IgnoreProjector" = "True"
        }
        LOD 200
        Cull [_Cull]

        // No opaque depth/normal or shadow-caster passes: objects behind this
        // surface must remain visible and must not receive its solid shadow.
        Pass
        {
            Name "RetroCelTransparent"
            Tags { "LightMode" = "UniversalForwardOnly" }
            Blend SrcAlpha OneMinusSrcAlpha, One OneMinusSrcAlpha
            ZWrite Off
            ZTest LEqual

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex CelVertex
            #pragma fragment CelFragment
            #pragma shader_feature_local_fragment _ALPHATEST_ON
            #pragma multi_compile_instancing
            #pragma multi_compile_fog
            #pragma multi_compile _ LIGHTMAP_ON
            #pragma multi_compile _ DIRLIGHTMAP_COMBINED
            #pragma multi_compile _ LIGHTMAP_SHADOW_MIXING
            #pragma multi_compile _ SHADOWS_SHADOWMASK
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS
            #pragma multi_compile _ _CLUSTER_LIGHT_LOOP
            #pragma multi_compile_fragment _ _ADDITIONAL_LIGHT_SHADOWS
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
            #pragma multi_compile_fragment _ _SCREEN_SPACE_OCCLUSION
            #define _SURFACE_TYPE_TRANSPARENT 1
            #include "RetroCelForwardPass.hlsl"
            ENDHLSL
        }
    }
    FallBack Off
}
