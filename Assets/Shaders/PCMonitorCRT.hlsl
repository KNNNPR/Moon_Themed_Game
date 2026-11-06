#ifndef MOON_PC_MONITOR_CRT_INCLUDED
#define MOON_PC_MONITOR_CRT_INCLUDED

half3 CrtSampleDisplay(float2 uv, float time)
{
    half3 color = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, saturate(uv)).rgb;
    #if defined(MOON_CHARGE_DISPLAY)
        float rate = max(_LetterFlickerRate, 0.1);
        float tick = floor(time * rate);
        float phase = frac(time * rate) / rate;
        float duration = min(_LetterFlickerDuration, 0.8 / rate);
        float chance = frac(sin(tick * 12.9898 + 71.17) * 43758.5453);
        float letters=max(1,_LetterCount);
        uint chosen = (uint)floor(frac(sin(tick * 78.233 + 19.31) * 19341.137) * letters);
        float start = frac(sin(tick * 39.346 + 3.71) * 27183.135) * max(0.0, 1.0 / rate - duration);
        float pulse = saturate(min(phase - start, start + duration - phase) / 0.008);
        float column = (uv.x - _LetterRect.x) / _LetterRect.z * letters;
        float inside = step(0.0, column) * (1.0 - step(letters, column));
        float selected = 1.0 - step(0.5, abs(floor(column) - (float)chosen));
        // One randomly chosen letter briefly loses brightness. Sampling the
        // same mask for every glow tap keeps its phosphor halo in sync.
        float flicker = pulse * inside * selected * step(chance, _LetterFlickerChance);
        color *= lerp(1.0, _LetterFlickerFloor, flicker);
    #endif
    return color;
}

float2 CrtContentUV(float2 uv, float time)
{
    float2 p = uv * 2.0 - 1.0;
    uv = 0.5 + p * (1.0 + _Curvature * dot(p, p)) * 0.5;
    uv.x += (sin(uv.y * 52.0 + time * _WaveSpeed) + 0.35 * sin(uv.y * 121.0 - time * _WaveSpeed * 0.63)) * _WaveStrength;
    uv.y += sin(time * 0.83 + uv.x * 7.0) * _VerticalDrift;
    return uv;
}

