Shader "Moon Game/Bevelled Menu Letter"
{
    Properties { _BaseColor("Letter Metal",Color)=(0.88,0.94,0.85,1) }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "RenderType"="Opaque" }
        Pass
        {
            Tags { "LightMode"="UniversalForwardOnly" }
            Cull Back ZWrite On
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            CBUFFER_START(UnityPerMaterial)
                half4 _BaseColor;
            CBUFFER_END
            struct Input { float3 positionOS:POSITION;float3 normalOS:NORMAL; };
            struct Output { float4 positionCS:SV_POSITION;float3 normalVS:TEXCOORD0; };
            Output Vert(Input input)
            {
                Output output;output.positionCS=TransformObjectToHClip(input.positionOS);
                output.normalVS=TransformWorldToViewDir(TransformObjectToWorldNormal(input.normalOS));return output;
            }
            half4 Frag(Output input):SV_Target
            {
                float3 n=normalize(input.normalVS);float3 light=normalize(float3(-.4,.65,.9));
                float diffuse=.24+.76*saturate(dot(n,light));
                float shine=pow(saturate(dot(n,normalize(light+float3(0,0,1)))),30)*.24;
                return half4(_BaseColor.rgb*diffuse+shine,1);
            }
            ENDHLSL
        }
    }
}
