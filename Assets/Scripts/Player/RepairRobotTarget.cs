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
        sealed class Slot { public GameObject visual; public int part; }
        readonly List<Slot> slots = new List<Slot>();
        RepairPartDefinition[] definitions;
        bool initialized;
        public bool IsBeingCollected { get; private set; }
        public int MissingCount
        {
            get
            {
                int count = 0;
                foreach (var slot in slots) if (!slot.visual || !slot.visual.activeSelf) count++;
                return count;
            }
        }
        public bool IsRepaired => initialized && MissingCount == 0;
        public bool IsSettled
        {
            get
            {
                var body = GetComponent<Rigidbody>();
                return !body || body.isKinematic;
            }
        }
        public override string Prompt
        {
            get
            {
                if (!initialized) return Loc.T("Robot diagnostics…");
                if (IsRepaired) return Loc.T(IsSettled ? "Robot repaired. You can call the crane." : "Robot repaired. Waiting for landing.");
                var counts = new int[definitions.Length];
                foreach (var slot in slots)
                    if (!slot.visual || !slot.visual.activeSelf) counts[slot.part]++;
                var text = new StringBuilder(Loc.T("Missing parts:"));
                for (int i = 0; i < counts.Length; i++)
                    if (counts[i] > 0)
                        text.Append('\n').Append(string.IsNullOrWhiteSpace(definitions[i].displayName)
                            ? definitions[i].prefab.name : definitions[i].displayName).Append(" ×").Append(counts[i]);
                return text.ToString();
            }
        }

        public void Initialize(RepairPartDefinition[] parts, int minimum, int maximum, int seed)
        {
            definitions = parts;
            slots.Clear();
            initialized = false;
            IsBeingCollected = false;
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
            if (slots.Count == 0) throw new InvalidOperationException("Robot has no part slots matching Prefabs/Parts.");
            foreach (int index in RepairDamagePlan.Select(slots.Count, minimum, maximum, seed))
                slots[index].visual.SetActive(false);
            initialized = true;
            interactionDistance = 4f;
        }

        public bool TryInstallPart(GameObject partPrefab)
        {
            if (!initialized || IsBeingCollected || !IsSettled || !partPrefab) return false;
            foreach (var slot in slots)
                if (slot.visual && !slot.visual.activeSelf && definitions[slot.part].prefab == partPrefab)
                {
                    slot.visual.SetActive(true);
                    return true;
                }
            return false;
        }

        [ContextMenu("Debug/Repair All Parts")]
        public void RepairAllForTesting()
        {
            if (!Application.isPlaying || !initialized || IsBeingCollected || !IsSettled) return;
            foreach (var slot in slots) if (slot.visual) slot.visual.SetActive(true);
        }

        public bool TryReserveCollection()
        {
            if (!isActiveAndEnabled || !IsRepaired || !IsSettled || IsBeingCollected) return false;
            IsBeingCollected = true;
            SetHighlighted(false);
            return true;
        }
        public void CancelCollection() => IsBeingCollected = false;
        public override bool CanInteract(Camera camera) => base.CanInteract(camera) && !IsBeingCollected;
        public override void Interact() { }
    }
}
