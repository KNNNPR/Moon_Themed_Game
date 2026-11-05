using UnityEngine;

namespace MoonThemedGame.Scavenger
{
    // Serialized settings from the former drop-point effect, read once during migration.
    [DisallowMultipleComponent]
    public sealed class ScavengerBayBlackout : MonoBehaviour
    {
        [Min(0)] public float fogDensity=4;
        public Color fogColor=new Color(0.16f,0.17f,0.16f,1);
        [Min(0.01f)] public float edgeFade=0.45f;
    }
}
