using System;
using UnityEngine;

namespace MoonThemedGame.Rendering
{
    public enum VisualStyle { Retro, ColorAscii }

    [CreateAssetMenu(menuName = "Moon Game/Visual Style Settings")]
    public sealed class VisualStyleSettings : ScriptableObject
    {
        [SerializeField] VisualStyle defaultStyle = VisualStyle.ColorAscii;
        [SerializeField] Material retroMaterial;
        [SerializeField] Material asciiMaterial;
        [NonSerialized] VisualStyle? sessionStyle;
        [NonSerialized] Material sessionAscii;

        public VisualStyle DefaultStyle => defaultStyle;
        public VisualStyle CurrentStyle => sessionStyle ?? defaultStyle;
        public Material RetroMaterial => retroMaterial;
        public Material AsciiMaterial => asciiMaterial;
        public Material CurrentMaterial => CurrentStyle == VisualStyle.ColorAscii ? (sessionAscii ? sessionAscii : asciiMaterial) : retroMaterial;

        // Temporary ASCII material (e.g. the robot feed); null restores the saved one.
        public void OverrideAscii(Material material) => sessionAscii = material;

        // Session changes never modify the saved start mode or material assets.
        public void SetStyle(VisualStyle style) => sessionStyle = style;
        public void Toggle() => SetStyle(CurrentStyle == VisualStyle.Retro ? VisualStyle.ColorAscii : VisualStyle.Retro);
        public void ResetSession()
        {
            sessionStyle = null;
            sessionAscii = null;
        }

        public void Configure(Material retro, Material ascii)
        {
            retroMaterial = retro;
            asciiMaterial = ascii;
        }

        public void SetDefaultStyle(VisualStyle style)
        {
            defaultStyle = style;
            ResetSession();
        }

        void OnEnable() => ResetSession();
    }
}
