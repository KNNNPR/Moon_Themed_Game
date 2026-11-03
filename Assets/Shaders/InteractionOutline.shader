Shader "Moon Game/Interaction Highlight"
{
    Properties
    {
        _HighlightAmount("Whitening Amount", Range(0, 0.4)) = 0.12
    }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "RenderType"="Transparent" "Queue"="Transparent+10" }
        Pass
        {
            Name "SoftWhiteHighlight"
            Tags { "LightMode"="UniversalForwardOnly" }
            Cull Off
            ZWrite Off
            ZTest Equal
            Blend SrcAlpha OneMinusSrcAlpha
            ColorMask RGB
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            CBUFFER_START(UnityPerMaterial)
                half _HighlightAmount;
            CBUFFER_END
            struct Attributes { float4 positionOS : POSITION; };
            struct Varyings { float4 positionCS : SV_POSITION; };
            Varyings Vert(Attributes input)
            {
                Varyings output;
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                return output;
            }
            half4 Frag(Varyings input) : SV_Target
            {
                return half4(1, 1, 1, saturate(_HighlightAmount));
            }
            ENDHLSL
        }
    }
}
