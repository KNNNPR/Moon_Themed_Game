using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace MoonThemedGame.Rendering
{
    // Reuse URP 17's native RenderGraph/compatibility implementation. Exactly
    // one material is submitted, so ASCII never processes a dithered image.
    public sealed class VisualStyleRendererFeature : FullScreenPassRendererFeature
    {
        public const string FeatureName = "Visual Style (Retro / Color ASCII)";
        [SerializeField] VisualStyleSettings settings;
        public VisualStyleSettings Settings { get => settings; set => settings = value; }

        public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
        {
            if (settings) passMaterial = settings.CurrentMaterial;
            base.AddRenderPasses(renderer, ref renderingData);
        }
    }
}
