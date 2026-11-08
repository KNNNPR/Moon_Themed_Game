Shader "Moon Game/Operator Intoxication"
{
    Properties { _Strength("Strength",Range(0,1))=0 _Phase("Phase",Float)=0 _MotionScale("Motion",Float)=1 _FovScale("FOV Motion",Float)=1 }
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
            float _Strength,_Phase,_MotionScale,_FovScale;
            half4 Frag(Varyings input):SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                float s=saturate(_Strength),t=_Phase;
                float2 p=input.texcoord-0.5;
                float roll=sin(t*0.83)*0.032*s*_MotionScale;
                float sine=sin(roll),cosine=cos(roll);
                p=float2(cosine*p.x-sine*p.y,sine*p.x+cosine*p.y);
                // Slight zoom covers the rotating edges; breathing/waves remain slow and bounded.
                p/=1+0.055*s*_MotionScale+sin(t*0.61)*0.012*s*_MotionScale*_FovScale;
                float2 uv=p+0.5;
                uv+=float2(sin(t*1.07+uv.y*6),cos(t*0.79+uv.x*5))*float2(0.006,0.004)*s*_MotionScale;
                float2 radius=_BlitTexture_TexelSize.xy*(0.4+3.2*s)*s;
                half4 color=SAMPLE_TEXTURE2D_X(_BlitTexture,sampler_LinearClamp,uv)*0.4;
                color+=SAMPLE_TEXTURE2D_X(_BlitTexture,sampler_LinearClamp,uv+float2(radius.x,0))*0.1;
                color+=SAMPLE_TEXTURE2D_X(_BlitTexture,sampler_LinearClamp,uv-float2(radius.x,0))*0.1;
                color+=SAMPLE_TEXTURE2D_X(_BlitTexture,sampler_LinearClamp,uv+float2(0,radius.y))*0.1;
                color+=SAMPLE_TEXTURE2D_X(_BlitTexture,sampler_LinearClamp,uv-float2(0,radius.y))*0.1;
                color+=SAMPLE_TEXTURE2D_X(_BlitTexture,sampler_LinearClamp,uv+radius)*0.05;
                color+=SAMPLE_TEXTURE2D_X(_BlitTexture,sampler_LinearClamp,uv-radius)*0.05;
                color+=SAMPLE_TEXTURE2D_X(_BlitTexture,sampler_LinearClamp,uv+float2(radius.x,-radius.y))*0.05;
                color+=SAMPLE_TEXTURE2D_X(_BlitTexture,sampler_LinearClamp,uv+float2(-radius.x,radius.y))*0.05;
                float2 ghost=float2(sin(t*0.67),cos(t*0.93))*0.005*s*s*_MotionScale;
                color.rgb=lerp(color.rgb,SAMPLE_TEXTURE2D_X(_BlitTexture,sampler_LinearClamp,uv+ghost).rgb,0.18*s);
                return color;
            }
            ENDHLSL
        }
    }
}
