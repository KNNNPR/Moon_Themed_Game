using UnityEngine;
namespace MoonThemedGame.Repair
{
    public sealed class CosmonautBodyAssets : ScriptableObject
    {
        public GameObject model;
        public Mesh firstPersonMesh;
        public Material material;
        public float sourceFeetY,sourceEyeHeight;
    }
}
