Shader "Moon Game/Color ASCII"
{
    Properties
    {
        _Intensity("Effect Intensity", Range(0, 1)) = 1
        [IntRange] _CharacterHeight("Character Height (Pixels)", Range(6, 24)) = 8
        _GlyphBrightness("Character Brightness", Range(0.5, 4)) = 1.8
        _BackgroundStrength("Scene Behind Characters", Range(0, 0.5)) = 0.16
        _ColorDetail("Local Color Detail", Range(0, 1)) = 0.35
        _Saturation("Color Saturation", Range(0, 2)) = 1
        _Exposure("Exposure (Stops)", Range(-2, 2)) = 0.15
        _Density("Character Density", Range(0.25, 2)) = 1.15
        _ShadowCutoff("Empty Dark Cells", Range(0, 0.2)) = 0.012
        _Vignette("Vignette", Range(0, 1)) = 0.12
    }
    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" }
        ZTest Always ZWrite Off Cull Off
        Pass
        {
            Name "Color ASCII"
            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float _Intensity, _CharacterHeight, _GlyphBrightness, _BackgroundStrength;
                float _ColorDetail, _Saturation, _Exposure, _Density;
                float _ShadowCutoff, _Vignette;
            CBUFFER_END

            // Each character is a real 5x7 bitmap, packed into two integers.
            // No font texture, time seed, scrolling text, or external dependency.
            #include "ColorAsciiGlyphs.hlsl"

            float3 DisplayColor(float3 color)
            {
                color = max(0.0, color * exp2(_Exposure));
                #if !defined(UNITY_COLORSPACE_GAMMA)
                    color = LinearToSRGB(color);
                #endif
                float luma = dot(color, float3(0.2126, 0.7152, 0.0722));
                return max(0.0, lerp(luma.xxx, color, _Saturation));
            }

            float2 SourceUV(float2 uv)
            {
                return saturate(uv) * _BlitScaleBias.xy + _BlitScaleBias.zw;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                half4 original = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, input.texcoord);
                float2 screenUV = (input.texcoord - _BlitScaleBias.zw) / _BlitScaleBias.xy;
                float2 screenSize = _ScaledScreenParams.xy;
                float height = max(6.0, floor(_CharacterHeight + 0.5));
                float2 cellSize = float2(max(4.0, floor(height * 0.75)), height);
                float2 cell = floor(screenUV * screenSize / cellSize);
                float2 center = (cell + 0.5) * cellSize / screenSize;
                float2 offset = cellSize * 0.23 / screenSize;

                // Four samples retain small colored lights and thin silhouettes.
                float3 a = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, SourceUV(center + offset)).rgb;
                float3 b = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, SourceUV(center - offset)).rgb;
                float3 c = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, SourceUV(center + float2(offset.x, -offset.y))).rgb;
                float3 d = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, SourceUV(center + float2(-offset.x, offset.y))).rgb;
                float3 color = DisplayColor((a + b + c + d) * 0.25);
                float luma = dot(color, float3(0.2126, 0.7152, 0.0722));
                float grain = frac(52.9829189 * frac(dot(cell, float2(0.06711056, 0.00583715)))) - 0.5;
                uint glyph = (uint)clamp(floor(pow(saturate(luma * _Density), 0.72) * 15.0 + 0.5 + grain * 0.4), 0.0, 15.0);

                float2 local = frac(screenUV * screenSize / cellSize);
                #if !UNITY_UV_STARTS_AT_TOP
                    local.y = 1.0 - local.y;
                #endif
                // A one-pixel gutter separates the letters at the default size.
                int2 glyphPixel = int2(floor(local * float2(6, 8)));
                float ink = 0.0;
                if (glyphPixel.x < 5 && glyphPixel.y < 7 && luma > _ShadowCutoff)
                {
                    uint bitIndex = (uint)(glyphPixel.y * 5 + glyphPixel.x);
                    uint2 bits = AsciiGlyph(glyph);
                    ink = bitIndex < 32u ? float((bits.x >> bitIndex) & 1u) : float((bits.y >> (bitIndex - 32u)) & 1u);
                }

                float3 localColor = DisplayColor(original.rgb);
                float3 inkColor = lerp(color, localColor, _ColorDetail);
                float3 result = lerp(localColor * _BackgroundStrength, saturate(inkColor * _GlyphBrightness), ink);
                float2 centered = screenUV * 2.0 - 1.0;
                result *= 1.0 - _Vignette * smoothstep(0.3, 1.6, dot(centered, centered));
                #if !defined(UNITY_COLORSPACE_GAMMA)
                    result = SRGBToLinear(result);
                #endif
                // Full bypass includes color, sampling, and the character grid.
                return half4(lerp(original.rgb, result, _Intensity), original.a);
            }
            ENDHLSL
        }
    }
    FallBack Off
}
