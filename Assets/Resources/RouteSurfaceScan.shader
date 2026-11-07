Shader "Hidden/Moon Game/Route Surface Scan"
{
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" }
        Pass
        {
            Cull Off ZWrite On ZTest LEqual
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            float4 _ScanMin,_ScanSize;
            struct Varyings { float4 position:SV_POSITION; float height:TEXCOORD0; float3 normal:TEXCOORD1; };
            Varyings Vert(float3 position:POSITION,float3 normal:NORMAL)
            {
                Varyings o;
                float3 world=TransformObjectToWorld(position);
                float3 scan=(world-_ScanMin.xyz)/_ScanSize.xyz;
                o.position=float4(scan.x*2-1,scan.z*2-1,1-scan.y,1);
                #if UNITY_UV_STARTS_AT_TOP
                    o.position.y=-o.position.y;
                #endif
                #if UNITY_REVERSED_Z
                    o.position.z=scan.y;
                #endif
                o.height=scan.y;
                o.normal=TransformObjectToWorldNormal(normal);
                return o;
            }
            float4 Frag(Varyings i):SV_Target
            {
                float light=saturate(dot(normalize(i.normal),normalize(float3(-.45,1,-.35))));
                float shade=.07+light*.16+saturate(i.height)*.09;
                return float4(shade,shade,shade,i.height);
            }
            ENDHLSL
        }
    }
}
