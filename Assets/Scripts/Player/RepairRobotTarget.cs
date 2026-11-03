using System;
using System.Collections.Generic;
using System.Text;
using MoonThemedGame.Interactions;
using UnityEngine;

namespace MoonThemedGame.Repair
{
    [Serializable]
    public sealed class RepairPartDefinition
    {
        public GameObject prefab;
        public string displayName;
    }

    [DisallowMultipleComponent]
    public sealed class RepairRobotTarget : Interactable
    {
        sealed class Slot
        {
            public GameObject visual;
            public int part;
        }

        string description = "Диагностика робота…";
        public override string Prompt => description;
        public int MissingCount { get; private set; }

        public void Initialize(RepairPartDefinition[] parts, int minimum, int maximum, int seed)
        {
            var slots = new List<Slot>();
            // Parts are whole direct children. Headlights remains one assembly.
            foreach (Transform child in transform)
            {
                string name = child.name.Split('.')[0];
                for (int p = 0; p < parts.Length; p++)
                {
                    if (parts[p] == null || !parts[p].prefab ||
                        !string.Equals(name, parts[p].prefab.name, StringComparison.OrdinalIgnoreCase)) continue;
                    child.gameObject.SetActive(true);
                    slots.Add(new Slot { visual = child.gameObject, part = p });
                    break;
                }
            }
            if (slots.Count == 0)
                throw new InvalidOperationException("Robot has no part slots matching Prefabs/Parts.");

            var missing = RepairDamagePlan.Select(slots.Count, minimum, maximum, seed);
            foreach (int index in missing)
                slots[index].visual.SetActive(false); // Hides both the mesh and its colliders.

            // Derive diagnosis from actual visibility, not a separate random count.
            var counts = new int[parts.Length];
            MissingCount = 0;
            foreach (var slot in slots)
                if (!slot.visual.activeSelf) { counts[slot.part]++; MissingCount++; }
            var text = new StringBuilder("Не хватает деталей:");
            for (int i = 0; i < counts.Length; i++)
                if (counts[i] > 0)
                    text.Append('\n').Append(string.IsNullOrWhiteSpace(parts[i].displayName)
                        ? parts[i].prefab.name : parts[i].displayName).Append(" ×").Append(counts[i]);
            description = text.ToString();
            interactionDistance = 4f;
        }

        // Diagnosis appears on aim. Installing parts will be a separate step.
        public override void Interact() { }
    }
}
