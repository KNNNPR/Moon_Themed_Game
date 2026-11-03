using UnityEngine;
using UnityEngine.UIElements;

namespace MoonThemedGame.Computer
{
    [CreateAssetMenu(menuName = "Moon Game/PC Monitor Assets")]
    public sealed class PcMonitorAssets : ScriptableObject
    {
        public VisualTreeAsset layout;
        public StyleSheet styleSheet;
        public ThemeStyleSheet theme;
        public Font font;
        public VectorImage shopIcon;
        public VectorImage scavengerIcon;
        public VectorImage messagesIcon;
        public VectorImage coinIcon;
        public VectorImage cursorIcon;
        public TextAsset translations;
        public Material screenMaterial;
        public Vector2Int resolution = new Vector2Int(960, 512);
    }
}
