Shader "Moon Game/Scavenger Feed CRT"
{
    Properties
    {
        _MainTex("Feed", 2D) = "black" {}
        _Overlay("Text Overlay", 2D) = "black" {}
        _Curvature("Curvature", Range(0, 0.4)) = 0.12
        _Scanlines("Scanlines", Range(0, 1)) = 0.35
        _Mask("Phosphor Mask", Range(0, 1)) = 0.18
        _Glow("Glow", Range(0, 2)) = 0.6
        _Vignette("Vignette", Range(0, 1)) = 0.45
        _Flicker("Flicker", Range(0, 0.2)) = 0.03
        _FlipY("Flip Feed Y", Float) = 0
        _FlipOverlay("Flip Overlay Y", Float) = 0
    }
    SubShader
    {
        ZTest Always ZWrite Off Cull Off
        Blend Off
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            sampler2D _MainTex, _Overlay;
            float4 _MainTex_TexelSize;
            float _Curvature, _Scanlines, _Mask, _Glow, _Vignette, _Flicker, _FlipY, _FlipOverlay;

            struct v2f { float4 pos : SV_POSITION; float2 uv : TEXCOORD0; };

            v2f vert(appdata_img v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.uv = v.texcoord;
                return o;
            }

            // Same mapping as ScavengerFeedHud.ToSource, so mouse clicks land where they are drawn.
            float2 Bend(float2 uv)
            {
                float2 c = uv * 2 - 1;
                c *= 1 + _Curvature * dot(c, c);
                return c * 0.5 + 0.5;
            }

            // Camera picture with the GUI text laid over it (the text target is premultiplied by its blending).
            float3 Tap(float2 uv)
            {
                float2 feedUv = uv, textUv = uv;
                if (_FlipY > 0.5) feedUv.y = 1 - feedUv.y;
                if (_FlipOverlay > 0.5) textUv.y = 1 - textUv.y;
                float3 feed = tex2D(_MainTex, feedUv).rgb;
                float4 text = tex2D(_Overlay, textUv);
                return feed * (1 - saturate(sqrt(text.a))) + text.rgb;
            }

            float4 frag(v2f i) : SV_Target
            {
                float2 uv = Bend(i.uv);
                if (any(uv < 0) || any(uv > 1)) return float4(0, 0, 0, 1);

                float2 px = _MainTex_TexelSize.xy;
                float3 color;
                color.r = Tap(uv + float2(px.x * 1.2, 0)).r;
                color.g = Tap(uv).g;
                color.b = Tap(uv - float2(px.x * 1.2, 0)).b;

                float3 glow = 0;
                for (int k = 1; k <= 3; k++)
                {
                    float2 o = px * k * 2.5;
                    glow += Tap(uv + float2(o.x, 0)) + Tap(uv - float2(o.x, 0)) + Tap(uv + float2(0, o.y)) + Tap(uv - float2(0, o.y));
                }
                color += glow / 12 * _Glow;

                float line = sin(uv.y * _MainTex_TexelSize.w * 3.14159);
                color *= 1 - _Scanlines * (0.5 - 0.5 * line);
                float column = frac(i.pos.x / 3);
                float3 mask = column < 0.333 ? float3(1, 0.7, 0.7) : column < 0.666 ? float3(0.7, 1, 0.7) : float3(0.7, 0.7, 1);
                color *= lerp(1, mask, _Mask);

                float2 c = uv * 2 - 1;
                color *= 1 - _Vignette * smoothstep(0.4, 1.5, dot(c, c));
                float2 edge = smoothstep(0, 0.012, uv) * smoothstep(0, 0.012, 1 - uv);
                color *= edge.x * edge.y;
                color *= 1 - _Flicker * (0.5 + 0.5 * sin(_Time.y * 60));
                return float4(color, 1);
            }
            ENDCG
        }
    }
    FallBack Off
}
