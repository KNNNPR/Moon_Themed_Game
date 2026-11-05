#ifndef OLD_TECH_RETRO_CEL_FORWARD_INCLUDED
#define OLD_TECH_RETRO_CEL_FORWARD_INCLUDED

#include "RetroCelInput.hlsl"
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
// Global art direction; material textures, emission and per-material settings remain editable.
float _LunarLookEnabled, _LunarBandPixelSize;
float4 _LunarCel;
half4 _LunarShadowTint;
#if defined(OLD_TECH_CRT_SCREEN)
    #include "CRTScreenEffect.hlsl"
#endif

struct CelAttributes
{
    float4 positionOS : POSITION;
    float3 normalOS : NORMAL;
    float2 uv : TEXCOORD0;
    float2 lightmapUV : TEXCOORD1;
    UNITY_VERTEX_INPUT_INSTANCE_ID
};

struct CelVaryings
{
    float4 positionCS : SV_POSITION;
    float3 positionWS : TEXCOORD0;
    half3 normalWS : TEXCOORD1;
    float2 uv : TEXCOORD2;
    half fogFactor : TEXCOORD3;
    DECLARE_LIGHTMAP_OR_SH(lightmapUV, vertexSH, 4);
    UNITY_VERTEX_INPUT_INSTANCE_ID
    UNITY_VERTEX_OUTPUT_STEREO
};

CelVaryings CelVertex(CelAttributes input)
{
    CelVaryings output = (CelVaryings)0;
    UNITY_SETUP_INSTANCE_ID(input);
    UNITY_TRANSFER_INSTANCE_ID(input, output);
    UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
    VertexPositionInputs position = GetVertexPositionInputs(input.positionOS.xyz);
    output.positionCS = position.positionCS;
    output.positionWS = position.positionWS;
    output.normalWS = TransformObjectToWorldNormal(input.normalOS);
    output.uv = TRANSFORM_TEX(input.uv, _BaseMap);
    output.fogFactor = ComputeFogFactor(position.positionCS.z);
    OUTPUT_LIGHTMAP_UV(input.lightmapUV, unity_LightmapST, output.lightmapUV);
    OUTPUT_SH(output.normalWS, output.vertexSH);
    return output;
}

half BandNoise(float2 pixel)
{
    if (_LunarLookEnabled > 0.5)
    {
        uint2 p = uint2(floor(pixel / max(1, _LunarBandPixelSize)));
        uint2 low = p & 1u, high = (p >> 1u) & 1u;
        uint a = ((low.x ^ low.y) << 1u) | low.y;
        uint b = ((high.x ^ high.y) << 1u) | high.y;
        return (float(a * 4u + b) + 0.5) / 16.0;
    }
    // Legacy static grain is retained when the lunar look is disabled.
    return frac(52.9829189 * frac(dot(floor(pixel), float2(0.06711056, 0.00583715))));
}

half3 CelLight(Light light, half3 normalWS, half noise)
{
    half steps = max(2.0h, floor(_LunarLookEnabled > 0.5 ? _LunarCel.x : _ShadeSteps)) - 1.0h;
    half ndotl = saturate(dot(normalWS, light.direction));
    half shadow = light.shadowAttenuation;
    // Include light intensity BEFORE quantizing attenuation. Otherwise a bright
    // point light incorrectly disappears a few metres from its source.
    float intensity = max(Luminance(light.color), 0.0001);
    half level = saturate(ndotl * shadow * light.distanceAttenuation * intensity);
    half band = saturate(floor(level * steps + 0.5h + noise * _BandDither) / steps);
    return (light.color / intensity) * band;
}

