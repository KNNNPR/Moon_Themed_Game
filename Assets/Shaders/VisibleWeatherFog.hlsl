#ifndef MOON_VISIBLE_WEATHER_FOG
#define MOON_VISIBLE_WEATHER_FOG
float4 _VisibleWeather; // density, opacity limit
float4 _VisibleWeatherTint;
float4 _VisibleWeatherMins[4],_VisibleWeatherMaxs[4];
int _VisibleWeatherShelters;
float VisibleWeatherFog(float3 origin,float3 world,bool sky)
{
    if(_VisibleWeather.x<=0)return 0;
    float3 delta=world-origin;
    float distance=min(length(delta),180);
    float3 direction=normalize(delta+0.000001);
    float3 safe=(step(0,direction)*2-1)*max(abs(direction),0.00001);
    float2 intervals[4];
    [unroll] for(int i=0;i<4;i++)
    {
        intervals[i]=distance.xx;
        if(i>=_VisibleWeatherShelters)continue;
        float3 a=(_VisibleWeatherMins[i].xyz-origin)/safe,b=(_VisibleWeatherMaxs[i].xyz-origin)/safe;
        float3 lo=min(a,b),hi=max(a,b);
        float enter=max(0,max(lo.x,max(lo.y,lo.z))),leave=min(distance,min(hi.x,min(hi.y,hi.z)));
        if(leave>enter)intervals[i]=float2(enter,leave);
    }
    [unroll] for(int a=0;a<3;a++)[unroll] for(int b=a+1;b<4;b++)
        if(intervals[b].x<intervals[a].x){float2 swap=intervals[a];intervals[a]=intervals[b];intervals[b]=swap;}
    float cursor=0,outside=0;
    [unroll] for(int i=0;i<4;i++){outside+=max(0,intervals[i].x-cursor);cursor=max(cursor,intervals[i].y);}
    outside+=max(0,distance-cursor);
    float opacity=min(_VisibleWeather.y,1-exp(-outside*_VisibleWeather.x));
    if(sky)opacity*=1-smoothstep(0.02,0.25,direction.y);
    return opacity;
}
#endif
