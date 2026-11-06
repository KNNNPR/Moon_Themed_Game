Shader "Moon Game/Charge Monitor CRT"
{
    Properties
    {
        [MainTexture] _BaseMap("CHARGE Display", 2D) = "black" {}
        _Curvature("Barrel Curvature", Range(0, 0.25)) = 0.055
        _WaveStrength("Horizontal Waves", Range(0, 0.01)) = 0.0016
        _VerticalDrift("Vertical Drift", Range(0, 0.005)) = 0.00045
        _WaveSpeed("Wave Speed", Range(0, 5)) = 1.6
        _Brightness("Phosphor Brightness", Range(0.2, 4)) = 1.3
        _Glow("Phosphor Glow", Range(0, 1)) = 0.2
        _Scanlines("Scanline Strength", Range(0, 0.4)) = 0.09
        _Flicker("Subtle Flicker", Range(0, 0.04)) = 0.004
        _GlassTint("Glass Reflection", Color) = (0.005, 0.008, 0.01, 1)
        _LetterFlickerChance("Letter Flicker Chance", Range(0, 1)) = 0.72
        _LetterFlickerRate("Letter Flicker Events / Second", Range(0.2, 8)) = 2.4
        _LetterFlickerDuration("Letter Flicker Duration", Range(0.02, 0.3)) = 0.095
        _LetterFlickerFloor("Dimmed Letter Brightness", Range(0, 1)) = 0.08
        _LetterCount("Letter Count", Float) = 6
        [HideInInspector] _LetterRect("Letter Cells UV Rect", Vector) = (0.125, 0, 0.75, 1)
        [HideInInspector] _PreviewTime("Preview Time (-1 = Live)", Float) = -1
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" "RenderPipeline"="UniversalPipeline" }
        Cull Back
        Pass
        {
            Name "CHARGE CRT"
            Tags { "LightMode"="UniversalForwardOnly" }
            ZWrite On
            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex ScreenVertex
            #pragma fragment ScreenFragment
            #pragma multi_compile_instancing
            #pragma multi_compile_fog
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            TEXTURE2D(_BaseMap); SAMPLER(sampler_BaseMap);
            CBUFFER_START(UnityPerMaterial)
                float4 _BaseMap_TexelSize;
                half4 _GlassTint;
                float4 _LetterRect;
                float _Curvature, _WaveStrength, _VerticalDrift, _WaveSpeed;
                float _Brightness, _Glow, _Scanlines, _Flicker, _PreviewTime;
                float _LetterFlickerChance, _LetterFlickerRate, _LetterFlickerDuration, _LetterFlickerFloor;
                float _LetterCount;
            CBUFFER_END
            #define MOON_CHARGE_DISPLAY 1
            #include "PCMonitorCRT.hlsl"
            struct ScreenAttributes { float4 positionOS:POSITION; float3 normalOS:NORMAL; float2 uv:TEXCOORD0; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct ScreenVaryings { float4 positionCS:SV_POSITION; float2 uv:TEXCOORD0; float3 positionWS:TEXCOORD1; half3 normalWS:TEXCOORD2; half fog:TEXCOORD3; UNITY_VERTEX_OUTPUT_STEREO };
            ScreenVaryings ScreenVertex(ScreenAttributes input)
            {
                UNITY_SETUP_INSTANCE_ID(input);
                ScreenVaryings output = (ScreenVaryings)0;
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
                VertexPositionInputs positions = GetVertexPositionInputs(input.positionOS.xyz);
                output.positionCS = positions.positionCS;
                output.positionWS = positions.positionWS;
                output.normalWS = TransformObjectToWorldNormal(input.normalOS);
                output.uv = input.uv;
                output.fog = ComputeFogFactor(positions.positionCS.z);
                return output;
            }
            half4 ScreenFragment(ScreenVaryings input):SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                return half4(MixFog(CrtDisplay(input.uv, normalize(input.normalWS), input.positionWS), input.fog), 1);
            }
            ENDHLSL
        }
        UsePass "Universal Render Pipeline/Unlit/DepthOnly"
        UsePass "Universal Render Pipeline/Unlit/DepthNormalsOnly"
    }
    FallBack Off
}