#if defined(OLD_TECH_CEL_GLOSS)
half3 CelGlossLight(Light light,half3 normal,half3 view,half3 albedo)
{
    half3 halfway=SafeNormalize(light.direction+view);
    half shine=pow(saturate(dot(normal,halfway)),exp2(3+_Smoothness*6));
    shine*=step(.001,dot(normal,light.direction))*light.shadowAttenuation*light.distanceAttenuation;
    return light.color*shine*lerp(half3(.08,.08,.08),albedo,_Metallic);
}
#endif
half4 CelFragment(CelVaryings input) : SV_Target
{
    UNITY_SETUP_INSTANCE_ID(input);
    UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
    half4 texel = SampleAlbedoAlpha(input.uv, TEXTURE2D_ARGS(_BaseMap, sampler_BaseMap));
    half alpha = Alpha(texel.a, _BaseColor, _Cutoff);
    half3 albedo = texel.rgb * _BaseColor.rgb;
    if (_LunarLookEnabled > 0.5)
    {
        half brightness = max(0.0001h, Luminance(albedo));
        half levels = max(2, _LunarCel.w) - 1;
        half band = floor(sqrt(brightness) * levels + 0.5h) / levels;
        albedo *= lerp(1, band * band / brightness, _LunarCel.z);
    }
    half3 normalWS = NormalizeNormalPerPixel(input.normalWS);
    half noise = BandNoise(input.positionCS.xy) - 0.5h;

    InputData inputData = (InputData)0;
    inputData.positionWS = input.positionWS;
    inputData.normalWS = normalWS;
    inputData.viewDirectionWS = GetWorldSpaceNormalizeViewDir(input.positionWS);
    inputData.normalizedScreenSpaceUV = GetNormalizedScreenSpaceUV(input.positionCS);
    #if defined(_MAIN_LIGHT_SHADOWS_SCREEN) && !defined(_SURFACE_TYPE_TRANSPARENT)
        inputData.shadowCoord = ComputeScreenPos(TransformWorldToHClip(input.positionWS));
    #else
        inputData.shadowCoord = TransformWorldToShadowCoord(input.positionWS);
    #endif
    half4 shadowMask = SAMPLE_SHADOWMASK(input.lightmapUV);
    Light mainLight = GetMainLight(inputData.shadowCoord, input.positionWS, shadowMask);
    half3 direct = CelLight(mainLight, normalWS, noise);

    #if defined(_ADDITIONAL_LIGHTS) || defined(_ADDITIONAL_LIGHTS_VERTEX)
        #if USE_CLUSTER_LIGHT_LOOP
            [loop] for (uint lightIndex = 0; lightIndex < min(URP_FP_DIRECTIONAL_LIGHTS_COUNT, MAX_VISIBLE_LIGHTS); ++lightIndex)
            {
                CLUSTER_LIGHT_LOOP_SUBTRACTIVE_LIGHT_CHECK
                direct += CelLight(GetAdditionalLight(lightIndex, input.positionWS, shadowMask), normalWS, noise);
            }
        #endif
        uint lightCount = GetAdditionalLightsCount();
        LIGHT_LOOP_BEGIN(lightCount)
            direct += CelLight(GetAdditionalLight(lightIndex, input.positionWS, shadowMask), normalWS, noise);
        LIGHT_LOOP_END
    #endif

    AmbientOcclusionFactor ao = GetScreenSpaceAmbientOcclusion(inputData.normalizedScreenSpaceUV);
    half3 shadowTint = _LunarLookEnabled > 0.5 ? lerp(_ShadowTint.rgb, _LunarShadowTint.rgb, 0.7h) : _ShadowTint.rgb;
    half ambientScale = _LunarLookEnabled > 0.5 ? _LunarCel.y : 1;
    half3 ambient = SAMPLE_GI(input.lightmapUV, input.vertexSH, normalWS) * _AmbientStrength * ambientScale;
    ambient = max(ambient, _ShadowFloor * shadowTint * ambientScale);
    half lightAmount = saturate(Luminance(direct));
    half3 tint = lerp(shadowTint, half3(1, 1, 1), lightAmount);
    half3 color = albedo * tint * (ambient * ao.indirectAmbientOcclusion + direct * ao.directAmbientOcclusion);
    #if defined(OLD_TECH_CEL_GLOSS)
        // Keep the same cel diffuse bands; add only a narrow wet highlight.
        half3 gloss = CelGlossLight(mainLight,normalWS,inputData.viewDirectionWS,albedo);
        #if defined(_ADDITIONAL_LIGHTS) || defined(_ADDITIONAL_LIGHTS_VERTEX)
            #if USE_CLUSTER_LIGHT_LOOP
                [loop] for (uint lightIndex=0;lightIndex<min(URP_FP_DIRECTIONAL_LIGHTS_COUNT,MAX_VISIBLE_LIGHTS);++lightIndex)
                {
                    CLUSTER_LIGHT_LOOP_SUBTRACTIVE_LIGHT_CHECK
                    gloss += CelGlossLight(GetAdditionalLight(lightIndex,input.positionWS,shadowMask),normalWS,inputData.viewDirectionWS,albedo);
                }
            #endif
            uint glossCount=GetAdditionalLightsCount();
            LIGHT_LOOP_BEGIN(glossCount)
                gloss += CelGlossLight(GetAdditionalLight(lightIndex,input.positionWS,shadowMask),normalWS,inputData.viewDirectionWS,albedo);
            LIGHT_LOOP_END
        #endif
        color += gloss * ao.directAmbientOcclusion;
    #endif
    color += SAMPLE_TEXTURE2D(_EmissionMap, sampler_EmissionMap, input.uv).rgb * _EmissionColor.rgb;
    #if defined(OLD_TECH_CRT_SCREEN)
        half screenMask;
        half3 screen = CRTScreenColor(input.uv, screenMask);
        color = lerp(color, screen, screenMask);
    #endif
    return half4(MixFog(color, input.fogFactor), alpha);
}

#endif
