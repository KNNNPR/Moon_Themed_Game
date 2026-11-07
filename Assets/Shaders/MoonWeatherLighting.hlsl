#ifndef MOON_WEATHER_LIGHTING_INCLUDED
#define MOON_WEATHER_LIGHTING_INCLUDED
float _MoonDeepNight;
int _MoonWeatherShelterCount;
float4 _MoonWeatherShelterMins[4],_MoonWeatherShelterMaxs[4];
float MoonExterior(float3 position)
{
    float exterior=1;
    [unroll] for(int i=0;i<4;i++)
    {
        if(i>=_MoonWeatherShelterCount)break;
        float3 edge=min(position-_MoonWeatherShelterMins[i].xyz,_MoonWeatherShelterMaxs[i].xyz-position);
        exterior*=1-smoothstep(-0.25,0.5,min(edge.x,min(edge.y,edge.z)));
    }
    return exterior;
}
#endif
