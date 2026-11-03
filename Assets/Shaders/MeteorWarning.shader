Shader "Moon Game/Meteor Impact Warning"
{
    Properties { _Countdown("Countdown", Range(0,1)) = 1 _Opacity("Opacity", Range(0,1)) = 0.8 _Impact("Impact Ring", Float) = 0 }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "Queue"="Transparent+10" "RenderType"="Transparent" }
        Pass
        {
            Tags { "LightMode"="SRPDefaultUnlit" }
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off Cull Off
            // Readability mask shared with Notes: warning rings stay clear in Retro and ASCII.
            Stencil { Ref 1 WriteMask 1 Comp Always Pass Replace }
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            float _Countdown, _Opacity, _Impact;
            struct Attributes { float4 positionOS:POSITION; float2 uv:TEXCOORD0; };
            struct Varyings { float4 positionCS:SV_POSITION; float2 uv:TEXCOORD0; };
            Varyings Vert(Attributes input) { Varyings output; output.positionCS = TransformObjectToHClip(input.positionOS.xyz); output.uv = input.uv; return output; }
            half4 Frag(Varyings input):SV_Target
            {
                float2 p = input.uv * 2 - 1;
                float radius = length(p), width = max(fwidth(radius) * 1.5, 0.008);
                float ring = 1 - smoothstep(0.018, 0.018 + width, abs(radius - 0.84));
                float angle = frac(atan2(p.x,p.y) / 6.2831853 + 1);
                float progress = (1 - smoothstep(0.016, 0.016 + width, abs(radius - 0.71))) * step(angle, 1 - _Countdown);
                float cross = (1 - smoothstep(0.01, 0.01 + width, min(abs(p.x),abs(p.y)))) * (1 - smoothstep(0.16,0.2,radius));
                float pulse = 0.7 + 0.3 * sin(_Time.y * lerp(15,5,_Countdown));
                float alpha = _Opacity * saturate(ring + (1 - _Impact) * (cross + progress)) * pulse;
                clip(alpha - 0.015);
                return half4(1.5, 0.42, 0.075, alpha);
            }
            ENDHLSL
        }
    }
}
