Shader "Moon Game/Readable Notes Paper"
{
    Properties
    {
        [MainTexture] _BaseMap("Paper and Tape", 2D) = "white" {}
        [MainColor] _BaseColor("Paper Tint", Color) = (1, 1, 1, 1)
        _Cutoff("Alpha Cutoff", Range(0, 1)) = 0.5
    }
    SubShader
    {
        Tags { "RenderType"="TransparentCutout" "Queue"="AlphaTest" "RenderPipeline"="UniversalPipeline" }
        Cull Back
        Pass
        {
            Name "Readable Paper"
            Tags { "LightMode"="UniversalForwardOnly" }
            ZWrite On
            Stencil { Ref 1 WriteMask 1 Comp Always Pass Replace }
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_fog
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            TEXTURE2D(_BaseMap); SAMPLER(sampler_BaseMap);
            CBUFFER_START(UnityPerMaterial)
                float4 _BaseMap_ST;
                half4 _BaseColor;
                float _Cutoff;
            CBUFFER_END
            struct Attributes { float4 positionOS:POSITION; float3 normalOS:NORMAL; float2 uv:TEXCOORD0; };
            struct Varyings { float4 positionCS:SV_POSITION; float3 positionWS:TEXCOORD0; half3 normalWS:TEXCOORD1; float2 uv:TEXCOORD2; half fog:TEXCOORD3; };
            Varyings Vert(Attributes input)
            {
                Varyings output;
                VertexPositionInputs positions = GetVertexPositionInputs(input.positionOS.xyz);
                output.positionCS = positions.positionCS;
                output.positionWS = positions.positionWS;
                output.normalWS = TransformObjectToWorldNormal(input.normalOS);
                output.uv = TRANSFORM_TEX(input.uv, _BaseMap);
                output.fog = ComputeFogFactor(positions.positionCS.z);
                return output;
            }
            half4 Frag(Varyings input):SV_Target
            {
                half4 paper = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, input.uv) * _BaseColor;
                clip(paper.a - _Cutoff);
                half3 normal = normalize(input.normalWS);
                #if defined(_MAIN_LIGHT_SHADOWS_SCREEN)
                    float4 shadow = ComputeScreenPos(TransformWorldToHClip(input.positionWS));
                #else
                    float4 shadow = TransformWorldToShadowCoord(input.positionWS);
                #endif
                Light mainLight = GetMainLight(shadow);
                half3 light = max(SampleSH(normal), half3(0.3, 0.3, 0.3));
                light += mainLight.color * saturate(dot(normal, mainLight.direction)) * mainLight.shadowAttenuation;
                return half4(MixFog(paper.rgb * light, input.fog), 1);
            }
            ENDHLSL
        }
        UsePass "Universal Render Pipeline/Unlit/DepthOnly"
        UsePass "Universal Render Pipeline/Unlit/DepthNormalsOnly"
    }
}
