using System;
using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace MoonThemedGame.Weather
{
    public sealed class WeatherAtmosphereFeature : FullScreenPassRendererFeature
    {
        readonly Vector4[] minimums = new Vector4[4], maximums = new Vector4[4];
        // Per-camera fog multiplier (the SCAVENGER robot's fog lamps thin the dust in its feed).
        public static Func<Camera, float> DensityScale;
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
            float scale = DensityScale != null ? DensityScale(renderingData.cameraData.camera) : 1;
            passMaterial.SetFloat("_Density", weather.Density * scale);
            passMaterial.SetFloat("_OpacityLimit", weather.OpacityLimit);
            passMaterial.SetColor("_FogTint", weather.AtmosphereTint);
            base.AddRenderPasses(renderer, ref renderingData);
        }
    }
}
