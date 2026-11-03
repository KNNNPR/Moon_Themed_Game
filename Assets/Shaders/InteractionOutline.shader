Shader "Moon Game/Interaction Highlight"
{
    Properties
    {
        _MainTex("Mask", 2D) = "black" {}
        _OutlineColor("Outline Color", Color) = (1, 0.95, 0.75, 1)
        _OutlineWidth("Outline Width (pixels)", Range(1, 8)) = 3
    }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" }
        Pass
        {
            Name "ObjectMask"
            Cull Off
            ZWrite On
            ZTest LEqual
            Blend Off
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            float4x4 _OutlineViewProjection;
            struct Attributes { float4 positionOS : POSITION; };
            struct Varyings { float4 positionCS : SV_POSITION; };
            Varyings Vert(Attributes input)
            {
                Varyings output;
                output.positionCS = mul(_OutlineViewProjection, float4(TransformObjectToWorld(input.positionOS.xyz), 1));
                return output;
            }
            half4 Frag(Varyings input) : SV_Target { return half4(1, 1, 1, 1); }
            ENDHLSL
        }
        Pass
        {
            Name "ContourOnly"
            Cull Off
            ZWrite Off
            ZTest Always
            Blend Off
            CGPROGRAM
            #pragma vertex vert_img
            #pragma fragment FragContour
            #include "UnityCG.cginc"
            sampler2D _MainTex;
            float4 _MainTex_TexelSize;
            float4 _OutlineColor;
            float _OutlineWidth;
            float4 FragContour(v2f_img input) : SV_Target
            {
                float2 stepUV = abs(_MainTex_TexelSize.xy) * _OutlineWidth;
                float center = tex2D(_MainTex, input.uv).a;
                float expanded = center;
                [unroll] for (int y = -1; y <= 1; y++)
                    [unroll] for (int x = -1; x <= 1; x++)
                        expanded = max(expanded, tex2D(_MainTex, input.uv + float2(x, y) * stepUV).a);
                return float4(_OutlineColor.rgb, saturate(expanded - center) * _OutlineColor.a);
            }
            ENDCG
        }
    }
}
