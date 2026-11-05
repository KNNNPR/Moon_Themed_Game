using UnityEngine;

namespace MoonThemedGame.Interactions
{
    public sealed class AppleStationAssets : ScriptableObject
    {
        public Mesh stationMesh;
        // Readable collision copy: runtime setup also works in a standalone build.
        public Mesh collisionMesh;
        public GameObject applePrefab;
        public RuntimeAnimatorController animatorController;
        public Vector3 localDropPosition;
        public Vector3 localOutwardDirection;
    }
}
