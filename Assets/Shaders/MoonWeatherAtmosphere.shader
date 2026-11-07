Shader "Moon Game/Exterior Weather Atmosphere"
{
    Properties { _Density("Density", Float) = 0 _OpacityLimit("Maximum Opacity", Range(0,1)) = 0.96 _FogTint("Dust / Gas Tint", Color) = (0.22,0.2,0.17,1) }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" }
        ZTest Always ZWrite Off Cull Off
        Stencil { Ref 1 ReadMask 1 Comp NotEqual Pass Keep }
        Pass
        {
            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"
            #include "MoonWeatherLighting.hlsl"
            float4 _ShelterMins[4], _ShelterMaxs[4];
            int _ShelterCount;
            float _Density, _OpacityLimit;
            half4 _FogTint;
            float4 _Haze, _HeightHaze, _Palette;
            half4 _HazeColor;
            int _RepairBayFogCount;
            float4 _RepairBayFogMins[16],_RepairBayFogMaxs[16],_RepairBayFogSizes[16],_RepairBayFogTints[16];
            float4x4 _RepairBayFogMatrices[16];

            float RepairFog(int volume,float3 origin,float3 direction,float distance)
            {
                float3 localOrigin=mul(_RepairBayFogMatrices[volume],float4(origin,1)).xyz;
                float3 localDirection=mul((float3x3)_RepairBayFogMatrices[volume],direction);
                float3 safe=(step(0,localDirection)*2-1)*max(abs(localDirection),0.000001);
                float3 a=(-0.5-localOrigin)/safe,b=(0.5-localOrigin)/safe;
                float3 lo=min(a,b),hi=max(a,b);
                float enter=max(0,max(lo.x,max(lo.y,lo.z)));
                float leave=min(distance,min(hi.x,min(hi.y,hi.z)));
                float span=max(0,leave-enter),density=0;
                if(span<=0)return 0;
                [unroll] for(int i=0;i<6;i++)
                {
                    float3 p=origin+direction*(enter+span*(i+0.5)/6.0);
                    float3 localPoint=localOrigin+localDirection*(enter+span*(i+0.5)/6.0);
                    float3 edge=min(localPoint+0.5,0.5-localPoint)*_RepairBayFogSizes[volume].xyz;
                    float fade=smoothstep(0,max(0.01,_RepairBayFogMaxs[volume].w),min(edge.x,min(edge.y,edge.z)));
                    float billow=0.85+0.15*sin(p.x*1.3+sin(p.z*1.1+_Time.y*0.18)+p.y*1.5-_Time.y*0.12);
                    density+=fade*billow;
                }
                return 1-exp(-span*_RepairBayFogMins[volume].w*density/6.0);
            }

            void AddExteriorSegment(float start, float end, float3 origin, float3 direction,
                inout float outside, inout float opticalDepth)
            {
                outside += max(0, end - start);
                start = max(start, _Haze.x);
                float span = max(0, end - start);
                if (span <= 0 || _Haze.y + _Haze.z <= 0) return;
                // Simpson integration makes low-lying dust denser without a flat fog ceiling.
                float3 heights = origin.y + direction.y * float3(start, (start + end) * 0.5, end);
                float3 density = exp(-max(0, heights - _HeightHaze.x) * _HeightHaze.y);
                opticalDepth += span * (_Haze.y + _Haze.z * dot(density, float3(1, 4, 1)) / 6.0);
            }

            float3 LightHalo(float2 uv)
            {
                float2 radius = 3.0 * _BlitTexture_TexelSize.xy;
                float3 a = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, uv + float2(radius.x, 0)).rgb;
                float3 b = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, uv - float2(radius.x, 0)).rgb;
                float3 c = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, uv + float2(0, radius.y)).rgb;
                float3 d = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, uv - float2(0, radius.y)).rgb;
                return (max(0, a - 0.55) + max(0, b - 0.55) + max(0, c - 0.55) + max(0, d - 0.55)) * 0.25;
            }
            half4 Frag(Varyings input):SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                half4 source = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, input.texcoord);
                float2 uv = GetNormalizedScreenSpaceUV(input.positionCS);
                float depth = SampleSceneDepth(uv);
                #if UNITY_REVERSED_Z
                    bool sky = depth < 0.00001;
                #else
                    bool sky = depth > 0.99999;
                #endif
                #if !UNITY_REVERSED_Z
                    depth = lerp(UNITY_NEAR_CLIP_VALUE, 1, depth);
                #endif
                float3 position = ComputeWorldSpacePosition(uv, depth, UNITY_MATRIX_I_VP);
                float3 origin = GetCameraPositionWS();
                float3 delta = position - origin;
                float distance = min(length(delta), 180.0);
                float3 direction = normalize(delta + 0.000001);
                float3 safe = (step(0, direction) * 2 - 1) * max(abs(direction), 0.00001);
                float2 intervals[4];
                [unroll] for (int i = 0; i < 4; i++)
                {
                    intervals[i] = distance.xx;
                    if (i >= _ShelterCount) continue;
                    float3 a = (_ShelterMins[i].xyz - origin) / safe;
                    float3 b = (_ShelterMaxs[i].xyz - origin) / safe;
                    float3 lo = min(a,b), hi = max(a,b);
                    float enter = max(max(lo.x,lo.y),lo.z), leave = min(min(hi.x,hi.y),hi.z);
                    enter = max(0, enter); leave = min(distance, leave);
                    if (leave > enter) intervals[i] = float2(enter, leave);
                }
                // Sort and merge shelter intervals: overlapping rooms are only removed once.
                [unroll] for (int a = 0; a < 3; a++)
                    [unroll] for (int b = a + 1; b < 4; b++)
                        if (intervals[b].x < intervals[a].x)
                        { float2 swap = intervals[a]; intervals[a] = intervals[b]; intervals[b] = swap; }
                float cursor = 0, outside = 0, opticalDepth = 0;
                [unroll] for (int room = 0; room < 4; room++)
                {
                    AddExteriorSegment(cursor, max(cursor, intervals[room].x), origin, direction, outside, opticalDepth);
                    cursor = max(cursor, intervals[room].y);
                }
                AddExteriorSegment(cursor, distance, origin, direction, outside, opticalDepth);
                float baseFog = min(_Haze.w, 1 - exp(-opticalDepth));
                float weatherFog = min(_OpacityLimit, 1 - exp(-outside * _Density));
                if (sky)
                {
                    float horizon = 1 - smoothstep(0.02, 0.2, direction.y);
                    baseFog *= horizon; weatherFog *= horizon;
                }
                float luma = dot(source.rgb, float3(0.2126, 0.7152, 0.0722));
                float bright = smoothstep(0.25, 1.2, luma);
                float saturation = lerp(_Palette.x, 1, bright * 0.65);
                float3 color = lerp(luma.xxx, source.rgb, saturation);
                if(sky)color*=1-_MoonDeepNight*.995;
                color *= lerp(_Palette.y, 1, smoothstep(0.015, 0.18, luma));
                // Small bright emitters remain useful landmarks through the haze.
                color = lerp(color, _HazeColor.rgb, baseFog * (1 - bright * 0.22));
                // Weather must remain visible over the permanent lunar haze, including distant terrain.
                color = lerp(color, _FogTint.rgb, weatherFog);
                if (_Palette.z > 0) color += LightHalo(input.texcoord) * _Palette.z;
                [loop] for(int volume=0;volume<min(_RepairBayFogCount,16);volume++)
                    color=lerp(color,_RepairBayFogTints[volume].rgb,RepairFog(volume,origin,direction,distance));
                return half4(color, source.a);
            }
            ENDHLSL
        }
    }
}
