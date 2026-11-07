using UnityEngine;
using UnityEngine.UIElements;

namespace MoonThemedGame.Computer
{
    [CreateAssetMenu(menuName = "Moon Game/Crazy Cosmonauts")]
    public sealed class CrazyCosmonautsContent : ScriptableObject
    {
        [Min(0)] public int price = 10;
        [Min(0)] public int waveReward = 3;
        [Min(0)] public int retryDelaySeconds = 180;
        public GameObject arenaPrefab, zombiePrefab, pistolPrefab;
        public VectorImage desktopIcon, healthIcon;
        public Material zombieMaterial;
        [Min(1)] public int startingHealth = 100;
        [Min(1)] public int contactDamage = 20;
        [Min(1)] public int firstWaveCount = 3;
        [Min(1)] public int addedPerWave = 2;
        [Min(1)] public int maximumAlive = 8;
        [Min(.2f)] public float spawnInterval = 1.1f;
        public string Title => Loc.Russian ? "Безумные Космонавты" : "Crazy Cosmonauts";
        public string Description => Loc.Russian ? "Выживи среди волн заражённых. +3 CR за волну." : "Survive waves of infected cosmonauts. +3 CR per wave.";
    }
}
