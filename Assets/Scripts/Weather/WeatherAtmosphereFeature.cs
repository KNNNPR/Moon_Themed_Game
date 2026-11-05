using System;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using MoonThemedGame.Rendering;

namespace MoonThemedGame.Weather
{
    public sealed class WeatherAtmosphereFeature : FullScreenPassRendererFeature
    {
        readonly Vector4[] minimums = new Vector4[4], maximums = new Vector4[4];
        MoonWeatherSystem editorWeather;
        // Per-camera fog multiplier (the SCAVENGER robot's fog lamps thin the dust in its feed).
        public static Func<Camera, float> DensityScale;
        public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
        {
            var look = LunarAtmosphereSettings.Current;
            if (look) look.ApplyCelGlobals();
            else Shader.SetGlobalFloat("_LunarLookEnabled", 0);
            var weather = MoonWeatherSystem.Active;
            if (!weather && !Application.isPlaying)
                weather = editorWeather ? editorWeather : editorWeather = UnityEngine.Object.FindFirstObjectByType<MoonWeatherSystem>();
            var assets = weather && weather.assets ? weather.assets : Resources.Load<MoonWeatherAssets>("MoonWeatherAssets");
            if (assets && assets.atmosphereMaterial) passMaterial = assets.atmosphereMaterial;
            bool repairFog=Shader.GetGlobalVector("_RepairBayFogMin").w>0;
            if (!passMaterial || (!repairFog && !(look && look.lookEnabled) && (!weather || weather.Density < 0.00001f))) return;
            int count = weather ? weather.ShelterCount : 0;
            for (int i = 0; i < count; i++)
            {
                minimums[i] = weather.GetShelter(i).min; maximums[i] = weather.GetShelter(i).max;
            }
            passMaterial.SetInt("_ShelterCount", count);
            passMaterial.SetVectorArray("_ShelterMins", minimums);
            passMaterial.SetVectorArray("_ShelterMaxs", maximums);
            float scale = DensityScale != null ? DensityScale(renderingData.cameraData.camera) : 1;
            passMaterial.SetFloat("_Density", weather ? weather.Density * scale : 0);
            passMaterial.SetFloat("_OpacityLimit", weather ? weather.OpacityLimit : 0);
            passMaterial.SetColor("_FogTint", weather ? weather.AtmosphereTint : Color.black);
            bool enabled = look && look.lookEnabled;
            passMaterial.SetVector("_Haze", enabled ? new Vector4(look.clearDistance, look.distanceDensity * scale, look.groundDensity * scale, look.maximumOpacity) : Vector4.zero);
            passMaterial.SetVector("_HeightHaze", enabled ? new Vector4(look.groundHeight, look.heightFalloff, 0, 0) : Vector4.zero);
            passMaterial.SetColor("_HazeColor", enabled ? look.hazeColor : Color.black);
            passMaterial.SetVector("_Palette", enabled ? new Vector4(look.saturation, look.shadowBrightness, look.lightHalo, 1) : new Vector4(1, 1, 0, 0));
            bindDepthStencilAttachment = true;
            base.AddRenderPasses(renderer, ref renderingData);
        }
    }
}
