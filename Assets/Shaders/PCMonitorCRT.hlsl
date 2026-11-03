#ifndef MOON_PC_MONITOR_CRT_INCLUDED
#define MOON_PC_MONITOR_CRT_INCLUDED

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
    float2 uv = CrtContentUV(meshUV, time);
    float2 texel = _BaseMap_TexelSize.xy;
    half3 source = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, saturate(uv)).rgb;
    half3 glow = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, saturate(uv + float2(texel.x * 2.0, 0))).rgb;
    glow += SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, saturate(uv - float2(texel.x * 2.0, 0))).rgb;
    glow += SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, saturate(uv + float2(0, texel.y * 2.0))).rgb;
    glow += SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, saturate(uv - float2(0, texel.y * 2.0))).rgb;
    glow *= 0.25;
    half3 wideGlow = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, saturate(uv + float2(texel.x * 5.0, texel.y * 3.0))).rgb;
    wideGlow += SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, saturate(uv - float2(texel.x * 5.0, texel.y * 3.0))).rgb;
    wideGlow *= 0.5;
    half scanline = 1.0 - _Scanlines * (0.5 + 0.5 * cos(uv.y * _BaseMap_TexelSize.w * 3.14159265));
    half flicker = 1.0 + sin(time * 39.0) * _Flicker;
    half roll = 1.0 - 0.025 * pow(0.5 + 0.5 * sin(uv.y * 6.2831853 - time * 0.7), 12.0);
    half3 color = (source + glow * _Glow + wideGlow * _Glow * 0.35) * _Brightness * scanline * flicker * roll;
    float2 p = abs(meshUV * 2.0 - 1.0);
    float2 q = p - float2(0.88, 0.84);
    float rounded = length(max(q, 0.0)) + min(max(q.x, q.y), 0.0) - 0.12;
    half mask = 1.0 - smoothstep(-0.012, 0.005, rounded);
    mask *= step(0.0, uv.x) * step(uv.x, 1.0) * step(0.0, uv.y) * step(uv.y, 1.0);
    float3 view = GetWorldSpaceNormalizeViewDir(positionWS);
    half fresnel = pow(1.0 - saturate(dot(normalWS, view)), 3.0);
    half sheen = pow(saturate(1.0 - length((meshUV - float2(0.19, 0.93)) * float2(1.0, 1.8))), 6.0);
    half edgeShade = 1.0 - 0.18 * dot(meshUV * 2.0 - 1.0, meshUV * 2.0 - 1.0);
    return color * mask * edgeShade + _GlassTint.rgb * (0.12 + fresnel * 0.7 + sheen * 0.6);
}
#endif
