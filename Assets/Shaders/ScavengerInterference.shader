Shader "Moon Game/Scavenger Interference"
{
    // Static over the SCAV_BOT camera when its charge fails. The visual style
    // (F6) still applies on top. _SignalLoss 1 = only snow.
    Properties
    {
        _Interference("Interference", Range(0, 1)) = 0
        _SignalLoss("Signal Loss", Range(0, 1)) = 0
        _Collapse("Tube Off Collapse", Range(0, 1)) = 0
    }
    SubShader
    {
        Tags { "RenderType"="Overlay" "Queue"="Overlay" "RenderPipeline"="UniversalPipeline" "IgnoreProjector"="True" }
        Pass
        {
            Name "SCAVENGER INTERFERENCE"
            Tags { "LightMode"="UniversalForwardOnly" }
            ZWrite Off
            ZTest Always
            Cull Off
            Blend Off
            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex InterferenceVertex
            #pragma fragment InterferenceFragment
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareOpaqueTexture.hlsl"
            CBUFFER_START(UnityPerMaterial)
                float _Interference, _SignalLoss, _Collapse;
            CBUFFER_END

            struct Attributes { float4 positionOS:POSITION; float2 uv:TEXCOORD0; };
            struct Varyings { float4 positionCS:SV_POSITION; };

            // UVs become clip space: the quad covers the screen at any aspect.
            Varyings InterferenceVertex(Attributes input)
            {
                Varyings output;
                output.positionCS = float4(input.uv * 2 - 1, 0.5, 1);
                return output;
            }

            float Hash(float2 p)
            {
                p = frac(p * float2(123.34, 456.21));
                p += dot(p, p + 45.32);
                return frac(p.x * p.y);
            }

            half4 InterferenceFragment(Varyings input):SV_Target
            {
                float t = _Time.y;
                float k = saturate(_Interference);
                float2 screen = GetNormalizedScreenSpaceUV(input.positionCS);
                float2 pixel = screen * _ScaledScreenParams.xy;

                // Tube switch-off: squash to a horizontal line, then to a dot.
                float c = saturate(_Collapse);
                float2 extent = float2(max(1 - saturate((c - 0.7) / 0.3), 0.002), max(1 - saturate(c / 0.7), 0.004));
                float2 offset = screen - 0.5;
                if (any(abs(offset) > extent * 0.5)) return half4(0, 0, 0, 1);
                float2 uv = 0.5 + offset / extent;

                float frame = floor(t * 24);
                uv.x += (Hash(float2(floor(uv.y * 220), frame)) - 0.5) * 0.05 * k * k;
                uv.x += smoothstep(0.92, 1, frac(uv.y * 0.7 - t * 0.35)) * (Hash(float2(frame, 7)) - 0.3) * 0.08 * k;
                if (Hash(float2(floor(t * 6), 3)) < k * 0.35) uv.y += (Hash(float2(frame, 11)) - 0.5) * 0.06 * k;
                uv = saturate(uv);

                float shift = 0.012 * k;
                half3 color;
                color.r = SampleSceneColor(uv + float2(shift, 0)).r;
                color.g = SampleSceneColor(uv).g;
                color.b = SampleSceneColor(uv - float2(shift, 0)).b;

                float noise = Hash(floor(pixel * 0.5) + frac(t * 61.7) * 113);
                color = lerp(color, noise.xxx, 0.65 * k * k);
                color *= 1 - 0.25 * k * (0.5 + 0.5 * sin(pixel.y * PI));
                color *= 1 - 0.35 * k * Hash(float2(frame, 1));

                half3 snow = noise.xxx * 0.55 * (0.8 + 0.2 * sin(pixel.y * 0.5 + t * 40));
                color = lerp(color, snow, saturate(_SignalLoss));
                return half4(lerp(color, half3(1, 1, 1), saturate(c * 1.4)), 1);
            }
            ENDHLSL
        }
    }
    FallBack Off
}
