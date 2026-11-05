using UnityEngine;

namespace MoonThemedGame.Rendering
{
    [CreateAssetMenu(menuName = "Moon Game/Lunar Atmosphere")]
    public sealed class LunarAtmosphereSettings : ScriptableObject
    {
        public bool lookEnabled = true;
        [Header("Permanent exterior haze (metres)")]
        [Min(0)] public float clearDistance = 10;
        [Min(0)] public float distanceDensity = 0.012f;
        [Min(0)] public float groundDensity = 0.008f;
        public float groundHeight = 2;
        [Min(0.001f)] public float heightFalloff = 0.15f;
        public Color hazeColor = new Color(0.003f, 0.004f, 0.005f, 1);
        [Range(0, 1)] public float maximumOpacity = 0.985f;
        [Header("Restrained palette / light")]
        [Range(0, 1)] public float saturation = 0.48f;
        [Range(0, 1)] public float shadowBrightness = 0.78f;
        [Range(0, 0.3f)] public float lightHalo = 0.07f;
        [Header("Cel shading")]
        [Range(2, 8)] public int lightingBands = 3;
        [Range(0, 1)] public float ambientMultiplier = 0.72f;
        [Range(0, 1)] public float textureSimplification = 0.4f;
        [Range(2, 16)] public int textureLevels = 6;
        [Range(1, 4)] public int bandPatternSize = 2;
        public Color shadowTint = new Color(0.64f, 0.72f, 0.8f, 1);

        static LunarAtmosphereSettings current;
        public static LunarAtmosphereSettings Current => current ? current : current = Resources.Load<LunarAtmosphereSettings>("LunarAtmosphereSettings");

        public void ApplyCelGlobals()
        {
            Shader.SetGlobalFloat("_LunarLookEnabled", lookEnabled ? 1 : 0);
            Shader.SetGlobalVector("_LunarCel", new Vector4(lightingBands, ambientMultiplier, textureSimplification, textureLevels));
            Shader.SetGlobalFloat("_LunarBandPixelSize", bandPatternSize);
            Shader.SetGlobalColor("_LunarShadowTint", shadowTint);
        }
    }
}
