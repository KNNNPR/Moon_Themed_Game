using System;
using System.Collections.Generic;
using UnityEngine;

namespace MoonThemedGame.Repair
{
    [CreateAssetMenu(menuName = "Moon Game/Repair Robot")]
    public sealed class RepairRobotDefinition : ScriptableObject
    {
        public string displayName;
        public GameObject prefab;
        [Min(.01f)] public float deliveryScale = .5f;
        public RepairPartDefinition[] parts = Array.Empty<RepairPartDefinition>();

        public string DisplayName => string.IsNullOrWhiteSpace(displayName) && prefab ? prefab.name : displayName;

        public bool IsValid => prefab && parts != null && parts.Length > 0;

        public static void AppendParts(List<GameObject> result, RepairPartDefinition[] definitions)
        {
            if (definitions == null) return;
            foreach (var part in definitions)
                if (part != null && part.prefab && !result.Contains(part.prefab)) result.Add(part.prefab);
        }
    }
}
