using UnityEngine;

namespace MoonThemedGame.Computer
{
    public sealed class CrazyCosmonautsArena : MonoBehaviour
    {
        public Transform playerSpawn;
        public Transform[] zombieSpawns = new Transform[0];
        public Bounds playableBounds;
        public UnityEngine.AI.NavMeshData navigation;
    }
}
