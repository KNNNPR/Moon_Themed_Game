#ifndef MOON_WEATHER_OVERLAY_INCLUDED
#define MOON_WEATHER_OVERLAY_INCLUDED
#include "MoonWeatherLighting.hlsl"
float4 _MoonWeatherCamera;
float MoonWeatherHash(float2 cell){return frac(sin(dot(cell,float2(127.1,311.7)))*43758.5453);}
float MoonIceNoise(float2 p)
{
    float2 cell=floor(p),f=frac(p);f=f*f*(3-2*f);
    return lerp(lerp(MoonWeatherHash(cell),MoonWeatherHash(cell+float2(1,0)),f.x),
        lerp(MoonWeatherHash(cell+float2(0,1)),MoonWeatherHash(cell+1),f.x),f.y);
}
float3 MoonWeatherFinish(float3 color,float2 uv)
{
    if(_MoonDeepNight>0.0001)
    {
        float depth=SampleSceneDepth(uv);
        #if UNITY_REVERSED_Z
            bool sky=depth<0.00001;
        #else
            bool sky=depth>0.99999;
            depth=lerp(UNITY_NEAR_CLIP_VALUE,1,depth);
        #endif
        if(sky)
        {
            float3 worldPoint=ComputeWorldSpacePosition(uv,depth,UNITY_MATRIX_I_VP);
            float3 direction=normalize(worldPoint-GetCameraPositionWS());
            float2 stars=float2(atan2(direction.z,direction.x)/6.2831853+0.5,asin(clamp(direction.y,-1,1))/3.14159265+0.5)*float2(600,300);
            float hash=MoonWeatherHash(floor(stars));
            float star=(1-smoothstep(0.03,0.22,length(frac(stars)-0.5)))*step(0.996,hash)*smoothstep(-0.03,0.08,direction.y);
            color+=float3(0.75,0.83,1)*star*_MoonDeepNight;
        }
    }
    float cold=_MoonWeatherCamera.y;
    if(cold>0.0001)
    {
        color=lerp(color,color*float3(.72,.89,1.18),cold*.55);
        float edge=min(min(uv.x,1-uv.x),min(uv.y,1-uv.y));
        float frostTime=_MoonWeatherCamera.z;
        // Slow domain warping grows and melts patches without scrolling the fine crystal grain.
        float2 flow=float2(frostTime*.014,sin(frostTime*.06)*.22);
        float breathing=.72+.28*sin(frostTime*.16+MoonIceNoise(uv*9)*6.2831853);
        float growth=MoonIceNoise(uv*17+flow);
        float front=(.025+.05*growth+.015*MoonIceNoise(uv*63-flow*.45))*breathing;
        front*=lerp(.2,1,smoothstep(0,1,cold));
        float frost=1-smoothstep(front-.015,front+.025,edge);
        float crystal=MoonWeatherHash(floor(uv*float2(270,180)));
        float veins=pow(saturate(1-abs(sin(uv.x*170+sin(uv.y*95)*4+growth*1.6))),8);
        float thaw=smoothstep(.1,.9,.5+.5*sin(frostTime*.13+MoonIceNoise(uv*8+float2(3,7))*8));
        float patches=lerp(.18,1,thaw)*(.8+.2*MoonIceNoise(uv*11+flow*.65));
        color=lerp(color,float3(.48,.65,.8),cold*frost*(.42+crystal*.25+veins*.18)*patches);
    }
    return color;
}
#endif
