Shader "Moon Game/Exterior Weather Atmosphere"
{
    Properties { _Density("Density", Float) = 0 _OpacityLimit("Maximum Opacity", Range(0,1)) = 0.96 _FogTint("Dust / Gas Tint", Color) = (0.22,0.2,0.17,1) }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" }
        ZTest Always ZWrite Off Cull Off
        Pass
        {
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"
            float4 _ShelterMins[4], _ShelterMaxs[4];
            int _ShelterCount;
            float _Density, _OpacityLimit;
            half4 _FogTint;
            half4 Frag(Varyings input):SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                half4 source = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, input.texcoord);
                float2 uv = GetNormalizedScreenSpaceUV(input.positionCS);
                float depth = SampleSceneDepth(uv);
                #if !UNITY_REVERSED_Z
                    depth = lerp(UNITY_NEAR_CLIP_VALUE, 1, depth);
                #endif
                float3 position = ComputeWorldSpacePosition(uv, depth, UNITY_MATRIX_I_VP);
                float3 origin = GetCameraPositionWS();
                float3 delta = position - origin;
                float distance = min(length(delta), 180.0);
                float3 direction = normalize(delta + 0.000001);
                float3 safe = (step(0, direction) * 2 - 1) * max(abs(direction), 0.00001);
                float indoor = 0;
                [unroll] for (int i = 0; i < 4; i++)
                {
                    if (i >= _ShelterCount) break;
                    float3 a = (_ShelterMins[i].xyz - origin) / safe;
                    float3 b = (_ShelterMaxs[i].xyz - origin) / safe;
                    float3 lo = min(a,b), hi = max(a,b);
                    float enter = max(max(lo.x,lo.y),lo.z), leave = min(min(hi.x,hi.y),hi.z);
                    indoor += max(0, min(distance,leave) - max(0,enter));
                }
                float outside = max(0, distance - indoor);
                float fog = min(_OpacityLimit, 1 - exp(-outside * _Density));
                return half4(lerp(source.rgb, _FogTint.rgb, fog), source.a);
            }
            ENDHLSL
        }
    }
}
