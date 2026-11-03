Shader "Old Tech/Retro Dither"
{
    Properties
    {
        _Intensity("Effect Intensity", Range(0, 1)) = 1
        _PixelHeight("Virtual Screen Height (0 = Native)", Float) = 480
        [IntRange] _ColorLevels("Color Levels per Channel", Range(4, 32)) = 16
        _DitherStrength("Dither Strength", Range(0, 1)) = 0.9
        _NoiseMix("Grain / Ordered Pattern", Range(0, 1)) = 0.85
        _Exposure("Exposure (Stops)", Range(-2, 2)) = 0
        _Contrast("Contrast", Range(0.5, 2)) = 1.18
        _Saturation("Saturation", Range(0, 1.5)) = 0.62
        _BlackPoint("Black Crush", Range(0, 0.15)) = 0.025
        _PaletteTint("Palette Tint", Range(0, 1)) = 0.16
        _ShadowTint("Rust Shadow Tint", Color) = (0.65, 0.43, 0.45, 1)
        _HighlightTint("Olive Highlight Tint", Color) = (0.85, 0.87, 0.68, 1)
        _Vignette("Vignette", Range(0, 1)) = 0.2
    }
    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" }
        ZTest Always ZWrite Off Cull Off
        Pass
        {
            Name "Retro Dither"
            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float _Intensity, _PixelHeight, _ColorLevels, _DitherStrength;
                float _NoiseMix, _Exposure, _Contrast, _Saturation;
                float _BlackPoint, _PaletteTint, _Vignette;
                half4 _ShadowTint, _HighlightTint;
            CBUFFER_END

            float OrderedThreshold(uint2 pixel)
            {
                // 4x4 Bayer matrix, evaluated without a texture or dynamic array lookup.
                uint2 low = pixel & 1u;
                uint2 high = (pixel >> 1u) & 1u;
                uint a = ((low.x ^ low.y) << 1u) | low.y;
                uint b = ((high.x ^ high.y) << 1u) | high.y;
                return (float(a * 4u + b) + 0.5) / 16.0;
            }

            float GrainThreshold(float2 pixel)
            {
                // Fixed hash: dithering changes quantization choices, never animates.
                float3 p = frac(float3(pixel.xyx) * 0.1031);
                p += dot(p, p.yzx + 33.33);
                return frac((p.x + p.y) * p.z);
            }

            half4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                float2 screenSize = _ScaledScreenParams.xy;
                // Integer-sized blocks keep the grain aligned at different resolutions.
                float pixelSize = _PixelHeight > 0.0 ? max(1.0, floor(screenSize.y / _PixelHeight + 0.5)) : 1.0;
                float2 pixel = floor(input.texcoord * screenSize / pixelSize);
                float2 uv = min((pixel + 0.5) * pixelSize / screenSize, 1.0 - 0.5 / screenSize);
                half4 source = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_PointClamp, uv);
                // A zero intensity must also bypass the pixelation.
                half4 original = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, input.texcoord);
                float3 color = max(0.0, source.rgb * exp2(_Exposure));
                #if !defined(UNITY_COLORSPACE_GAMMA)
                    color = LinearToSRGB(color);
                #endif
                float luma = dot(color, float3(0.2126, 0.7152, 0.0722));
                color = lerp(luma.xxx, color, _Saturation);
                color = (color - 0.5) * _Contrast + 0.5;
                color = saturate((color - _BlackPoint) / max(0.001, 1.0 - _BlackPoint));
                float3 tint = lerp(_ShadowTint.rgb, _HighlightTint.rgb, saturate(luma));
                color *= lerp(1.0.xxx, tint, _PaletteTint);
                float2 centered = input.texcoord * 2.0 - 1.0;
                color *= 1.0 - _Vignette * smoothstep(0.25, 1.5, dot(centered, centered));

                float threshold = lerp(OrderedThreshold(uint2(pixel)), GrainThreshold(pixel), _NoiseMix);
                float levels = max(2.0, floor(_ColorLevels)) - 1.0;
                // Stochastic rounding between adjacent palette entries (true dithering).
                color = floor(saturate(color) * levels + lerp(0.5, threshold, _DitherStrength)) / levels;
                #if !defined(UNITY_COLORSPACE_GAMMA)
                    color = SRGBToLinear(color);
                #endif
                return half4(lerp(original.rgb, color, _Intensity), original.a);
            }
            ENDHLSL
        }
    }
    FallBack Off
}
