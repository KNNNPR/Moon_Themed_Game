using UnityEngine;
using UnityEngine.UIElements;

namespace MoonThemedGame.Weather
{
    [CreateAssetMenu(menuName = "Moon Game/Weather Assets")]
    public sealed class MoonWeatherAssets : ScriptableObject
    {
        public VisualTreeAsset layout;
        public StyleSheet styleSheet;
        public ThemeStyleSheet theme;
        public Font font;
        public VectorImage sunny, dust, toxicity, meteors;
        public VectorImage night,snow,wind;
        public VectorImage horde;
        public Material screenMaterial, atmosphereMaterial, particleMaterial, warningMaterial;
        public GameObject meteoritePrefab;
        public Vector2Int resolution = new Vector2Int(640, 1080);
        public VectorImage Icon(WeatherKind kind) => kind switch
        {
            WeatherKind.MoonDust => dust, WeatherKind.Radiation => toxicity,
            WeatherKind.MeteoriteRain => meteors,WeatherKind.DeepNight=>night,
            WeatherKind.CryogenicFront=>snow,WeatherKind.AshenCalm=>wind,WeatherKind.Horde=>horde?horde:toxicity, _ => sunny
        };
    }
}
