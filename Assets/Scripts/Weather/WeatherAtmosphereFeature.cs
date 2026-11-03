using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace MoonThemedGame.Weather
{
    public sealed class WeatherAtmosphereFeature : FullScreenPassRendererFeature
    {
        readonly Vector4[] minimums = new Vector4[4], maximums = new Vector4[4];
        public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
        {
            var weather = MoonWeatherSystem.Active;
            if (!weather || weather.Density < 0.00001f || !weather.assets || !weather.assets.atmosphereMaterial) return;
            passMaterial = weather.assets.atmosphereMaterial;
            for (int i = 0; i < weather.ShelterCount; i++)
            {
                minimums[i] = weather.GetShelter(i).min; maximums[i] = weather.GetShelter(i).max;
            }
            passMaterial.SetInt("_ShelterCount", weather.ShelterCount);
            passMaterial.SetVectorArray("_ShelterMins", minimums);
            passMaterial.SetVectorArray("_ShelterMaxs", maximums);
            passMaterial.SetFloat("_Density", weather.Density);
            passMaterial.SetFloat("_OpacityLimit", weather.OpacityLimit);
            passMaterial.SetColor("_FogTint", weather.AtmosphereTint);
            base.AddRenderPasses(renderer, ref renderingData);
        }
    }
}
