using UnityEngine;
using UnityEngine.UIElements;

namespace MoonThemedGame.Computer
{
    [CreateAssetMenu(menuName="Moon Game/Main Menu Assets")]
    public sealed class MainMenuAssets : ScriptableObject
    {
        public StyleSheet screenStyle;
        public GameObject applePrefab;
        public Material previewMaterial;
        public Material titleMaterial;
        public GameObject titleModel;
        [Min(.1f)] public float appleDiameter=.46f;
        public string gameplayScene="Operator";
    }
}