half3 CrtDisplay(float2 meshUV, half3 normalWS, float3 positionWS)
{
    float time = _PreviewTime >= 0.0 ? _PreviewTime : _Time.y;
    float2 contentUV = meshUV;
    #if defined(MOON_CRT_POWER)
        float2 powerSize = lerp(float2(1, 1), float2(0.003, 0.004),
            float2(smoothstep(0.60, 0.90, _PowerOff), smoothstep(0.0, 0.62, _PowerOff)));
        contentUV = (meshUV - 0.5) / powerSize + 0.5;
    #endif
    float2 uv = CrtContentUV(contentUV, time);
    float2 texel = _BaseMap_TexelSize.xy;
    half3 source = CrtSampleDisplay(uv, time);
    #if defined(MOON_CRT_TUBE)
        // Red and blue guns slightly misconverged, like an old tube.
        source.r = CrtSampleDisplay(uv + float2(texel.x * _Bleed, 0), time).r;
        source.b = CrtSampleDisplay(uv - float2(texel.x * _Bleed, 0), time).b;
    #endif
    half3 glow = CrtSampleDisplay(uv + float2(texel.x * 2.0, 0), time);
    glow += CrtSampleDisplay(uv - float2(texel.x * 2.0, 0), time);
    glow += CrtSampleDisplay(uv + float2(0, texel.y * 2.0), time);
    glow += CrtSampleDisplay(uv - float2(0, texel.y * 2.0), time);
    glow *= 0.25;
    half3 wideGlow = CrtSampleDisplay(uv + float2(texel.x * 5.0, texel.y * 3.0), time);
    wideGlow += CrtSampleDisplay(uv - float2(texel.x * 5.0, texel.y * 3.0), time);
    wideGlow *= 0.5;
    half scanline = 1.0 - _Scanlines * (0.5 + 0.5 * cos(uv.y * _BaseMap_TexelSize.w * 3.14159265));
    half flicker = 1.0 + sin(time * 39.0) * _Flicker;
    half roll = 1.0 - 0.025 * pow(0.5 + 0.5 * sin(uv.y * 6.2831853 - time * 0.7), 12.0);
    half3 color = (source + glow * _Glow + wideGlow * _Glow * 0.35) * _Brightness * scanline * flicker * roll;
    #if defined(MOON_CRT_TUBE)
        // Single-phosphor tube: the picture glows in one colour, a little of the original hue survives.
        half luma = dot(color, half3(0.2126, 0.7152, 0.0722));
        color = lerp(color, luma * _Phosphor.rgb * 1.35, _Monochrome);
        // Aperture grille: one RGB triad every three display pixels, and a darker rim of the tube.
        float triad = frac(uv.x * _BaseMap_TexelSize.z / 3.0);
        half3 grille = triad < 0.3333 ? half3(1.0, 0.55, 0.55) : triad < 0.6666 ? half3(0.55, 1.0, 0.55) : half3(0.55, 0.55, 1.0);
        color *= lerp(half3(1, 1, 1), grille, _Mask) * (1.0 + _Mask * 0.45);
        float2 tube = uv * 2.0 - 1.0;
        color *= 1.0 - _Vignette * smoothstep(0.35, 1.45, dot(tube, tube));
    #endif
    float2 p = abs(meshUV * 2.0 - 1.0);
    float2 q = p - float2(0.88, 0.84);
    float rounded = length(max(q, 0.0)) + min(max(q.x, q.y), 0.0) - 0.12;
    half mask = 1.0 - smoothstep(-0.012, 0.005, rounded);
    mask *= step(0.0, uv.x) * step(uv.x, 1.0) * step(0.0, uv.y) * step(uv.y, 1.0);
    #if defined(MOON_CRT_TUBE)
        // A powered tube is never pitch black: the phosphor glows faintly behind the image.
        color += _BlackLevel.rgb * scanline * flicker;
    #endif
    float3 view = GetWorldSpaceNormalizeViewDir(positionWS);
    half fresnel = pow(1.0 - saturate(dot(normalWS, view)), 3.0);
    half sheen = pow(saturate(1.0 - length((meshUV - float2(0.19, 0.93)) * float2(1.0, 1.8))), 6.0);
    half edgeShade = 1.0 - 0.18 * dot(meshUV * 2.0 - 1.0, meshUV * 2.0 - 1.0);
    color = color * mask * edgeShade;
    #if defined(MOON_CRT_POWER)
        float fade = 1.0 - smoothstep(0.88, 1.0, _PowerOff);
        float2 aa = max(fwidth(meshUV), float2(0.001, 0.001));
        float collapseGlow = exp(-pow((meshUV.y - 0.5) / max(0.0025, aa.y), 2.0));
        collapseGlow *= 1.0 - smoothstep(powerSize.x * 0.46, powerSize.x * 0.5 + aa.x, abs(meshUV.x - 0.5));
        float flash = smoothstep(0.25, 0.60, _PowerOff) * fade;
        color = color * fade + lerp(_Phosphor.rgb, half3(1, 1, 1), 0.7) * collapseGlow * flash * 3.0;
    #endif
    color += _GlassTint.rgb * (0.12 + fresnel * 0.7 + sheen * 0.6);
    #if defined(MOON_PC_HOVER)
        half rim = exp(-abs(rounded + 0.02) * 65.0);
        half pulse = 0.88 + 0.12 * sin(time * 2.5);
        color += half3(0.72, 0.85, 0.9) * _HoverHighlight * pulse * (rim + 0.025 * mask);
    #endif
    return color;
}
#endif
