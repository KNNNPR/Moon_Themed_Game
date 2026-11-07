Shader "Moon Game/Story Credits CRT"
{
    Properties
    {
        _BaseMap("Credits Display",2D)="black"{}
        _Curvature("Curvature",Float)=.075
        _WaveStrength("Waves",Float)=.0009
        _VerticalDrift("Vertical Drift",Float)=.0003
        _WaveSpeed("Wave Speed",Float)=1.6
        _Brightness("Brightness",Float)=1.1
        _Glow("Glow",Float)=.12
        _Scanlines("Scanlines",Float)=.06
        _Flicker("Flicker",Float)=.003
        _GlassTint("Glass",Color)=(.008,.012,.004,1)
        _Mask("Mask",Float)=.06
        _Vignette("Vignette",Float)=.38
        _Bleed("Bleed",Float)=.35
        _BlackLevel("Black Level",Color)=(.028,.034,.01,1)
        _Phosphor("Phosphor",Color)=(1,.87,.57,1)
        _Monochrome("Monochrome",Float)=.88
        _PreviewTime("Time",Float)=0
    }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" }
        Cull Off ZWrite Off ZTest Always
        Pass
        {
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            TEXTURE2D(_BaseMap);SAMPLER(sampler_BaseMap);
            CBUFFER_START(UnityPerMaterial)
                float4 _BaseMap_TexelSize;
                half4 _GlassTint,_BlackLevel,_Phosphor;
                float _Curvature,_WaveStrength,_VerticalDrift,_WaveSpeed;
                float _Brightness,_Glow,_Scanlines,_Flicker,_PreviewTime;
                float _Mask,_Vignette,_Bleed,_Monochrome;
            CBUFFER_END
            #define MOON_CRT_TUBE 1
            #define MOON_CRT_FIXED_VIEW 1
            #include "../Shaders/PCMonitorCRT.hlsl"
            struct Attributes {float4 positionOS:POSITION;float2 uv:TEXCOORD0;};
            struct Varyings {float4 positionCS:SV_POSITION;float2 uv:TEXCOORD0;};
            Varyings Vert(Attributes input)
            {
                Varyings output;output.positionCS=TransformObjectToHClip(input.positionOS.xyz);output.uv=input.uv;return output;
            }
            half4 Frag(Varyings input):SV_Target{return half4(CrtDisplay(input.uv,half3(0,0,1),float3(0,0,0)),1);}
            ENDHLSL
        }
    }
}
